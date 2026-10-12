using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Transports;
using StackExchange.Redis.Configuration;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class TransportCloseTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PeerCloseFailsOutstandingCommandsAndRaisesConnectionFailed()
    {
        using var server = new InProcessTestServer(output);
        var tunnel = new StreamTransportTunnel(server.Tunnel);
        var config = server.GetClientConfig(withPubSub: false, defaultOnly: true);
        config.Tunnel = tunnel;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var failed = new TaskCompletionSource<ConnectionFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.ConnectionFailed += (_, e) => failed.TrySetResult(e);

        var db = conn.GetDatabase();
        await db.PingAsync();

        // The next command is written to the transport but never reaches the server, so it is still
        // awaiting its reply when the server closes the connection.
        var transport = tunnel.Interactive!;
        transport.DropWrites();
        var pending = db.PingAsync();
        await transport.Dropped.WaitAsync(TimeSpan.FromSeconds(5));
        transport.CloseAsIfByPeer();

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(() => pending);
        Assert.Equal(ConnectionFailureType.SocketClosed, ex.FailureType);

        var args = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionFailureType.SocketClosed, args.FailureType);
    }

    /// <summary>Connects through the in-process server's tunnel, but hands the client a <see cref="DuplexTransport"/>
    /// over that stream instead of the stream itself.</summary>
    private sealed class StreamTransportTunnel(Tunnel inner) : Tunnel
    {
        public StreamTransport? Interactive { get; private set; }

        public override async ValueTask<DuplexTransport?> ConnectTransportAsync(
            EndPoint endpoint, ConnectionType connectionType, TlsOptions tls, CancellationToken cancellationToken)
        {
            var stream = await inner.BeforeAuthenticateAsync(endpoint, connectionType, null, cancellationToken)
                ?? throw new InvalidOperationException($"No in-process node at {endpoint}");
            var transport = new StreamTransport(stream);
            if (connectionType == ConnectionType.Interactive) Interactive = transport;
            return transport;
        }
    }

    /// <summary>A transport over a stream, with two levers for the test: drop what is written, and close the way
    /// a transport does when its peer goes away - <see cref="TransportReceiver.OnClosed"/> with no fault.</summary>
    private sealed class StreamTransport(Stream stream) : DuplexTransport
    {
        private readonly CancellationTokenSource _peerClosed = new();
        private readonly TaskCompletionSource<bool> _dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[] _staged = new byte[1024];
        private int _stagedCount, _closed;
        private volatile bool _dropWrites;
        private TransportReceiver? _receiver;

        public Task<bool> Dropped => _dropped.Task;

        public void DropWrites() => _dropWrites = true;

        public void CloseAsIfByPeer() => _peerClosed.Cancel();

        public override Memory<byte> GetMemory(int sizeHint = 0) => Reserve(sizeHint).AsMemory(_stagedCount);

        public override void Advance(int count) => _stagedCount += count;

        public override bool Flush()
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                _stagedCount = 0;
                return false;
            }

            if (_stagedCount != 0)
            {
                if (_dropWrites)
                {
                    _dropped.TrySetResult(true);
                }
                else
                {
                    stream.Write(_staged, 0, _stagedCount);
                    stream.Flush();
                }
                _stagedCount = 0;
            }
            return true;
        }

        public override void Start(TransportReceiver receiver)
        {
            _receiver = receiver;
            _ = Task.Run(ReceiveAsync);
        }

        public override ValueTask DisposeAsync()
        {
            _peerClosed.Cancel();
            return default;
        }

        private async Task ReceiveAsync()
        {
            var buffer = new byte[4096];
            Exception? fault = null;
            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, _peerClosed.Token);
                    if (read <= 0 || !_receiver!.OnReceived(new ReadOnlySpan<byte>(buffer, 0, read))) break;
                    _receiver.OnBatchEnd();
                }
            }
            catch (OperationCanceledException) when (_peerClosed.IsCancellationRequested)
            {
                // a clean close, as when the server ends the connection
            }
            catch (Exception ex)
            {
                fault = ex;
            }

            Volatile.Write(ref _closed, 1);
            stream.Dispose();
            _receiver!.OnClosed(fault);
        }

        private byte[] Reserve(int sizeHint)
        {
            int needed = _stagedCount + Math.Max(sizeHint, 1);
            if (needed > _staged.Length) Array.Resize(ref _staged, Math.Max(needed, _staged.Length * 2));
            return _staged;
        }
    }
}
