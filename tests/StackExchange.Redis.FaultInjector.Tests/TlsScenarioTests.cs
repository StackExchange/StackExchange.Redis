using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    [Fact]
    public async Task NotificationsArriveOverTlsAndIdentityIsVerified()
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
            extra: new Dictionary<string, string?>
            {
                ["include_tls"] = "true",
                ["variant_index"] = "1",
            },
            cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);
        log.WriteLine($"provisioned {database} (tls={database.Tls})");

        if (!database.Tls)
        {
            Assert.Skip("the injector provisioned a plaintext database despite include_tls=true; nothing to test here");
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
