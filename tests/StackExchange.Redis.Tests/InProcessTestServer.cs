using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using StackExchange.Redis.Configuration;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class InProcessTestServer : MemoryCacheRedisServer
{
    public Tunnel Tunnel { get; }

    private readonly ITestOutputHelper? _log;
    public InProcessTestServer(ITestOutputHelper? log = null)
    {
        _log = log;
        // ReSharper disable once VirtualMemberCallInConstructor
        _log?.WriteLine($"Creating in-process server: {ToString()}");
        Tunnel = new InProcTunnel(this);
    }

    private sealed class InProcTunnel(
        InProcessTestServer server,
        PipeOptions? pipeOptions = null) : Tunnel
    {
        public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(
            EndPoint endpoint,
            CancellationToken cancellationToken)
        {
            // server._log?.WriteLine($"Disabling client creation, requested endpoint: {Format.ToString(endpoint)}");
            return default;
        }

        public override ValueTask<Stream?> BeforeAuthenticateAsync(
            EndPoint endpoint,
            ConnectionType connectionType,
            Socket? socket,
            CancellationToken cancellationToken)
        {
            server._log?.WriteLine($"Client intercepted, requested endpoint: {Format.ToString(endpoint)} for {connectionType} usage");
            var clientToServer = new Pipe(pipeOptions ?? PipeOptions.Default);
            var serverToClient = new Pipe(pipeOptions ?? PipeOptions.Default);
            var serverSide = new Duplex(clientToServer.Reader, serverToClient.Writer);
            _ = Task.Run(async () => await server.RunClientAsync(serverSide), cancellationToken);
            var clientSide = StreamConnection.GetDuplex(serverToClient.Reader, clientToServer.Writer);
            return new(clientSide);
        }

        private sealed class Duplex(PipeReader input, PipeWriter output) : IDuplexPipe
        {
            public PipeReader Input => input;
            public PipeWriter Output => output;

            public ValueTask Dispose()
            {
                input.Complete();
                output.Complete();
                return default;
            }
        }
    }
    /*

    private readonly RespServer _server;
    public RespSocketServer(RespServer server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        server.Shutdown.ContinueWith((_, o) => ((SocketServer)o).Dispose(), this);
    }
    protected override void OnStarted(EndPoint endPoint)
        => _server.Log("Server is listening on " + endPoint);

    protected override Task OnClientConnectedAsync(in ClientConnection client)
        => _server.RunClientAsync(client.Transport);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _server.Dispose();
    }
    */
}
