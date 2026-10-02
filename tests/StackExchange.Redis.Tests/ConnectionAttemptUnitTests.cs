using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
#if !NETFRAMEWORK
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#endif
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectionAttemptUnitTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SuccessfulAttemptsAreReported()
    {
        using var server = new InProcessTestServer(output);
        var options = server.GetClientConfig();
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);

        await WaitForAsync(() => attempts.Count(a => a.IsSuccess) >= 2);
        var interactive = Assert.Single(attempts, a => a.ConnectionType == ConnectionType.Interactive);
        var subscription = Assert.Single(attempts, a => a.ConnectionType == ConnectionType.Subscription);
        foreach (var attempt in new[] { interactive, subscription })
        {
            Assert.True(attempt.IsSuccess);
            Assert.Equal(ConnectionAttemptStage.Established, attempt.Stage);
            Assert.Equal(ConnectionFailureType.None, attempt.FailureType);
            Assert.Null(attempt.Exception);
            Assert.Null(attempt.ClientCertificateSubject);
            Assert.Null(attempt.ClientCertificateThumbprint);
            Assert.Null(attempt.TlsHostName); // no TLS
            Assert.Null(attempt.ServerCertificatePolicyErrors);
            Assert.Null(attempt.ServerCertificateAccepted);
        }
    }

    [Fact]
    public async Task EveryFailedReconnectIsReported()
    {
        // a port with nothing listening: every attempt is refused, so the server never becomes reachable,
        // which is exactly when ConnectionFailed stops reporting
        var endpoint = new IPEndPoint(IPAddress.Loopback, GetUnusedPort());
        var options = new ConfigurationOptions
        {
            EndPoints = { endpoint },
            AbortOnConnectFail = false,
            ConnectTimeout = 1000,
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            ReconnectRetryPolicy = new LinearRetry(50),
        };
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);

        await WaitForAsync(() => attempts.Count(a => a.ConnectionType == ConnectionType.Interactive) >= 4);
        var interactive = attempts.Where(a => a.ConnectionType == ConnectionType.Interactive).ToArray();
        output.WriteLine($"interactive attempts: {interactive.Length}");
        Assert.All(interactive, a =>
        {
            Assert.False(a.IsSuccess);
            Assert.Equal(ConnectionAttemptStage.Connect, a.Stage);
            Assert.NotEqual(ConnectionFailureType.None, a.FailureType);
            Assert.Equal(endpoint, a.EndPoint);
        });
    }

#if !NETFRAMEWORK // the in-process server only does TLS on .NET
    [Fact]
    public async Task AcceptedClientCertificateIsIdentified()
    {
        using var good = CreateClientCertificate("good-client");
        using var server = new InProcessTestServer(output, useSsl: true)
        {
            ClientCertificateValidator = cert => cert is not null && cert.GetCertHashString() == good.Thumbprint,
        };
        var options = server.GetClientConfig(withPubSub: false);
        options.CertificateSelection += (_, _, _, _, _) => good;
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.True(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = Assert.Single(attempts);
        Assert.True(attempt.IsSuccess);
        Assert.Equal(good.Subject, attempt.ClientCertificateSubject);
        Assert.Equal(good.Issuer, attempt.ClientCertificateIssuer);
        Assert.Equal(good.Thumbprint, attempt.ClientCertificateThumbprint);
        Assert.Equal(Sha256(good), attempt.ClientCertificateThumbprintSha256);
        Assert.Equal(options.ResolveTlsHostName(attempt.EndPoint!), attempt.TlsHostName);
        Assert.False(string.IsNullOrEmpty(attempt.TlsHostName));
        Assert.Equal(ConnectionAttemptStage.Established, attempt.Stage);

        // the test server's certificate is self-signed, and accepted by the test's own validation callback regardless
        output.WriteLine($"server certificate: {attempt.ServerCertificatePolicyErrors} / {attempt.ServerCertificateChainStatus}, accepted: {attempt.ServerCertificateAccepted}");
        Assert.True(attempt.ServerCertificateAccepted);
        Assert.NotNull(attempt.ServerCertificatePolicyErrors);
        Assert.NotNull(attempt.ServerCertificateChainStatus);
    }

    [Fact]
    public async Task RejectedClientCertificateIsIdentified()
    {
        using var good = CreateClientCertificate("good-client");
        using var bad = CreateClientCertificate("bad-client");
        using var server = new InProcessTestServer(output, useSsl: true)
        {
            ClientCertificateValidator = cert => cert is not null && cert.GetCertHashString() == good.Thumbprint,
        };
        var options = server.GetClientConfig(withPubSub: false);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.CertificateSelection += (_, _, _, _, _) => bad;
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.False(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = attempts.First();
        output.WriteLine($"rejected attempt: {attempt.Stage}, {attempt.FailureType}: {attempt.Exception?.GetType().Name}: {attempt.Exception?.Message}");
        Assert.False(attempt.IsSuccess);
        Assert.True(attempt.Stage is ConnectionAttemptStage.Tls or ConnectionAttemptStage.Handshake); // TLS 1.3: Handshake, TLS 1.2: Tls
        Assert.True(attempt.ServerCertificateAccepted); // the failure is the client certificate, not the server's
        Assert.NotEqual(ConnectionFailureType.None, attempt.FailureType);
        Assert.Equal(bad.Subject, attempt.ClientCertificateSubject);
        Assert.Equal(bad.Thumbprint, attempt.ClientCertificateThumbprint);
        Assert.Equal(Sha256(bad), attempt.ClientCertificateThumbprintSha256);
    }

    [Fact]
    public async Task ClientCertificateNotRequestedByServer()
    {
        // the selection callback still runs when the server does not ask for a certificate, but nothing is sent,
        // so nothing should be reported
        using var cert = CreateClientCertificate("unrequested-client");
        using var server = new InProcessTestServer(output, useSsl: true);
        var options = server.GetClientConfig(withPubSub: false);
        int selections = 0;
        options.CertificateSelection += (_, _, _, _, _) =>
        {
            System.Threading.Interlocked.Increment(ref selections);
            return cert;
        };
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.True(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = Assert.Single(attempts);
        output.WriteLine($"selection callback invoked {selections} time(s); reported thumbprint: {attempt.ClientCertificateThumbprint ?? "(null)"}");
        Assert.True(attempt.IsSuccess);
        Assert.Null(attempt.ClientCertificateThumbprint);
    }

    [Fact]
    public async Task ServerCertificateRejectedByPlatformDefault()
    {
        // no validation callback configured: the library observes validation through its own callback, which must keep the
        // platform-default semantics (reject a certificate with any errors - here, a self-signed one)
        using var server = new InProcessTestServer(output, useSsl: true);
        var options = server.GetClientConfig(withPubSub: false, validateServerCertificate: false);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.False(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = attempts.First();
        output.WriteLine($"server certificate: {attempt.ServerCertificatePolicyErrors} / {attempt.ServerCertificateChainStatus}; {attempt.FailureType}: {attempt.Exception?.Message}");
        Assert.False(attempt.IsSuccess);
        Assert.Equal(ConnectionAttemptStage.Tls, attempt.Stage);
        Assert.Equal(ConnectionFailureType.AuthenticationFailure, attempt.FailureType);
        Assert.False(attempt.ServerCertificateAccepted);
        Assert.True(attempt.ServerCertificatePolicyErrors!.Value.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.NotEqual(X509ChainStatusFlags.NoError, attempt.ServerCertificateChainStatus);

        // observing must not cost the detail that the platform's own message would have given
        Assert.Contains(nameof(SslPolicyErrors.RemoteCertificateChainErrors), attempt.Exception?.Message);
    }

    [Fact]
    public async Task ServerCertificateRejectedByCallback()
    {
        using var server = new InProcessTestServer(output, useSsl: true);
        var options = server.GetClientConfig(withPubSub: false, validateServerCertificate: false);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.CertificateValidation += (_, _, _, _) => false;
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.False(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = attempts.First();
        Assert.False(attempt.IsSuccess);
        Assert.Equal(ConnectionAttemptStage.Tls, attempt.Stage);
        Assert.False(attempt.ServerCertificateAccepted);
        Assert.NotNull(attempt.ServerCertificatePolicyErrors);
    }

    [Fact]
    public async Task OptionsValidationCallbackIsNotDisplaced()
    {
        // SslStream throws if a constructor callback differs from one in SslClientAuthenticationOptions; observing must
        // not introduce a constructor callback when the options supply their own
        using var server = new InProcessTestServer(output, useSsl: true);
        var options = server.GetClientConfig(withPubSub: false, validateServerCertificate: false);
        int validations = 0;
        options.SslClientAuthenticationOptions = host => new SslClientAuthenticationOptions
        {
            TargetHost = host,
            RemoteCertificateValidationCallback = (_, _, _, _) =>
            {
                System.Threading.Interlocked.Increment(ref validations);
                return true;
            },
        };
        var attempts = Observe(options);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.True(conn.IsConnected);

        await WaitForAsync(() => !attempts.IsEmpty);
        var attempt = Assert.Single(attempts);
        Assert.True(attempt.IsSuccess);
        Assert.True(validations > 0);
        Assert.Null(attempt.ServerCertificateAccepted); // the options own validation, so the library did not observe it
        Assert.Equal(options.ResolveTlsHostName(attempt.EndPoint!), attempt.TlsHostName);
    }

    private static X509Certificate2 CreateClientCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
#pragma warning disable SYSLIB0057
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057
    }

    private static string Sha256(X509Certificate2 certificate)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(certificate.RawData));
    }
#endif

    private static ConcurrentQueue<ConnectionAttemptEventArgs> Observe(ConfigurationOptions options)
    {
        var attempts = new ConcurrentQueue<ConnectionAttemptEventArgs>();
        options.ConnectionAttemptCompleted += (_, e) => attempts.Enqueue(e);
        return attempts;
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 10000)
    {
        var giveUp = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > giveUp) Assert.Fail("Condition not met before timeout");
            await Task.Delay(20);
        }
        await Task.Delay(100); // let any stragglers (e.g. a duplicate report, if there were one) arrive before asserting
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); // not IDisposable on net481
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
