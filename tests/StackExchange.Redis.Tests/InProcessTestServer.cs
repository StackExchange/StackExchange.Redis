extern alias respite;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
#if !NETFRAMEWORK
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#endif
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
#if !NETFRAMEWORK
    private readonly X509Certificate2? _serverCertificate;
    private readonly string? _serverCertificateThumbprint;
    private readonly RemoteCertificateValidationCallback? _certificateValidationCallback;
#endif

    public InProcessTestServer(ITestOutputHelper? log = null, EndPoint? endpoint = null, bool useSsl = false)
        : base(endpoint)
    {
        RedisVersion = RedisFeatures.v6_0_0; // for client to expect RESP3
        _log = log;
#if NETFRAMEWORK
        UseSsl = false;
#else
        UseSsl = useSsl;
        if (useSsl)
        {
            _serverCertificate = CreateServerCertificate(DefaultEndPoint);
            _serverCertificateThumbprint = _serverCertificate.Thumbprint;
            _certificateValidationCallback = ValidateServerCertificate;
        }
#endif
        // ReSharper disable once VirtualMemberCallInConstructor
        _log?.WriteLine($"Creating in-process server: {ToString()}");
        Tunnel = new InProcTunnel(this);
    }

    public Task<ConnectionMultiplexer> ConnectAsync(bool withPubSub = true, bool defaultOnly = false /*, WriteMode writeMode = WriteMode.Default */, TextWriter? log = null)
        => ConnectionMultiplexer.ConnectAsync(GetClientConfig(withPubSub, defaultOnly /*, writeMode */), log);

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

    public ConfigurationOptions GetClientConfig(bool withPubSub = true, bool defaultOnly = false /*, WriteMode writeMode = WriteMode.Default */)
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
        config.Ssl = UseSsl; // explicitly, ignore provider defaults
        if (UseSsl)
        {
#if !NETFRAMEWORK
            config.CertificateValidation += _certificateValidationCallback;
#endif
        }

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

        if (defaultOnly)
        {
            config.EndPoints.Add(DefaultEndPoint);
        }
        else
        {
            foreach (var endpoint in GetEndPoints())
            {
                config.EndPoints.Add(endpoint);
            }
        }
        return config;
    }

    public Tunnel Tunnel { get; }
    public bool UseSsl { get; }

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

    protected override void Dispose(bool disposing)
    {
#if !NETFRAMEWORK
        if (disposing)
        {
            _serverCertificate?.Dispose();
        }
#endif
        base.Dispose(disposing);
    }

#if !NETFRAMEWORK
    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        return certificate is not null
            && _serverCertificateThumbprint is not null
            && string.Equals(certificate.GetCertHashString(), _serverCertificateThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2 CreateServerCertificate(EndPoint endpoint)
    {
        var now = DateTimeOffset.UtcNow;
        var subjectName = GetCertificateSubjectName(endpoint);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));

        var san = new SubjectAlternativeNameBuilder();
        switch (endpoint)
        {
            case DnsEndPoint dns:
                san.AddDnsName(dns.Host);
                break;
            case IPEndPoint ip:
                san.AddIpAddress(ip.Address);
                break;
        }
        request.CertificateExtensions.Add(san.Build());

        using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(7));
#pragma warning disable SYSLIB0057
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057

        static string GetCertificateSubjectName(EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => dns.Host,
            IPEndPoint ip => ip.Address.ToString(),
            _ => "localhost",
        };
    }
#endif

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

        public override async ValueTask<Stream?> BeforeAuthenticateAsync(
            EndPoint endpoint,
            ConnectionType connectionType,
            Socket? socket,
            CancellationToken cancellationToken)
        {
            if (server.TryGetNode(endpoint, out var node))
            {
                await server.OnAcceptClientAsync(endpoint);
                server._log?.WriteLine(
                    $"[{endpoint}] accepting {connectionType} mapped to {server.ServerType} node {node} via {(server.UseSsl ? "TLS" : "plaintext")}");
                var clientToServer = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverToClient = new Pipe(pipeOptions ?? PipeOptions.Default);
                var serverInput = clientToServer.Reader.AsStream();
                var serverOutput = serverToClient.Writer.AsStream();
                var serverTransport = new DuplexStream(serverInput, serverOutput);

                if (server.UseSsl)
                {
#if !NETFRAMEWORK
                    Task.Run(
                        async () =>
                        {
                            using var ssl = new SslStream(serverTransport, leaveInnerStreamOpen: false);
                            await ssl.AuthenticateAsServerAsync(
                                server._serverCertificate!,
                                clientCertificateRequired: false,
                                enabledSslProtocols: SslProtocols.None,
                                checkCertificateRevocation: false).ConfigureAwait(false);
                            var serverSide = new StreamDuplexPipe(ssl);
                            await server.RunClientAsync(serverSide, node: node, state: null).ConfigureAwait(false);
                        },
                        cancellationToken).RedisFireAndForget();
#endif
                }
                else
                {
                    var serverSide = new Duplex(clientToServer.Reader, serverToClient.Writer);
                    TaskCompletionSource<RedisClient> clientTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task.Run(async () => await server.RunClientAsync(serverSide, node: node, state: clientTcs), cancellationToken).RedisFireAndForget();
                    if (!clientTcs.Task.Wait(1000)) throw new TimeoutException("Client not connected");
                    _ = clientTcs.Task.Result;
                }

                var readStream = serverToClient.Reader.AsStream();
                var writeStream = clientToServer.Writer.AsStream();
                var clientSide = new DuplexStream(readStream, writeStream);
                return clientSide;
            }
            return await base.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
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

        private sealed class StreamDuplexPipe(Stream stream) : IDuplexPipe
        {
            public PipeReader Input { get; } = PipeReader.Create(stream);
            public PipeWriter Output { get; } = PipeWriter.Create(stream);
        }
    }

    protected virtual ValueTask OnAcceptClientAsync(EndPoint endpoint) => default;

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
