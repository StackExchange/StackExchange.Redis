
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis.Server
{
    public abstract partial class RespServer : IDisposable
    {
        public enum ShutdownReason
        {
            ServerDisposed,
            ClientInitiated,
        }
        private readonly List<RedisClient> _clients = new List<RedisClient>();
        private readonly TextWriter _output;

        protected RespServer(TextWriter output = null)
        {
            _output = output;
            _commands = BuildCommands(this);
        }

        private static Dictionary<CommandBytes, RespCommand> BuildCommands(RespServer server)
        {
            static RedisCommandAttribute CheckSignatureAndGetAttribute(MethodInfo method)
            {
                if (method.ReturnType != typeof(TypedRedisValue)) return null;
                var p = method.GetParameters();
                if (p.Length != 2 || p[0].ParameterType != typeof(RedisClient) || p[1].ParameterType != typeof(RedisRequest))
                    return null;
                return (RedisCommandAttribute)Attribute.GetCustomAttribute(method, typeof(RedisCommandAttribute));
            }
            var grouped = from method in server.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          let attrib = CheckSignatureAndGetAttribute(method)
                          where attrib != null
                          select new RespCommand(attrib, method, server) into cmd
                          group cmd by cmd.Command;

            var result = new Dictionary<CommandBytes, RespCommand>();
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
                result.Add(new CommandBytes(grp.Key), parent);
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
            public RedisCommandAttribute(int arity,
                string command = null, string subcommand = null)
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
        private readonly Dictionary<CommandBytes, RespCommand> _commands;

        private readonly struct RespCommand
        {
            public RespCommand(RedisCommandAttribute attrib, MethodInfo method, RespServer server)
            {
                _operation = (RespOperation)Delegate.CreateDelegate(typeof(RespOperation), server, method);
                Command = (string.IsNullOrWhiteSpace(attrib.Command) ? method.Name : attrib.Command).Trim().ToLowerInvariant();
                CommandBytes = new CommandBytes(Command);
                SubCommand = attrib.SubCommand?.Trim()?.ToLowerInvariant();
                Arity = attrib.Arity;
                MaxArgs = attrib.MaxArgs;
                LockFree = attrib.LockFree;
                _subcommands = null;
            }
            private CommandBytes CommandBytes { get; }
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
                CommandBytes = parent.CommandBytes;
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

        private delegate TypedRedisValue RespOperation(RedisClient client, RedisRequest request);

        // for extensibility, so that a subclass can get their own client type
        // to be used via ListenForConnections
        public virtual RedisClient CreateClient() => new RedisClient();

        public int ClientCount
        {
            get { lock (_clients) { return _clients.Count; } }
        }
        public int TotalClientCount { get; private set; }
        private int _nextId;
        public RedisClient AddClient()
        {
            var client = CreateClient();
            lock (_clients)
            {
                ThrowIfShutdown();
                client.Id = ++_nextId;
                _clients.Add(client);
                TotalClientCount++;
            }
            return client;
        }
        public bool RemoveClient(RedisClient client)
        {
            if (client == null) return false;
            lock (_clients)
            {
                client.Closed = true;
                return _clients.Remove(client);
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
            lock (_clients)
            {
                foreach (var client in _clients) client.Dispose();
                _clients.Clear();
            }
            _shutdown.TrySetResult(reason);
        }
        public Task<ShutdownReason> Shutdown => _shutdown.Task;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            _arena.Dispose();
            DoShutdown(ShutdownReason.ServerDisposed);
        }

        public async Task RunClientAsync(IDuplexPipe pipe)
        {
            Exception fault = null;
            RedisClient client = null;
            try
            {
                client = AddClient();
                while (!client.Closed)
                {
                    var readResult = await pipe.Input.ReadAsync().ConfigureAwait(false);
                    var buffer = readResult.Buffer;

                    bool makingProgress = false;
                    while (!client.Closed && await TryProcessRequestAsync(ref buffer, client, pipe.Output).ConfigureAwait(false))
                    {
                        makingProgress = true;
                    }
                    pipe.Input.AdvanceTo(buffer.Start, buffer.End);

                    if (!makingProgress && readResult.IsCompleted)
                    { // nothing to do, and nothing more will be arriving
                        break;
                    }
                }
            }
            catch (ConnectionResetException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (ex.GetType().Name != nameof(ConnectionResetException))
                {
                    // aspnet core has one too; swallow it by pattern
                    fault = ex; throw;
                }
            }
            finally
            {
                RemoveClient(client);
                try { pipe.Input.Complete(fault); } catch { }
                try { pipe.Output.Complete(fault); } catch { }

                if (fault != null && !_isShutdown)
                {
                    Log("Connection faulted (" + fault.GetType().Name + "): " + fault.Message);
                }
            }
        }
        public void Log(string message)
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

        public static async ValueTask WriteResponseAsync(RedisClient client, PipeWriter output, TypedRedisValue value)
        {
            static void WritePrefix(PipeWriter ooutput, char pprefix)
            {
                var span = ooutput.GetSpan(1);
                span[0] = (byte)pprefix;
                ooutput.Advance(1);
            }

            if (value.IsNil) return; // not actually a request (i.e. empty/whitespace request)
            if (client != null && client.ShouldSkipResponse()) return; // intentionally skipping the result
            char prefix;
            switch (value.Type.ToResp2())
            {
                case ResultType.Integer:
                    PhysicalConnection.WriteInteger(output, (long)value.AsRedisValue());
                    break;
                case ResultType.Error:
                    prefix = '-';
                    goto BasicMessage;
                case ResultType.SimpleString:
                    prefix = '+';
                    BasicMessage:
                    WritePrefix(output, prefix);
                    var val = (string)value.AsRedisValue();
                    var expectedLength = Encoding.UTF8.GetByteCount(val);
                    PhysicalConnection.WriteRaw(output, val, expectedLength);
                    PhysicalConnection.WriteCrlf(output);
                    break;
                case ResultType.BulkString:
                    PhysicalConnection.WriteBulkString(value.AsRedisValue(), output);
                    break;
                case ResultType.Array:
                    if (value.IsNullArray)
                    {
                        PhysicalConnection.WriteMultiBulkHeader(output, -1);
                    }
                    else
                    {
                        var segment = value.Segment;
                        PhysicalConnection.WriteMultiBulkHeader(output, segment.Count);
                        var arr = segment.Array;
                        int offset = segment.Offset;
                        for (int i = 0; i < segment.Count; i++)
                        {
                            var item = arr[offset++];
                            if (item.IsNil)
                                throw new InvalidOperationException("Array element cannot be nil, index " + i);

                            // note: don't pass client down; this would impact SkipReplies
                            await WriteResponseAsync(null, output, item);
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unexpected result type: " + value.Type);
            }
            await output.FlushAsync().ConfigureAwait(false);
        }

        private static bool TryParseRequest(Arena<RawResult> arena, ref ReadOnlySequence<byte> buffer, out RedisRequest request)
        {
            var reader = new BufferReader(buffer);
            var raw = PhysicalConnection.TryParseResult(false, arena, in buffer, ref reader, false, null, true);
            if (raw.HasValue)
            {
                buffer = reader.SliceFromCurrent();
                request = new RedisRequest(raw);
                return true;
            }
            request = default;

            return false;
        }

        private readonly Arena<RawResult> _arena = new Arena<RawResult>();

        public ValueTask<bool> TryProcessRequestAsync(ref ReadOnlySequence<byte> buffer, RedisClient client, PipeWriter output)
        {
            static async ValueTask<bool> Awaited(ValueTask wwrite, TypedRedisValue rresponse)
            {
                await wwrite;
                rresponse.Recycle();
                return true;
            }
            if (!buffer.IsEmpty && TryParseRequest(_arena, ref buffer, out var request))
            {
                TypedRedisValue response;
                try { response = Execute(client, request); }
                finally { _arena.Reset(); }

                var write = WriteResponseAsync(client, output, response);
                if (!write.IsCompletedSuccessfully) return Awaited(write, response);
                response.Recycle();
                return new ValueTask<bool>(true);
            }
            return new ValueTask<bool>(false);
        }

        protected object ServerSyncLock => this;

        private long _totalCommandsProcesed, _totalErrorCount;
        public long TotalCommandsProcesed => _totalCommandsProcesed;
        public long TotalErrorCount => _totalErrorCount;

        public TypedRedisValue Execute(RedisClient client, RedisRequest request)
        {
            if (request.Count == 0) return default;// not a request

            if (!request.TryGetCommandBytes(0, out var cmdBytes)) return request.CommandNotFound();
            if (cmdBytes.Length == 0) return default; // not a request
            Interlocked.Increment(ref _totalCommandsProcesed);
            try
            {
                TypedRedisValue result;
                if (_commands.TryGetValue(cmdBytes, out var cmd))
                {
                    if (cmd.HasSubCommands)
                    {
                        cmd = cmd.Resolve(request);
                        if (cmd.IsUnknown) return request.UnknownSubcommandOrArgumentCount();
                    }
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
                    result = TypedRedisValue.Nil;
                }

                if (result.IsNil)
                {
                    Log($"missing command: '{request.GetString(0)}'");
                    return request.CommandNotFound();
                }
                if (result.Type == ResultType.Error) Interlocked.Increment(ref _totalErrorCount);
                return result;
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

        internal static string ToLower(in RawResult value)
        {
            var val = value.GetString();
            if (string.IsNullOrWhiteSpace(val)) return val;
            return val.ToLowerInvariant();
        }

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Command(RedisClient client, RedisRequest request)
        {
            var results = TypedRedisValue.Rent(_commands.Count, out var span);
            int index = 0;
            foreach (var pair in _commands)
                span[index++] = CommandInfo(pair.Value);
            return results;
        }

        [RedisCommand(-2, "command", "info", LockFree = true)]
        protected virtual TypedRedisValue CommandInfo(RedisClient client, RedisRequest request)
        {
            var results = TypedRedisValue.Rent(request.Count - 2, out var span);
            for (int i = 2; i < request.Count; i++)
            {
                span[i - 2] = request.TryGetCommandBytes(i, out var cmdBytes)
                    &&_commands.TryGetValue(cmdBytes, out var cmdInfo)
                    ? CommandInfo(cmdInfo) : TypedRedisValue.NullArray;
            }
            return results;
        }
        private TypedRedisValue CommandInfo(RespCommand command)
        {
            var arr = TypedRedisValue.Rent(6, out var span);
            span[0] = TypedRedisValue.BulkString(command.Command);
            span[1] = TypedRedisValue.Integer(command.NetArity());
            span[2] = TypedRedisValue.EmptyArray;
            span[3] = TypedRedisValue.Zero;
            span[4] = TypedRedisValue.Zero;
            span[5] = TypedRedisValue.Zero;
            return arr;
        }
    }
}
