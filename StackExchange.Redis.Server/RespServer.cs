
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
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
        }
        protected int TcpPort()
        {
            var ep = _listener?.LocalEndPoint;
            if (ep is IPEndPoint ip) return ip.Port;
            if (ep is DnsEndPoint dns) return dns.Port;
            return -1;
        }

        private Action<object> _runClientCallback;
        private Action<object> RunClientCallback => _runClientCallback ??
            (_runClientCallback = state => RunClient((RedisClient)state));

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
        protected virtual RedisClient CreateClient() => new RedisClient();

        public int ClientCount
        {
            get { lock (_clients) { return _clients.Count; } }
        }
        public int TotalClientCount { get; private set; }
        public void AddClient(RedisClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            lock (_clients)
            {
                ThrowIfShutdown();
                _clients.Add(client);
                TotalClientCount++;
            }
        }
        public bool RemoveClient(RedisClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
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

                    var pipe = SocketConnection.Create(client, sendOptions, receiveOptions);
                    var c = CreateClient();
                    c.LinkedPipe = pipe;
                    AddClient(c);
                    StartOnScheduler(receiveOptions.ReaderScheduler, RunClientCallback, c);
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if(!_isShutdown) Log("Listener faulted: " + ex.Message);
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

        async void RunClient(RedisClient client)
        {
            ThrowIfShutdown();

            var input = client?.LinkedPipe?.Input;
            var output = client?.LinkedPipe?.Output;
            if (input == null || output == null) return; // nope


            Exception fault = null;
            try
            {
                while (!client.Closed)
                {
                    var readResult = await input.ReadAsync();
                    var buffer = readResult.Buffer;

                    bool makingProgress = false;
                    while (!client.Closed && TryProcessRequest(ref buffer, client, output))
                    {
                        makingProgress = true;
                        await output.FlushAsync();
                    }
                    input.AdvanceTo(buffer.Start, buffer.End);

                    if (!makingProgress && readResult.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (ConnectionResetException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { fault = ex; }
            finally
            {
                try { input.Complete(fault); } catch { }
                try { output.Complete(fault); } catch { }

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
        private Encoder _serverEncoder = Encoding.UTF8.GetEncoder();

        static Encoder s_sharedEncoder; // swapped in/out to avoid alloc on the public WriteResponse API
        public static void WriteResponse(PipeWriter output, RedisResult response)
        {
            var enc = Interlocked.Exchange(ref s_sharedEncoder, null) ?? Encoding.UTF8.GetEncoder();
            WriteResponse(output, response, enc);
            Interlocked.Exchange(ref s_sharedEncoder, enc);
        }
        internal static void WriteResponse(PipeWriter output, RedisResult response, Encoder encoder)
        {
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
                    var span = output.GetSpan(1);
                    span[0] = (byte)prefix;
                    output.Advance(1);

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
                            WriteResponse(output, arr[i], encoder);
                        }
                    }
                    break;
            }
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
        bool TryProcessRequest(ref ReadOnlySequence<byte> buffer, RedisClient client, PipeWriter output)
        {
            if (!buffer.IsEmpty && TryParseRequest(ref buffer, out var request))
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                {
                    request.Recycle();
                    return true;
                }
                
                RedisResult response;
                try { response = Execute(client, request); }
                finally { request.Recycle(); }
                WriteResponse(output, response, _serverEncoder);
                return true;
            }
            return false;
        }

        private object ServerSyncLock => this;

        private long _commandsProcesed;
        public long CommandsProcesed => _commandsProcesed;

        public RedisResult Execute(RedisClient client, RedisRequest request)
        {
            Interlocked.Increment(ref _commandsProcesed);
            try
            {
                // try to executte without needing a server lock
                var result = ConnectionExecute(client, request);
                if (result != null) return result;

                // but no problem taking the lock when needed (most of the time)
                lock (ServerSyncLock)
                {        
                    result = ServerExecute(client, request);
                    if (result == null) Log($"missing command: '{request.Command}'");
                    return result ?? CommandNotFound(request.Command);
                }
            }
            catch (NotImplementedException)
            {
                Log($"missing command: '{request.Command}'");
                return CommandNotFound(request.Command);
            }
            catch (Exception ex)
            {
                if(!_isShutdown) Log(ex.Message);
                return RedisResult.Create("ERR " + ex.Message, ResultType.Error);
            }
        }
        public virtual RedisResult ConnectionExecute(RedisClient client, RedisRequest request) => null;
        public abstract RedisResult ServerExecute(RedisClient client, RedisRequest request);

        internal static string ToLower(RawResult value)
        {
            var val = value.GetString();
            if (string.IsNullOrWhiteSpace(val)) return val;
            return val.ToLowerInvariant();
        }
        //{

        //    {
        //        try
        //        {
        //            var args = command.GetItemsMemory();
        //            var span = args.Span;
        //            var cmdName = span[0].GetString();
        //            RedisResult result;
        //            if (s_commands.TryGetValue(cmdName, out var callback))
        //            {
        //                lock (server.ServerSyncLock)
        //                {
        //                    result = callback(server, client, args);
        //                }
        //            }
        //            else result = null;

        //            if (result == null)
        //            {
        //                var sb = new System.Text.StringBuilder("\t").Append(span[0].GetString());
        //                for (int i = 1; i < span.Length; i++)
        //                {
        //                    sb.Append(' ');
        //                    sb.Append(span[i].GetString());
        //                }
        //                var line = sb.ToString();
        //                server.Log(line);
        //                result = CommandNotFound(cmdName);
        //            }
        //            return result;
        //        }
        //        catch (Exception ex)
        //        {

        //        }
        //    }

        protected static RedisResult CommandNotFound(string command)
            => RedisResult.Create($"ERR unknown command '{command}'", ResultType.Error);
    }
}
