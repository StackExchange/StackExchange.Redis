using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis.Tests
{
    public class FakeRedisServer : IDisposable
    {
        private readonly List<IDuplexPipe> _clients = new List<IDuplexPipe>();
        private readonly PipeOptions _sendOptions, _receiveOptions;
        private readonly TextWriter _output;
        private Socket _socket;

        public FakeRedisServer(TextWriter output = null, PipeOptions sendOptions = null, PipeOptions receiveOptions = null)
        {
            _sendOptions = sendOptions ?? PipeOptions.Default;
            _receiveOptions = receiveOptions ?? PipeOptions.Default;
            _output = output;

            m_RunClient = state => RunClient((IDuplexPipe)state);
        }

        public void Start(EndPoint endpoint,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp)
        {
            Socket listener = new Socket(addressFamily, socketType, protocolType);
            listener.Bind(endpoint);
            listener.Listen(20);

            _socket = listener;
            Schedule(server => ((FakeRedisServer)server).ListenForConnections(), this);
        }

        private void Schedule(Action<object> callback, object state)
        {
            var scheduler = _receiveOptions.ReaderScheduler;
            if (scheduler == PipeScheduler.Inline) scheduler = null;
            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }
        private async void ListenForConnections()
        {
            try
            {
                while (true)
                {
                    var client = await _socket.AcceptAsync();

                    var c = SocketConnection.Create(client, _sendOptions, _receiveOptions);
                    Schedule(m_RunClient, c);
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Log("Listener faulted: " + ex.Message);
            }
        }
        public void Dispose()
        {
            var socket = _socket;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
            lock (_clients)
            {
                foreach (var client in _clients) Dispose(client);
                _clients.Clear();
            }
        }
        static void Dispose(IDuplexPipe pipe)
        {
            if (pipe is IDisposable d) try { d.Dispose(); } catch { }
        }


        private readonly Action<object> m_RunClient;

        async void RunClient(IDuplexPipe pipe)
        {
            var input = pipe.Input;

            try
            {
                while (true)
                {
                    var readResult = await input.ReadAsync();
                    var buffer = readResult.Buffer;
                    int requestsHandled = TryProcessRequests(ref buffer, pipe.Output);
                    Log($"Processed {requestsHandled} requests");

                    input.AdvanceTo(buffer.Start, buffer.End);
                    if (requestsHandled == 0 && readResult.IsCompleted)
                    {
                        break;
                    }
                }
                Log($"Connection closed");
            }
            catch (Exception ex)
            {
                Log("Connection faulted: " + ex.Message);
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
        int TryProcessRequests(ref ReadOnlySequence<byte> buffer, PipeWriter output)
        {
            int messageCount = 0;

            while (!buffer.IsEmpty)
            {
                var reader = new BufferReader(buffer);
                var result = PhysicalConnection.TryParseResult(in buffer, ref reader, false, null);
                try
                {
                    if (result.HasValue)
                    {
                        buffer = reader.SliceFromCurrent();
                        messageCount++;
                        ProcessRequest(result, output);
                    }
                    else
                    {
                        break; // remaining buffer isn't enough; give up
                    }
                }
                finally
                {
                    result.Recycle();
                }
            }
            return messageCount;
        }

        private object ServerSyncLock => this;
        private void ProcessRequest(RawResult request, PipeWriter output)
        {
            string command;
            lock (ServerSyncLock)
            {
                var args = request.GetItems();
                command = args[0].GetString();
            }
            Log(command);

        }
    }
}
