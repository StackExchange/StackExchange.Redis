using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using StackExchange.Redis.Configuration;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class InProcessTestServer : MemoryCacheRedisServer
{
    private readonly ITestOutputHelper? _log;
    public InProcessTestServer(ITestOutputHelper? log = null)
    {
        RedisVersion = RedisFeatures.v6_0_0; // for client to expect RESP3
        _log = log;
        // ReSharper disable once VirtualMemberCallInConstructor
        _log?.WriteLine($"Creating in-process server: {ToString()}");
        Tunnel = new InProcTunnel(this);
    }

    public Task<ConnectionMultiplexer> ConnectAsync(bool withPubSub = false, bool useSyncInputOutput = false, TextWriter? log = null)
        => ConnectionMultiplexer.ConnectAsync(GetClientConfig(withPubSub, useSyncInputOutput), log);

    // view request/response highlights in the log
    public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
    {
        var result = base.Execute(client, in request);
        Log($"[{client.Id}] {request.Command} => {(char)result.Type} ({result.Type})");
        return result;
    }

    public ConfigurationOptions GetClientConfig(bool withPubSub = false, bool useSyncInputOutput = false)
    {
        var commands = GetCommands();
        if (!withPubSub)
        {
            commands.Remove(nameof(RedisCommand.SUBSCRIBE));
            commands.Remove(nameof(RedisCommand.PSUBSCRIBE));
            commands.Remove(nameof(RedisCommand.SSUBSCRIBE));
            commands.Remove(nameof(RedisCommand.UNSUBSCRIBE));
            commands.Remove(nameof(RedisCommand.PUNSUBSCRIBE));
            commands.Remove(nameof(RedisCommand.SUNSUBSCRIBE));
            commands.Remove(nameof(RedisCommand.PUBLISH));
            commands.Remove(nameof(RedisCommand.SPUBLISH));
        }

        var config = new ConfigurationOptions
        {
            CommandMap = CommandMap.Create(commands),
            ConfigurationChannel = "",
            TieBreaker = "",
            DefaultVersion = RedisVersion,
            ConnectTimeout = 10000,
            SyncTimeout = 5000,
            AsyncTimeout = 5000,
            AllowAdmin = true,
            Tunnel = Tunnel,
            UseSyncInputOutput = useSyncInputOutput,
        };

        /* useful for viewing *outbound* data in the log
#if DEBUG
        if (_log is not null)
        {
            config.OutputLog = msg =>
            {
                lock (_log)
                {
                    _log.WriteLine(msg);
                }
            };
        }
#endif
        */

        foreach (var endpoint in GetEndPoints())
        {
            config.EndPoints.Add(endpoint);
        }
        return config;
    }

    public Tunnel Tunnel { get; }

    public override void Log(string message)
    {
        _log?.WriteLine(message);
        base.Log(message);
    }

    protected override void OnMoved(RedisClient client, int hashSlot, Node node)
    {
        _log?.WriteLine($"Client {client.Id} being redirected: {hashSlot} to {node}");
        base.OnMoved(client, hashSlot, node);
    }

    public override TypedRedisValue OnUnknownCommand(in RedisClient client, in RedisRequest request, ReadOnlySpan<byte> command)
    {
        _log?.WriteLine($"[{client.Id}] unknown command: {Encoding.ASCII.GetString(command)}");
        return base.OnUnknownCommand(in client, in request, command);
    }

    private sealed class InProcTunnel(
        InProcessTestServer server,
        PipeOptions? pipeOptions = null) : Tunnel
    {
        public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(
            EndPoint endpoint,
            CancellationToken cancellationToken)
        {
            if (server.TryGetNode(endpoint, out _))
            {
                // server._log?.WriteLine($"Disabling client creation, requested endpoint: {Format.ToString(endpoint)}");
                return default;
            }
            return base.GetSocketConnectEndpointAsync(endpoint, cancellationToken);
        }

        public override ValueTask<Stream?> BeforeAuthenticateAsync(
            EndPoint endpoint,
            ConnectionType connectionType,
            Socket? socket,
            CancellationToken cancellationToken)
        {
            if (server.TryGetNode(endpoint, out var node))
            {
                server._log?.WriteLine(
                    $"Client intercepted, endpoint {Format.ToString(endpoint)} ({connectionType}) mapped to {server.ServerType} node {node}");
                var clientToServer = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverToClient = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverSide = new Duplex(clientToServer.Reader, serverToClient.Writer);
                Task.Run(async () => await server.RunClientAsync(serverSide, node: node), cancellationToken).RedisFireAndForget();

                var readStream = serverToClient.Reader.AsStream();
                var writeStream = clientToServer.Writer.AsStream();
                var clientSide = new DuplexStream(readStream, writeStream);
                return new(clientSide);
            }
            return base.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
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
