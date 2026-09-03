using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;

namespace StackExchange.Redis.MaintenanceSoak;

/// <summary>
/// Hosts the in-process RESP server on a real TCP socket.
/// </summary>
/// <remarks>
/// A real socket rather than the tunnel the test suite uses, deliberately: a soak is looking for what leaks
/// over thousands of cycles - sockets, pipes, buffers, event handlers - and the tunnel bypasses exactly the
/// machinery most likely to leak. It also means the connection has a genuine remote address, so the handoff's
/// endpoint-type derivation runs for real instead of resolving to "no address to classify".
/// </remarks>
internal sealed class SoakServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _accepting;

    public SoakServer(MemoryCacheRedisServer server, int port)
    {
        Server = server;
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
        _listener.Listen(64);
        EndPoint = (IPEndPoint)_listener.LocalEndPoint!;
        _accepting = AcceptLoopAsync();
    }

    public MemoryCacheRedisServer Server { get; }

    public IPEndPoint EndPoint { get; }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptAsync(_shutdown.Token);
            }
            catch (Exception) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"accept failed: {ex.Message}");
                continue;
            }

            _ = ServeAsync(socket);
        }
    }

    private async Task ServeAsync(Socket socket)
    {
        try
        {
            socket.NoDelay = true;
            using var stream = new NetworkStream(socket, ownsSocket: true);

            // one pipe each way, which is what RunClientAsync expects
            var input = PipeReader.Create(stream);
            var output = PipeWriter.Create(stream);
            await Server.RunClientAsync(new DuplexPipe(input, output));
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException or SocketException)
        {
            // an ordinary disconnect; the soak causes plenty of them on purpose
        }
        catch (Exception ex)
        {
            Console.WriteLine($"client faulted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            _listener.Dispose();
        }
        catch { }

        try
        {
            await _accepting;
        }
        catch { }

        Server.Dispose();
        _shutdown.Dispose();
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input => input;
        public PipeWriter Output => output;
    }
}
