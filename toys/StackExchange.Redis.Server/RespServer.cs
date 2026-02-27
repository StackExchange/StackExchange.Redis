using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis.Server
{
    public abstract partial class RespServer : IDisposable
    {
        public enum ShutdownReason
        {
            ServerDisposed,
            ClientInitiated,
        }

        private readonly TextWriter _output;

        protected RespServer(TextWriter output = null)
        {
            _output = output;
            _commands = BuildCommands(this);
        }

        public HashSet<string> GetCommands()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _commands)
            {
                set.Add(kvp.Key.ToString());
            }
            return set;
        }

        private static Dictionary<AsciiHash, RespCommand> BuildCommands(RespServer server)
        {
            static RedisCommandAttribute CheckSignatureAndGetAttribute(MethodInfo method)
            {
                if (method.ReturnType != typeof(TypedRedisValue)) return null;
                var p = method.GetParameters();
                if (p.Length != 2 || p[0].ParameterType != typeof(RedisClient) || p[1].ParameterType != typeof(RedisRequest).MakeByRefType())
                    return null;
                return (RedisCommandAttribute)Attribute.GetCustomAttribute(method, typeof(RedisCommandAttribute));
            }

            var grouped = (
                    from method in server.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    let attrib = CheckSignatureAndGetAttribute(method)
                    where attrib != null
                    select new RespCommand(attrib, method, server))
                .GroupBy(x => new AsciiHash(x.Command.ToUpperInvariant()), AsciiHash.CaseSensitiveEqualityComparer);

            var result = new Dictionary<AsciiHash, RespCommand>(AsciiHash.CaseSensitiveEqualityComparer);
            foreach (var grp in grouped)
            {
                RespCommand parent;
                if (grp.Any(x => x.IsSubCommand))
                {
                    var subs = grp.Where(x => x.IsSubCommand).ToArray();
                    parent = grp.SingleOrDefault(x => !x.IsSubCommand).WithSubCommands(subs);
                }
                else
                {
                    parent = grp.Single();
                }

                Debug.WriteLine($"Registering: {grp.Key}");
                result.Add(grp.Key, parent);
            }
            return result;
        }

        public string GetStats()
        {
            var sb = new StringBuilder();
            AppendStats(sb);
            return sb.ToString();
        }

        protected virtual void AppendStats(StringBuilder sb) =>
            sb.Append("Current clients:\t").Append(ClientCount).AppendLine()
              .Append("Total clients:\t").Append(TotalClientCount).AppendLine()
              .Append("Total operations:\t").Append(TotalCommandsProcesed).AppendLine()
              .Append("Error replies:\t").Append(TotalErrorCount).AppendLine();

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
        protected sealed class RedisCommandAttribute : Attribute
        {
            public RedisCommandAttribute(
                int arity,
                string command = null,
                string subcommand = null)
            {
                Command = command;
                SubCommand = subcommand;
                Arity = arity;
                MaxArgs = Arity > 0 ? Arity : int.MaxValue;
            }
            public int MaxArgs { get; set; }
            public string Command { get; }
            public string SubCommand { get; }
            public int Arity { get; }
            public bool LockFree { get; set; }
        }
        private readonly Dictionary<AsciiHash, RespCommand> _commands;

        private readonly struct RespCommand
        {
            public RespCommand(RedisCommandAttribute attrib, MethodInfo method, RespServer server)
            {
                _operation = (RespOperation)Delegate.CreateDelegate(typeof(RespOperation), server, method);

                var command = attrib.Command;
                if (string.IsNullOrEmpty(command)) command = method.Name;

                Command = command;
                SubCommand = attrib.SubCommand?.Trim()?.ToLowerInvariant();
                Arity = attrib.Arity;
                MaxArgs = attrib.MaxArgs;
                LockFree = attrib.LockFree;
                _subcommands = null;
            }
            public string Command { get; }
            public string SubCommand { get; }
            public bool IsSubCommand => !string.IsNullOrEmpty(SubCommand);
            public int Arity { get; }
            public int MaxArgs { get; }
            public bool LockFree { get; }
            private readonly RespOperation _operation;

            private readonly RespCommand[] _subcommands;
            public bool HasSubCommands => _subcommands != null;
            internal RespCommand WithSubCommands(RespCommand[] subs)
                => new RespCommand(this, subs);
            private RespCommand(in RespCommand parent, RespCommand[] subs)
            {
                if (parent.IsSubCommand) throw new InvalidOperationException("Cannot have nested sub-commands");
                if (parent.HasSubCommands) throw new InvalidOperationException("Already has sub-commands");
                if (subs == null || subs.Length == 0) throw new InvalidOperationException("Cannot add empty sub-commands");

                Command = parent.Command;
                SubCommand = parent.SubCommand;
                Arity = parent.Arity;
                MaxArgs = parent.MaxArgs;
                LockFree = parent.LockFree;
                _operation = parent._operation;
                _subcommands = subs;
            }
            public bool IsUnknown => _operation == null;

            public RespCommand Resolve(in RedisRequest request)
            {
                if (request.Count >= 2)
                {
                    var subs = _subcommands;
                    if (subs != null)
                    {
                        var subcommand = request.GetString(1);
                        for (int i = 0; i < subs.Length; i++)
                        {
                            if (string.Equals(subcommand, subs[i].SubCommand, StringComparison.OrdinalIgnoreCase))
                                return subs[i];
                        }
                    }
                }
                return this;
            }
            public TypedRedisValue Execute(RedisClient client, in RedisRequest request)
            {
                var args = request.Count;
                if (!CheckArity(request.Count))
                {
                    return IsSubCommand
                           ? request.UnknownSubcommandOrArgumentCount()
                           : request.WrongArgCount();
                }

                return _operation(client, request);
            }
            private bool CheckArity(int count)
                => count <= MaxArgs && (Arity <= 0 ? count >= -Arity : count == Arity);

            internal int NetArity()
            {
                if (!HasSubCommands) return Arity;

                var minMagnitude = _subcommands.Min(x => Math.Abs(x.Arity));
                bool varadic = _subcommands.Any(x => x.Arity <= 0);
                if (!IsUnknown)
                {
                    minMagnitude = Math.Min(minMagnitude, Math.Abs(Arity));
                    if (Arity <= 0) varadic = true;
                }
                return varadic ? -minMagnitude : minMagnitude;
            }
        }

        private delegate TypedRedisValue RespOperation(RedisClient client, in RedisRequest request);

        // for extensibility, so that a subclass can get their own client type
        // to be used via ListenForConnections
        public virtual RedisClient CreateClient(RedisServer.Node node) => new(node);

        public virtual void OnClientConnected(RedisClient client, object state) { }

        public int ClientCount => _clientLookup.Count;
        public int TotalClientCount => _totalClientCount;
        private int _nextId, _totalClientCount;

        public RedisClient AddClient(RedisServer.Node node, object state)
        {
            var client = CreateClient(node);
            client.Id = Interlocked.Increment(ref _nextId);
            Interlocked.Increment(ref _totalClientCount);
            ThrowIfShutdown();
            _clientLookup[client.Id] = client;
            OnClientConnected(client, state);
            return client;
        }

        public bool TryGetClient(int id, out RedisClient client) => _clientLookup.TryGetValue(id, out client);

        private readonly ConcurrentDictionary<int, RedisClient> _clientLookup = new();

        public bool RemoveClient(RedisClient client)
        {
            if (client == null) return false;
            client.Closed = true;
            return _clientLookup.TryRemove(client.Id, out _);
        }

        protected virtual void Touch(int database, in RedisKey key)
        {
            foreach (var client in _clientLookup.Values)
            {
                client.Touch(database, key);
            }
        }

        private readonly TaskCompletionSource<ShutdownReason> _shutdown = TaskSource.Create<ShutdownReason>(null, TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isShutdown;
        protected void ThrowIfShutdown()
        {
            if (_isShutdown) throw new InvalidOperationException("The server is shutting down");
        }
        protected void DoShutdown(ShutdownReason reason)
        {
            if (_isShutdown) return;
            Log("Server shutting down...");
            _isShutdown = true;
            foreach (var client in _clientLookup.Values) client.Dispose();
            _clientLookup.Clear();
            _shutdown.TrySetResult(reason);
        }
        public Task<ShutdownReason> Shutdown => _shutdown.Task;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            DoShutdown(ShutdownReason.ServerDisposed);
        }

        private readonly Arena _arena = new();

        public virtual RedisServer.Node DefaultNode => null;

        public async Task RunClientAsync(IDuplexPipe pipe, RedisServer.Node node = null, object state = null)
        {
            Exception fault = null;
            RedisClient client = null;
            byte[] commandLease = RedisRequest.GetLease();
            try
            {
                node ??= DefaultNode;
                client = AddClient(node, state);

                while (!client.Closed)
                {
                    var readResult = await pipe.Input.ReadAsync().ConfigureAwait(false);
                    var buffer = readResult.Buffer;

                    while (!client.Closed && client.TryReadRequest(buffer, out long consumed))
                    {
                        // process a completed request
                        RedisRequest request = new(buffer.Slice(0, consumed), ref commandLease);
                        request = request.WithClient(client);
                        var response = Execute(client, request);
                        client.ResetAfterRequest();

                        WriteResponse(client, pipe.Output, response, client.Protocol);
                        await pipe.Output.FlushAsync().ConfigureAwait(false);

                        // advance the buffer to account for the message we just read
                        buffer = buffer.Slice(consumed);
                    }

                    pipe.Input.AdvanceTo(buffer.Start, buffer.End);
                    if (readResult.IsCompleted) break; // EOF
                }
            }
            catch (ConnectionResetException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (ex.GetType().Name != nameof(ConnectionResetException))
                {
                    // aspnet core has one too; swallow it by pattern
                    fault = ex;
                    throw;
                }
            }
            finally
            {
                RedisRequest.ReleaseLease(ref commandLease);
                RemoveClient(client);
                try { pipe.Input.Complete(fault); } catch { }
                try { pipe.Output.Complete(fault); } catch { }

                if (fault != null && !_isShutdown)
                {
                    Log("Connection faulted (" + fault.GetType().Name + "): " + fault.Message);
                }
            }
        }
        public virtual void Log(string message)
        {
            var output = _output;
            if (output != null)
            {
                lock (output)
                {
                    output.WriteLine(message);
                }
            }
        }

        public static void WriteResponse(RedisClient client, IBufferWriter<byte> output, TypedRedisValue value, RedisProtocol protocol)
        {
            static void WritePrefix(IBufferWriter<byte> output, char prefix)
            {
                var span = output.GetSpan(1);
                span[0] = (byte)prefix;
                output.Advance(1);
            }

            if (value.IsNil) return; // not actually a request (i.e. empty/whitespace request)
            if (client != null && client.ShouldSkipResponse()) return; // intentionally skipping the result

            var type = value.Type;
            if (protocol is RedisProtocol.Resp2 & type is not RespPrefix.Null)
            {
                if (type is RespPrefix.VerbatimString)
                {
                    var s = (string)value.AsRedisValue();
                    if (s is { Length: >= 4 } && s[3] == ':')
                        value = TypedRedisValue.BulkString(s.Substring(4));
                }
                type = ToResp2(type);
            }
            RetryResp2:
            if (protocol is RedisProtocol.Resp3 && value.IsNullValueOrArray)
            {
                output.Write("_\r\n"u8);
            }
            else
            {
                char prefix;
                switch (type)
                {
                    case RespPrefix.Integer:
                        MessageWriter.WriteInteger(output, (long)value.AsRedisValue());
                        break;
                    case RespPrefix.SimpleError:
                        prefix = '-';
                        goto BasicMessage;
                    case RespPrefix.SimpleString:
                        prefix = '+';
                        BasicMessage:
                        WritePrefix(output, prefix);
                        var val = (string)value.AsRedisValue();
                        var expectedLength = Encoding.UTF8.GetByteCount(val);
                        MessageWriter.WriteRaw(output, val, expectedLength);
                        MessageWriter.WriteCrlf(output);
                        break;
                    case RespPrefix.BulkString:
                        MessageWriter.WriteBulkString(value.AsRedisValue(), output);
                        break;
                    case RespPrefix.Null:
                    case RespPrefix.Push when value.IsNullArray:
                    case RespPrefix.Map when value.IsNullArray:
                    case RespPrefix.Set when value.IsNullArray:
                    case RespPrefix.Attribute when value.IsNullArray:
                        output.Write("_\r\n"u8);
                        break;
                    case RespPrefix.Array when value.IsNullArray:
                        MessageWriter.WriteMultiBulkHeader(output, -1);
                        break;
                    case RespPrefix.Push:
                    case RespPrefix.Map:
                    case RespPrefix.Array:
                    case RespPrefix.Set:
                    case RespPrefix.Attribute:
                        var segment = value.Segment;
                        MessageWriter.WriteMultiBulkHeader(output, segment.Count, type);
                        var arr = segment.Array;
                        int offset = segment.Offset;
                        for (int i = 0; i < segment.Count; i++)
                        {
                            var item = arr[offset++];
                            if (item.IsNil)
                                throw new InvalidOperationException("Array element cannot be nil, index " + i);

                            // note: don't pass client down; this would impact SkipReplies
                            WriteResponse(null, output, item, protocol);
                        }
                        break;
                    default:
                        // retry with RESP2
                        var r2 = ToResp2(type);
                        if (r2 != type)
                        {
                            Debug.WriteLine($"{type} not handled in RESP3; using {r2} instead");
                            goto RetryResp2;
                        }

                        throw new InvalidOperationException(
                            "Unexpected result type: " + value.Type);
                }
            }

            static RespPrefix ToResp2(RespPrefix type)
            {
                switch (type)
                {
                    case RespPrefix.Boolean:
                        return RespPrefix.Integer;
                    case RespPrefix.Double:
                    case RespPrefix.BigInteger:
                        return RespPrefix.SimpleString;
                    case RespPrefix.BulkError:
                        return RespPrefix.SimpleError;
                    case RespPrefix.VerbatimString:
                        return RespPrefix.BulkString;
                    case RespPrefix.Map:
                    case RespPrefix.Set:
                    case RespPrefix.Push:
                    case RespPrefix.Attribute:
                        return RespPrefix.Array;
                    default: return type;
                }
            }
        }

        protected object ServerSyncLock => this;

        private long _totalCommandsProcesed, _totalErrorCount;
        public long TotalCommandsProcesed => _totalCommandsProcesed;
        public long TotalErrorCount => _totalErrorCount;

        public virtual void ResetCounters()
        {
            _totalCommandsProcesed = _totalErrorCount = _totalClientCount = 0;
        }

        public virtual TypedRedisValue OnUnknownCommand(in RedisClient client, in RedisRequest request, ReadOnlySpan<byte> command)
        {
            return TypedRedisValue.Nil;
        }

        public virtual TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (request.Count == 0 || request.Command.Length == 0) // not a request
            {
                client.ExecAbort();
                return request.CommandNotFound();
            }

            Interlocked.Increment(ref _totalCommandsProcesed);
            try
            {
                TypedRedisValue result;
                if (_commands.TryGetValue(request.Command, out var cmd))
                {
                    if (cmd.HasSubCommands)
                    {
                        cmd = cmd.Resolve(request);
                        if (cmd.IsUnknown)
                        {
                            client.ExecAbort();
                            return request.UnknownSubcommandOrArgumentCount();
                        }
                    }

                    if (client.BufferMulti(request, request.Command)) return TypedRedisValue.SimpleString("QUEUED");

                    if (cmd.LockFree)
                    {
                        result = cmd.Execute(client, request);
                    }
                    else
                    {
                        lock (ServerSyncLock)
                        {
                            result = cmd.Execute(client, request);
                        }
                    }
                }
                else
                {
                    client.ExecAbort();
                    result = OnUnknownCommand(client, request, request.Command.Span);
                }

                if (result.IsNil)
                {
                    Log($"missing command: '{request.GetString(0)}'");
                    return request.CommandNotFound();
                }

                if (result.IsError) Interlocked.Increment(ref _totalErrorCount);
                return result;
            }
            catch (KeyMovedException moved)
            {
                if (GetNode(moved.HashSlot) is { } node)
                {
                    OnMoved(client, moved.HashSlot, node);
                    return TypedRedisValue.Error($"MOVED {moved.HashSlot} {node.Host}:{node.Port}");
                }
                return TypedRedisValue.Error($"ERR key has been migrated from slot {moved.HashSlot}, but the new owner is unknown");
            }
            catch (CrossSlotException)
            {
                return TypedRedisValue.Error("CROSSSLOT Keys in request don't hash to the same slot");
            }
            catch (NotSupportedException)
            {
                Log($"missing command: '{request.GetString(0)}'");
                return request.CommandNotFound();
            }
            catch (NotImplementedException)
            {
                Log($"missing command: '{request.GetString(0)}'");
                return request.CommandNotFound();
            }
            catch (WrongTypeException)
            {
                return TypedRedisValue.Error("WRONGTYPE Operation against a key holding the wrong kind of value");
            }
            catch (InvalidCastException)
            {
                return TypedRedisValue.Error("WRONGTYPE Operation against a key holding the wrong kind of value");
            }
            catch (Exception ex)
            {
                if (!_isShutdown) Log(ex.Message);
                return TypedRedisValue.Error("ERR " + ex.Message);
            }
        }

        protected virtual void OnMoved(RedisClient client, int hashSlot, RedisServer.Node node)
        {
        }

        protected virtual RedisServer.Node GetNode(int hashSlot) => null;

        public sealed class KeyMovedException : Exception
        {
            private KeyMovedException(int hashSlot) => HashSlot = hashSlot;
            public int HashSlot { get; }
            public static void Throw(int hashSlot) => throw new KeyMovedException(hashSlot);
            public static void Throw(in RedisKey key) => throw new KeyMovedException(GetHashSlot(key));
        }

        public sealed class WrongTypeException : Exception
        {
        }

        protected internal static int GetHashSlot(in RedisKey key) => s_ClusterSelectionStrategy.HashSlot(key);
        private static readonly ServerSelectionStrategy s_ClusterSelectionStrategy = new(null) { ServerType = ServerType.Cluster };

        /*
        internal static string ToLower(in RawResult value)
        {
            var val = value.GetString();
            if (string.IsNullOrWhiteSpace(val)) return val;
            return val.ToLowerInvariant();
        }
        */

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Command(RedisClient client, in RedisRequest request)
        {
            var results = TypedRedisValue.Rent(_commands.Count, out var span, RespPrefix.Array);
            int index = 0;
            foreach (var pair in _commands)
                span[index++] = CommandInfo(pair.Value);
            return results;
        }

        [RedisCommand(-2, nameof(RedisCommand.COMMAND), "info", LockFree = true)]
        protected virtual TypedRedisValue CommandInfo(RedisClient client, in RedisRequest request)
        {
            var results = TypedRedisValue.Rent(request.Count - 2, out var span, RespPrefix.Array);
            for (int i = 2; i < request.Count; i++)
            {
                span[i - 2] = _commands.TryGetValue(request.Command, out var cmdInfo)
                    ? CommandInfo(cmdInfo) : TypedRedisValue.NullArray(RespPrefix.Array);
            }
            return results;
        }

        private TypedRedisValue CommandInfo(in RespCommand command)
        {
            var arr = TypedRedisValue.Rent(6, out var span, RespPrefix.Array);
            span[0] = TypedRedisValue.BulkString(command.Command);
            span[1] = TypedRedisValue.Integer(command.NetArity());
            span[2] = TypedRedisValue.EmptyArray(RespPrefix.Array);
            span[3] = TypedRedisValue.Zero;
            span[4] = TypedRedisValue.Zero;
            span[5] = TypedRedisValue.Zero;
            return arr;
        }
    }
}
