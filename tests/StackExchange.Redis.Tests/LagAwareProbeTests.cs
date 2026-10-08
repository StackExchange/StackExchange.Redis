using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Tests.MultiGroupTests;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The lag-aware probe against a stubbed REST transport. Response shapes are the ones captured from real
/// Redis Enterprise 8.0.22 deployments (see notes/lag-aware/findings.md, section 10), minus credentials.
/// </summary>
public class LagAwareProbeTests(ITestOutputHelper log)
{
    private const string Host = "redis-14460.c1.example.com";
    private const int Port = 14460;

    private const string BdbList = """
        [{"endpoints":[{"addr":["54.243.16.166"],"addr_type":"external","dns_name":"redis-14460.c1.example.com","oss_cluster_api_preferred_endpoint_type":"ip","oss_cluster_api_preferred_ip_type":"internal","port":14460,"proxy_policy":"single","uid":"1:1"}],"uid":1},
         {"endpoints":[{"addr":["100.53.190.50"],"addr_type":"external","dns_name":"redis-12706.c1.example.com","oss_cluster_api_preferred_endpoint_type":"ip","oss_cluster_api_preferred_ip_type":"external","port":12706,"proxy_policy":"all-master-shards","uid":"2:1"}],"uid":2}]
        """;

    private const string ActiveActive = """{"crdt":true,"uid":1}""";
    private const string NotActiveActive = """{"crdt":false,"uid":1}""";
    private const string Unavailable = """{"description":"BDB 1 is not available","error_code":"bdb_unavailable"}""";

    // answers by path-and-query; records every request
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<string, (HttpStatusCode Status, string? Body)> answer)
            : this((request, _) =>
            {
                var (status, body) = answer(request.RequestUri!.PathAndQuery);
                var response = new HttpResponseMessage(status);
                if (body is not null) response.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return Task.FromResult(response);
            })
        {
        }

        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        public IEnumerable<string> Paths => Requests.Select(r => r.RequestUri!.PathAndQuery);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return respond(request, cancellationToken);
        }
    }

    // the cluster as measured: discovery, crdt lookup, then the availability answer
    private static Func<string, (HttpStatusCode, string?)> Cluster(bool activeActive, HttpStatusCode availability, string? availabilityBody = null)
        => path => path switch
        {
            "/v1/bdbs?fields=uid,endpoints" => (HttpStatusCode.OK, BdbList),
            "/v1/bdbs/1?fields=uid,crdt" => (HttpStatusCode.OK, activeActive ? ActiveActive : NotActiveActive),
            _ when path.StartsWith("/v1/bdbs/1/availability", StringComparison.Ordinal) => (availability, availabilityBody),
            _ => (HttpStatusCode.NotFound, null),
        };

    private int _credentialCalls;

    private LagAwareOptions Options(StubHandler handler) => new()
    {
        Credentials = _ =>
        {
            Interlocked.Increment(ref _credentialCalls);
            return new(new NetworkCredential("probe@example.com", "s3cret"));
        },
        HttpMessageHandlerFactory = () => handler,
    };

    private async Task<(HealthCheckResult Result, StubHandler Handler)> CheckOnceAsync(
        StubHandler handler, LagAwareOptions? options = null, TimeSpan? timeout = null, HealthCheckProbe? probe = null)
    {
        using var server = new InProcessTestServer(log, endpoint: new DnsEndPoint(Host, Port));
        await using var conn = await server.ConnectAsync();
        probe ??= HealthCheckProbe.LagAware(options ?? Options(handler));
        var context = new HealthCheckContext(conn.GetServer(new DnsEndPoint(Host, Port)), timeout ?? TimeSpan.FromSeconds(3));
        return (await probe.CheckHealthAsync(context), handler);
    }

    [Fact]
    public void ProbeIsMemberScoped()
        => Assert.Equal(HealthCheckProbeScope.Member, HealthCheckProbe.LagAware(Options(new StubHandler(Cluster(true, HttpStatusCode.OK)))).Scope);

    [Fact]
    public async Task DiscoversTheDatabaseThenAsksTheLagQuestionOfAnActiveActiveMember()
    {
        var (result, handler) = await CheckOnceAsync(new StubHandler(Cluster(activeActive: true, HttpStatusCode.OK)));

        Assert.Equal(HealthCheckResult.Healthy, result);
        Assert.Equal(
            ["/v1/bdbs?fields=uid,endpoints", "/v1/bdbs/1?fields=uid,crdt", "/v1/bdbs/1/availability?extend_check=lag"],
            handler.Paths);

        // the cluster name, derived from the configured database host; Basic auth on every request
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("https://c1.example.com:9443", r.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Equal("Basic", r.Headers.Authorization?.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("probe@example.com:s3cret")), r.Headers.Authorization?.Parameter);
        });
    }

    [Fact]
    public async Task NeverIssuesAnUnfilteredDatabaseRead()
    {
        // measured: unfiltered /v1/bdbs and /v1/crdbs return database passwords, even to db_viewer
        var (_, handler) = await CheckOnceAsync(new StubHandler(Cluster(activeActive: true, HttpStatusCode.OK)));
        Assert.All(handler.Paths, path =>
        {
            Assert.DoesNotContain("/v1/crdbs", path);
            if (!path.Contains("/availability")) Assert.Contains("fields=", path);
        });
    }

    [Fact]
    public async Task ChecksPlainAvailabilityOfADatabaseThatIsNotActiveActive()
    {
        // measured: the lag check reports every non-Active-Active database unavailable, whatever the tolerance
        var (result, handler) = await CheckOnceAsync(new StubHandler(Cluster(activeActive: false, HttpStatusCode.OK)));
        Assert.Equal(HealthCheckResult.Healthy, result);
        Assert.Equal("/v1/bdbs/1/availability", handler.Paths.Last());
    }

    [Fact]
    public async Task DiscoveryIsCachedAcrossPassesAndCredentialsAreReadEachTime()
    {
        var handler = new StubHandler(Cluster(activeActive: true, HttpStatusCode.OK));
        var probe = HealthCheckProbe.LagAware(Options(handler));
        await CheckOnceAsync(handler, probe: probe);
        await CheckOnceAsync(handler, probe: probe);

        Assert.Equal(4, handler.Requests.Count); // three, then just the availability call
        Assert.Equal("/v1/bdbs/1/availability?extend_check=lag", handler.Paths.Last());
        Assert.Equal(2, _credentialCalls); // rotation: asked once per check
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, HealthCheckResult.Healthy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HealthCheckResult.Unhealthy)] // unavailable, or lagging
    [InlineData(HttpStatusCode.NotFound, HealthCheckResult.Unhealthy)]
    [InlineData(HttpStatusCode.Unauthorized, HealthCheckResult.Inconclusive)] // management plane, not the database
    [InlineData(HttpStatusCode.Forbidden, HealthCheckResult.Inconclusive)]
    [InlineData(HttpStatusCode.InternalServerError, HealthCheckResult.Inconclusive)]
    public async Task MapsTheAvailabilityAnswer(HttpStatusCode status, HealthCheckResult expected)
    {
        var (result, _) = await CheckOnceAsync(new StubHandler(Cluster(activeActive: true, status, status == HttpStatusCode.ServiceUnavailable ? Unavailable : null)));
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task NotFoundForgetsTheDiscoveredDatabase()
    {
        var gone = false;
        var handler = new StubHandler(path => gone && path.Contains("/availability") ? (HttpStatusCode.NotFound, null) : Cluster(true, HttpStatusCode.OK)(path));
        var probe = HealthCheckProbe.LagAware(Options(handler));
        Assert.Equal(HealthCheckResult.Healthy, (await CheckOnceAsync(handler, probe: probe)).Result);

        gone = true;
        Assert.Equal(HealthCheckResult.Unhealthy, (await CheckOnceAsync(handler, probe: probe)).Result);

        gone = false;
        await CheckOnceAsync(handler, probe: probe);
        Assert.Equal(2, handler.Paths.Count(p => p == "/v1/bdbs?fields=uid,endpoints")); // rediscovered
    }

    [Fact]
    public async Task TransportFailureIsInconclusive()
    {
        var (result, _) = await CheckOnceAsync(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));
        Assert.Equal(HealthCheckResult.Inconclusive, result);
    }

    [Fact]
    public async Task ASlowAnswerIsInconclusiveWithinTheProbeTimeout()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (result, _) = await CheckOnceAsync(handler, timeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(HealthCheckResult.Inconclusive, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task ADatabaseThatCannotBeFoundIsInconclusive()
    {
        var (result, handler) = await CheckOnceAsync(new StubHandler(path => (HttpStatusCode.OK, path.Contains("fields=uid,endpoints") ? "[]" : null)));
        Assert.Equal(HealthCheckResult.Inconclusive, result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExplicitOptionsSkipDiscoveryAndSendTheTolerance()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, null));
        var options = Options(handler);
        options.RestEndpoint = new Uri("https://cluster.example.com:9443/");
        options.DatabaseId = 7;
        options.LagCheck = LagCheckMode.Enabled;
        options.LagTolerance = TimeSpan.FromMilliseconds(250);

        var (result, _) = await CheckOnceAsync(handler, options);
        Assert.Equal(HealthCheckResult.Healthy, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://cluster.example.com:9443/v1/bdbs/7/availability?extend_check=lag&availability_lag_tolerance_ms=250", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task DisabledNeverAsksTheLagQuestion()
    {
        var handler = new StubHandler(Cluster(activeActive: true, HttpStatusCode.OK));
        var options = Options(handler);
        options.LagCheck = LagCheckMode.Disabled;
        var (_, _) = await CheckOnceAsync(handler, options);
        Assert.DoesNotContain(handler.Paths, p => p.Contains("extend_check") || p.Contains("crdt"));
    }

    [Theory]
    [InlineData("redis-14460.c1.example.com", 14460, "c1.example.com")] // the form Redis Enterprise assigns
    [InlineData("REDIS-14460.c1.example.com", 14460, "c1.example.com")]
    [InlineData("redis-14460.c1.example.com", 6379, "redis-14460.c1.example.com")] // port does not match
    [InlineData("cache.example.com", 14460, "cache.example.com")] // a name of the operator's own
    [InlineData("redis-14460.", 14460, "redis-14460.")]
    public void DerivesTheClusterNameFromTheDatabaseHost(string host, int port, string expected)
        => Assert.Equal(expected, HealthCheckProbe.LagAwareProbe.ClusterHost(host, port));

    [Fact]
    public async Task ARedirectIsReportedNotFollowed()
    {
        // measured: non-master nodes redirect management routes to the master's internal address
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://10.0.101.184:9443" + request.RequestUri!.PathAndQuery);
            return Task.FromResult(response);
        });
        var (result, _) = await CheckOnceAsync(handler);
        Assert.Equal(HealthCheckResult.Inconclusive, result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void FindsTheDatabaseByAddressAsWellAsByName()
        => Assert.Equal(2, HealthCheckProbe.LagAwareProbe.FindUid(BdbList, "100.53.190.50", 12706));

    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentNullException>(() => HealthCheckProbe.LagAware(null!));
        Assert.Throws<ArgumentException>(() => HealthCheckProbe.LagAware(new LagAwareOptions()));
        Assert.Throws<ArgumentOutOfRangeException>(() => HealthCheckProbe.LagAware(new LagAwareOptions
        {
            Credentials = _ => new(new NetworkCredential()),
            LagTolerance = TimeSpan.FromMilliseconds(-1),
        }));
    }

    [Fact]
    public async Task AsAFailbackCheckItKeepsTrafficOffALaggingMember()
    {
        // end to end at the stub level: beta has the higher weight, but its cluster reports it lagging
        var alphaEp = new DnsEndPoint("redis-14460.c1.example.com", 14460);
        var betaEp = new DnsEndPoint("redis-14460.c2.example.com", 14460);
        using var alpha = new InProcessTestServer(log, endpoint: alphaEp);
        using var beta = new InProcessTestServer(log, endpoint: betaEp);

        var handler = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var isC2 = request.RequestUri.Host == "c2.example.com"; // the cluster names, derived from the members' hosts
            (HttpStatusCode Status, string? Body) answer = path switch
            {
                "/v1/bdbs?fields=uid,endpoints" => (HttpStatusCode.OK, isC2 ? BdbList.Replace(".c1.", ".c2.") : BdbList),
                "/v1/bdbs/1?fields=uid,crdt" => (HttpStatusCode.OK, ActiveActive),
                _ => isC2 ? (HttpStatusCode.ServiceUnavailable, Unavailable) : (HttpStatusCode.OK, null),
            };
            var response = new HttpResponseMessage(answer.Status);
            if (answer.Body is not null) response.Content = new StringContent(answer.Body);
            return Task.FromResult(response);
        });

        // liveness held down until both members are connected, so the first real selection sees the whole group
        var gate = new GateProbe();
        MultiGroupOptions groupOptions = new MultiGroupOptions.Builder
        {
            HealthCheck = new HealthCheck.Builder { Probe = gate, ProbeCount = 1 },
            FailbackHealthCheck = HealthCheck.LagAware(Options(handler)),
            HealthCheckInterval = TimeSpan.MaxValue,
        };
        ConnectionGroupMember[] members = [new(alpha.GetClientConfig(), "alpha") { Weight = 1 }, new(beta.GetClientConfig(), "beta") { Weight = 9 }];
        await using var group = await ConnectionMultiplexer.ConnectGroupAsync(members, groupOptions);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!Array.TrueForAll(members, m => m.Multiplexer.IsConnected) && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        gate.Open = true;
        await MultiGroupMultiplexer.TryHealthCheckAndSelectPreferredGroupAsync(group);
        await GroupWait.AssertConnectedAsync(group);

        Assert.Equal("alpha", group.ActiveMember?.Name);
        Assert.False(members[1].FailbackVerified);
        Assert.Contains(handler.Requests, r => r.RequestUri!.Host == "c2.example.com" && r.RequestUri.AbsolutePath == "/v1/bdbs/1/availability");
    }

    private sealed class GateProbe : HealthCheckProbe
    {
        public volatile bool Open;

        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context) => Open ? HealthyTask : UnhealthyTask;
    }
}
