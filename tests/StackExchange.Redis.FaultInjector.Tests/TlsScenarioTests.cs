using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The same feature over TLS, against certificates a real cluster generated.
/// </summary>
/// <remarks>
/// A gap that has been open since the identity work: nothing had exercised a real TLS deployment, only the
/// in-process harness with a certificate it made itself. That matters because the interesting failures are
/// about *identity* - which name the certificate carries, and whether the endpoint we are told to use is
/// covered by it - and a fake that issues its own certificate cannot be wrong about that in the way a real
/// deployment can.
/// <para>
/// Certificates here are self-signed per environment, so the client pins the issuer with
/// <see cref="ConfigurationOptions.TrustIssuer(string)"/>. Note what is deliberately *not* done: validation is
/// never disabled. A TLS test that stops checking identity reports success for precisely the thing it exists to
/// catch, so a missing CA fails the run instead.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "tls")]
public class TlsScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData(1, "single_tls", false)]
    [InlineData(2, "mtls", true)]
    public async Task NotificationsArriveOverTlsAndIdentityIsVerified(int variantIndex, string expectedConfig, bool expectClientCertificate)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        if (fixture.Environment.CertificateAuthorityPath is null)
        {
            // a skip rather than a failure: whether the environment generated certificates is a provisioning
            // choice, and running this without them would prove nothing
            Assert.Skip($"no CA certificate in {fixture.Environment.ConfigDirectory.FullName}; provision with TLS to run this");
        }

        log.WriteLine($"trusting issuer {fixture.Environment.CertificateAuthorityPath}");
        CertificateSanity.RequireCertificatesMatchThisCluster(fixture.Environment, log.WriteLine);

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector,
            "topology-change-standalone",
            "conn_drop",
            "endpoint_rebind",
            log.WriteLine,
            // include_tls does not *request* TLS, it widens the list of variants the setup may choose from;
            // variant_index is what picks one. Confirmed by asking the discovery endpoint: with no flags a
            // trigger offers one variant ("single"), with include_tls two ("single", "single_tls"), and with
            // include_mtls a third ("mtls"). Passing include_tls alone provisions variant 0 and yields a
            // plaintext database, which is how this test first came to skip itself.
            // include_tls / include_mtls widen the variant list; variant_index picks one. With no flags a
            // trigger offers just "single"; include_tls adds "single_tls"; include_mtls adds "mtls", which is
            // the same TLS database plus enforce_client_authentication.
            extra: new Dictionary<string, string?>
            {
                ["include_tls"] = "true",
                ["include_mtls"] = expectClientCertificate ? "true" : null,
                ["variant_index"] = variantIndex.ToString(),
            },
            cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);
        log.WriteLine($"provisioned {database} (tls={database.Tls})");

        if (!database.Tls)
        {
            Assert.Skip("the injector provisioned a plaintext database despite include_tls=true; nothing to test here");
        }

        Assert.Equal(expectedConfig, database.ProxyPolicy); // the setup reports which variant it built
        if (expectClientCertificate)
        {
            // A database with enforce_client_authentication rejects a connection that offers no certificate, so
            // the material has to be there: this is the client identity, issued by the injector's own
            // intermediate CA, and a different trust root from the one validating the server.
            Assert.NotNull(database.Mtls);
            log.WriteLine($"presenting client certificate {database.Mtls.ClientCertificatePath}");
            Assert.True(File.Exists(database.Mtls.ClientCertificatePath), $"missing {database.Mtls.ClientCertificatePath}");
            Assert.True(File.Exists(database.Mtls.ClientKeyPath), $"missing {database.Mtls.ClientKeyPath}");
        }
        else
        {
            Assert.Null(database.Mtls);
        }

        var clock = Stopwatch.StartNew();
        var events = new List<PushMaintenanceEvent>();

        var config = database.GetClientConfig(fixture.Environment);

        // AbortOnConnectFail=true *for this test only*: everywhere else tolerating a slow start is right, but a
        // TLS failure has to surface its reason. With it false, a certificate problem is indistinguishable from
        // a slow cluster - ConnectAsync succeeds, IsConnected is false, and the cause is gone.
        config.AbortOnConnectFail = true;

        // If the certificate does not cover the name we dialled, this throws - which is the point: the
        // assertion that identity was verified is the connect succeeding with validation on. The log goes to
        // test output so a handshake failure says *which* check failed.
        var connectLog = new StringWriter();
        ConnectionMultiplexer conn;
        try
        {
            conn = await ConnectionMultiplexer.ConnectAsync(config, connectLog);
        }
        catch (Exception ex)
        {
            log.WriteLine(connectLog.ToString());
            log.WriteLine($"TLS connect failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        await using (conn)
        {
        Assert.True(conn.IsConnected);

        var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);
        Assert.True(endpoint.MaintenanceNotificationsActive, "the opt-in should be live over TLS too");
        log.WriteLine($"connected over TLS; opt-in active; ping {(await conn.GetDatabase().PingAsync()).TotalMilliseconds:0.0}ms");

        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is PushMaintenanceEvent push)
            {
                lock (events) events.Add(push);
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId} {push.RawMessage}");
            }
        };

        clock.Restart();
        await scenario.FireAsync(cancellationToken);

        var deadline = clock.Elapsed + TimeSpan.FromSeconds(45);
        while (clock.Elapsed < deadline)
        {
            try
            {
                await conn.GetDatabase().PingAsync();
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                log.WriteLine($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  ping failed: {ex.GetType().Name}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        lock (events)
        {
            log.WriteLine($"  {events.Count} notification(s) over TLS");
            Assert.NotEmpty(events);

            // The TLS half of the endpoint-type derivation, end to end: an encrypted connection asks for an
            // FQDN form, so a MOVING should name a *host* rather than an address - which is the whole point,
            // since a certificate carrying DNS names cannot validate a bare IP.
            foreach (var moving in events.Where(e => e.NotificationType == MaintenanceNotificationType.Moving))
            {
                log.WriteLine($"  MOVING named: {moving.NewEndPoint?.ToString() ?? "(null)"}");
                if (moving.NewEndPoint is not null)
                {
                    Assert.IsType<System.Net.DnsEndPoint>(moving.NewEndPoint);
                }
            }
        }

        // and the TLS handshake succeeds again on the *replacement* connection, which is the part a
        // certificate-name problem would break rather than the first connect
        Assert.True(
            await Poll.UntilAsync(
                () =>
                {
                    try
                    {
                        return conn.IsConnected && conn.GetDatabase().Ping() >= TimeSpan.Zero;
                    }
                    catch (Exception ex) when (ex is RedisException or TimeoutException)
                    {
                        return false;
                    }
                },
                timeoutMilliseconds: 30_000),
            "the client should re-establish TLS after the endpoint moves");
        }
    }
}
