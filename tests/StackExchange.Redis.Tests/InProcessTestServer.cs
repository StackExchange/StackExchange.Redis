extern alias respite;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using respite::RESPite.Messages;
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

    public Task<ConnectionMultiplexer> ConnectAsync(bool withPubSub = true /*, WriteMode writeMode = WriteMode.Default */, TextWriter? log = null)
        => ConnectionMultiplexer.ConnectAsync(GetClientConfig(withPubSub /*, writeMode */), log);

    // view request/response highlights in the log
    public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
    {
        var result = base.Execute(client, in request);
        var type = client.ApplyProtocol(result.Type);
        if (result.IsNil)
        {
            Log($"[{client}] {request.Command} (no reply)");
        }
        else if (result.IsAggregate)
        {
            Log($"[{client}] {request.Command} => {(char)type}{result.Span.Length}");
        }
        else
        {
            try
            {
                var s = result.AsRedisValue().ToString() ?? "(null)";
                const int MAX_CHARS = 32;
                s = s.Length <= MAX_CHARS ? s : s.Substring(0, MAX_CHARS) + "...";
                Log($"[{client}] {request.Command} => {(char)type}{s}");
            }
            catch
            {
                Log($"[{client}] {request.Command} => {(char)type}");
            }
        }
        return result;
    }

    public ConfigurationOptions GetClientConfig(bool withPubSub = true /*, WriteMode writeMode = WriteMode.Default */)
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
            Protocol = TestContext.Current.GetProtocol(),
            // WriteMode = (BufferedStreamWriter.WriteMode)writeMode,
        };
        if (!string.IsNullOrEmpty(Password)) config.Password = Password;

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
        _log?.WriteLine($"[{client}] being redirected: slot {hashSlot} to {node}");
        base.OnMoved(client, hashSlot, node);
    }

    protected override void OnOutOfBand(RedisClient client, TypedRedisValue message)
    {
        var type = client.ApplyProtocol(message.Type);
        if (message.IsAggregate
            && message.Span is { IsEmpty: false } span
            && !span[0].IsAggregate)
        {
            _log?.WriteLine($"[{client}] => {(char)type}{message.Span.Length} {span[0].AsRedisValue()}");
        }
        else
        {
            _log?.WriteLine($"[{client}] => {(char)type}");
        }

        base.OnOutOfBand(client, message);
    }

    /*
    public override void OnFlush(RedisClient client, int messages, long bytes)
    {
        if (bytes >= 0)
        {
            _log?.WriteLine($"[{client}] flushed {messages} messages, {bytes} bytes");
        }
        else
        {
            _log?.WriteLine($"[{client}] flushed {messages} messages"); // bytes not available
        }
        base.OnFlush(client, messages, bytes);
    }
    */

    public override TypedRedisValue OnUnknownCommand(in RedisClient client, in RedisRequest request, ReadOnlySpan<byte> command)
    {
        _log?.WriteLine($"[{client}] unknown command: {Encoding.ASCII.GetString(command)}");
        return base.OnUnknownCommand(in client, in request, command);
    }

    public override void OnClientConnected(RedisClient client, object state)
    {
        if (state is TaskCompletionSource<RedisClient> pending)
        {
            pending.TrySetResult(client);
        }
        base.OnClientConnected(client, state);
    }

    public override void OnClientCompleted(RedisClient client, Exception? fault)
    {
        if (fault is null)
        {
            _log?.WriteLine($"[{client}] completed");
        }
        else
        {
            _log?.WriteLine($"[{client}] faulted: {fault.Message} ({fault.GetType().Name})");
        }
        base.OnClientCompleted(client, fault);
    }

    protected override void OnSkippedReply(RedisClient client)
    {
        _log?.WriteLine($"[{client}] skipped reply");
        base.OnSkippedReply(client);
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
                server.OnAcceptClient(endpoint);
                var clientToServer = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverToClient = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverSide = new Duplex(clientToServer.Reader, serverToClient.Writer);

                TaskCompletionSource<RedisClient> clientTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task.Run(async () => await server.RunClientAsync(serverSide, node: node, state: clientTcs), cancellationToken).RedisFireAndForget();
                if (!clientTcs.Task.Wait(1000)) throw new TimeoutException("Client not connected");
                var client = clientTcs.Task.Result;
                server._log?.WriteLine(
                    $"[{client}] connected ({connectionType} mapped to {server.ServerType} node {node})");

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

    protected virtual void OnAcceptClient(EndPoint endpoint)
    {
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
