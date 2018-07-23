
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis.Server
{
    public abstract partial class RespServer : IDisposable
    {
        private readonly List<RedisClient> _clients = new List<RedisClient>();
        private readonly TextWriter _output;
        private Socket _listener;
        public RespServer(TextWriter output = null)
        {
            _output = output;
            _commands = BuildCommands(this);
        }

        private static Dictionary<string, RespCommand> BuildCommands(RespServer server)
        {
            RedisCommandAttribute CheckSignatureAndGetAttribute(MethodInfo method)
            {
                if (method.ReturnType != typeof(RedisResult)) return null;
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

            var result = new Dictionary<string, RespCommand>(StringComparer.OrdinalIgnoreCase);
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
        protected virtual void AppendStats(StringBuilder sb)
        {
            sb.Append("Current clients:\t").Append(ClientCount).AppendLine()
                .Append("Total clients:\t").Append(TotalClientCount).AppendLine()
                .Append("Total operations:\t").Append(TotalCommandsProcesed).AppendLine()
                .Append("Error replies:\t").Append(TotalErrorCount).AppendLine();
        }

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
        private readonly Dictionary<string, RespCommand> _commands;

        readonly struct RespCommand
        {
            public RespCommand(RedisCommandAttribute attrib, MethodInfo method, RespServer server)
            {
                _operation = (RespOperation)Delegate.CreateDelegate(typeof(RespOperation), server, method);
                Command = (string.IsNullOrWhiteSpace(attrib.Command) ? method.Name : attrib.Command).Trim().ToLowerInvariant();
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
            readonly RespOperation _operation;

            private readonly RespCommand[] _subcommands;
            public bool HasSubCommands => _subcommands != null;
            internal RespCommand WithSubCommands(RespCommand[] subs)
                => new RespCommand(this, subs);
            private RespCommand(RespCommand parent, RespCommand[] subs)
            {
                if (parent.IsSubCommand) throw new InvalidOperationException("Cannot have nested sub-commands");
                if (parent.HasSubCommands) throw new InvalidOperationException("Already has sub-commands");
                if (subs == null || subs.Length == 0) throw new InvalidOperationException("Cannot add empty sub-commands");

                Command = parent.Command ?? subs[0].Command;
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
            public RedisResult Execute(RedisClient client, RedisRequest request)
            {
                var args = request.Count;
                if (!CheckArity(request.Count)) return IsSubCommand
                        ? request.UnknownSubcommandOrArgumentCount()
                        : request.WrongArgCount();

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
        delegate RedisResult RespOperation(RedisClient client, RedisRequest request);

        protected int TcpPort()
        {
            var ep = _listener?.LocalEndPoint;
            if (ep is IPEndPoint ip) return ip.Port;
            if (ep is DnsEndPoint dns) return dns.Port;
            return -1;
        }

        private Action<object> _runClientCallback;
        // KeepAlive here just to make the compiler happy that we've done *something* with the task
        private Action<object> RunClientCallback => _runClientCallback ??
            (_runClientCallback = state => GC.KeepAlive(RunClient((IDuplexPipe)state)));

        public void Listen(
            EndPoint endpoint,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp,
            PipeOptions sendOptions = null, PipeOptions receiveOptions = null)
        {
            Socket listener = new Socket(addressFamily, socketType, protocolType);
            listener.Bind(endpoint);
            listener.Listen(20);

            _listener = listener;
            StartOnScheduler(receiveOptions?.ReaderScheduler, _ => ListenForConnections(
                sendOptions ?? PipeOptions.Default, receiveOptions ?? PipeOptions.Default), null);

            Log("Server is listening on " + Format.ToString(endpoint));
        }

        private static void StartOnScheduler(PipeScheduler scheduler, Action<object> callback, object state)
        {
            if (scheduler == PipeScheduler.Inline) scheduler = null;
            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }
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
        private async void ListenForConnections(PipeOptions sendOptions, PipeOptions receiveOptions)
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptAsync();
                    SocketConnection.SetRecommendedServerOptions(client);
                    var pipe = SocketConnection.Create(client, sendOptions, receiveOptions);
                    StartOnScheduler(receiveOptions.ReaderScheduler, RunClientCallback, pipe);
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_isShutdown) Log("Listener faulted: " + ex.Message);
            }
        }

        private readonly TaskCompletionSource<int> _shutdown = new TaskCompletionSource<int>();
        private bool _isShutdown;
        protected void ThrowIfShutdown()
        {
            if (_isShutdown) throw new InvalidOperationException("The server is shutting down");
        }
        protected void DoShutdown(PipeScheduler scheduler = null)
        {
            if (_isShutdown) return;
            Log("Server shutting down...");
            _isShutdown = true;
            lock (_clients)
            {
                foreach (var client in _clients) client.Dispose();
                _clients.Clear();
            }
            StartOnScheduler(scheduler,
                state => ((TaskCompletionSource<int>)state).TrySetResult(0), _shutdown);
        }
        public Task Shutdown => _shutdown.Task;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            DoShutdown();
            var socket = _listener;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
        }

        public async Task RunClient(IDuplexPipe pipe)
        {
            Exception fault = null;
            RedisClient client = null;
            try
            {
                client = AddClient();
                while (!client.Closed)
                {
                    var readResult = await pipe.Input.ReadAsync();
                    var buffer = readResult.Buffer;

                    bool makingProgress = false;
                    while (!client.Closed && await TryProcessRequestAsync(ref buffer, client, pipe.Output))
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
            catch (Exception ex) { fault = ex; }
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
        private void Log(string message)
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

        static Encoder s_sharedEncoder; // swapped in/out to avoid alloc on the public WriteResponse API
        public static ValueTask WriteResponseAsync(RedisClient client, PipeWriter output, RedisResult response)
        {
            async ValueTask Awaited(ValueTask wwrite, Encoder eenc)
            {
                await wwrite;
                Interlocked.Exchange(ref s_sharedEncoder, eenc);
            }
            var enc = Interlocked.Exchange(ref s_sharedEncoder, null) ?? Encoding.UTF8.GetEncoder();
            var write = WriteResponseAsync(client, output, response, enc);
            if (!write.IsCompletedSuccessfully) return Awaited(write, enc);
            Interlocked.Exchange(ref s_sharedEncoder, enc);
            return default;
        }

        internal static async ValueTask WriteResponseAsync(RedisClient client, PipeWriter output, RedisResult response, Encoder encoder)
        {
            void WritePrefix(PipeWriter ooutput, char pprefix)
            {
                var span = ooutput.GetSpan(1);
                span[0] = (byte)pprefix;
                ooutput.Advance(1);
            }

            if (response == null) return; // not actually a request (i.e. empty/whitespace request)
            if (client != null && client.ShouldSkipResponse()) return; // intentionally skipping the result
            char prefix;
            switch (response.Type)
            {
                case ResultType.Integer:
                    PhysicalConnection.WriteInteger(output, (long)response);
                    break;
                case ResultType.Error:
                    prefix = '-';
                    goto BasicMessage;
                case ResultType.SimpleString:
                    prefix = '+';
                    BasicMessage:
                    WritePrefix(output, prefix);

                    var val = response.AsString();

                    var expectedLength = Encoding.UTF8.GetByteCount(val);
                    PhysicalConnection.WriteRaw(output, val, expectedLength, encoder);
                    PhysicalConnection.WriteCrlf(output);
                    break;
                case ResultType.BulkString:
                    PhysicalConnection.WriteBulkString(response.AsRedisValue(), output, encoder);
                    break;
                case ResultType.MultiBulk:
                    if (response.IsNull)
                    {
                        PhysicalConnection.WriteMultiBulkHeader(output, -1);
                    }
                    else
                    {
                        var arr = (RedisResult[])response;
                        PhysicalConnection.WriteMultiBulkHeader(output, arr.Length);
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var item = arr[i];
                            if (item == null)
                                throw new InvalidOperationException("Array element cannot be null, index " + i);

                            // note: don't pass client down; this would impact SkipReplies
                            await WriteResponseAsync(null, output, item, encoder);
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unexpected result type: " + response.Type);
            }
            await output.FlushAsync();
        }
        public static bool TryParseRequest(ref ReadOnlySequence<byte> buffer, out RedisRequest request)
        {
            var reader = new BufferReader(buffer);
            var raw = PhysicalConnection.TryParseResult(in buffer, ref reader, false, null, true);
            if (raw.HasValue)
            {
                buffer = reader.SliceFromCurrent();
                request = new RedisRequest(raw);
                return true;
            }
            request = default;

            return false;
        }
        public ValueTask<bool> TryProcessRequestAsync(ref ReadOnlySequence<byte> buffer, RedisClient client, PipeWriter output)
        {
            async ValueTask<bool> Awaited(ValueTask wwrite)
            {
                await wwrite;
                return true;
            }
            if (!buffer.IsEmpty && TryParseRequest(ref buffer, out var request))
            {
                RedisResult response;
                try { response = Execute(client, request); }
                finally { request.Recycle(); }
                var write = WriteResponseAsync(client, output, response);
                if (!write.IsCompletedSuccessfully) return Awaited(write);
                return new ValueTask<bool>(true);
            }
            return new ValueTask<bool>(false);
        }

        protected object ServerSyncLock => this;

        private long _totalCommandsProcesed, _totalErrorCount;
        public long TotalCommandsProcesed => _totalCommandsProcesed;
        public long TotalErrorCount => _totalErrorCount;

        public RedisResult Execute(RedisClient client, RedisRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Command)) return null; // not a request
            Interlocked.Increment(ref _totalCommandsProcesed);
            try
            {
                RedisResult result;
                if (_commands.TryGetValue(request.Command, out var cmd))
                {
                    request = request.AsCommand(cmd.Command); // fixup casing
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
                    result = null;
                }

                if (result == null) Log($"missing command: '{request.Command}'");
                else if (result.Type == ResultType.Error) Interlocked.Increment(ref _totalErrorCount);
                return result ?? CommandNotFound(request.Command);
            }
            catch (NotSupportedException)
            {
                Log($"missing command: '{request.Command}'");
                return CommandNotFound(request.Command);
            }
            catch (NotImplementedException)
            {
                Log($"missing command: '{request.Command}'");
                return CommandNotFound(request.Command);
            }
            catch (InvalidCastException)
            {
                return RedisResult.Create("WRONGTYPE Operation against a key holding the wrong kind of value", ResultType.Error);
            }
            catch (Exception ex)
            {
                if (!_isShutdown) Log(ex.Message);
                return RedisResult.Create("ERR " + ex.Message, ResultType.Error);
            }
        }

        internal static string ToLower(RawResult value)
        {
            var val = value.GetString();
            if (string.IsNullOrWhiteSpace(val)) return val;
            return val.ToLowerInvariant();
        }

        protected static RedisResult CommandNotFound(string command)
            => RedisResult.Create($"ERR unknown command '{command}'", ResultType.Error);

        [RedisCommand(1, LockFree = true)]
        protected virtual RedisResult Command(RedisClient client, RedisRequest request)
        {
            var results = new RedisResult[_commands.Count];
            int index = 0;
            foreach (var pair in _commands)
                results[index++] = CommandInfo(pair.Value);
            return RedisResult.Create(results);
        }

        [RedisCommand(-2, "command", "info", LockFree = true)]
        protected virtual RedisResult CommandInfo(RedisClient client, RedisRequest request)
        {
            var results = new RedisResult[request.Count - 2];
            for (int i = 2; i < request.Count; i++)
            {
                results[i - 2] = _commands.TryGetValue(request.GetString(i), out var cmd)
                    ? CommandInfo(cmd) : null;
            }
            return RedisResult.Create(results);
        }
        private RedisResult CommandInfo(RespCommand command)
            => RedisResult.Create(new[]
            {
                RedisResult.Create(command.Command, ResultType.BulkString),
                RedisResult.Create(command.NetArity(), ResultType.Integer),
                RedisResult.EmptyArray,
                RedisResult.Zero,
                RedisResult.Zero,
                RedisResult.Zero,
            });
    }
}
