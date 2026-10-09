using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests.MultiGroupTests;

/// <summary>
/// The failback health check: consulted only before traffic moves to a member, never evicting the active one,
/// with selection falling back to liveness when no live member is eligible.
/// </summary>
public class FailbackHealthCheckTests(ITestOutputHelper log)
{
    private static readonly DnsEndPoint Alpha = new("alpha", 6379), Beta = new("beta", 6379), Gamma = new("gamma", 6379);

    // answers per endpoint as scripted by the test (default Healthy), counting calls
    private sealed class ScriptedProbe(HealthCheckProbeScope scope = HealthCheckProbeScope.Endpoint) : HealthCheckProbe
    {
        private readonly ConcurrentDictionary<EndPoint, HealthCheckResult> _answers = new();

        public ConcurrentDictionary<EndPoint, int> Calls { get; } = new();

        public override HealthCheckProbeScope Scope => scope;

        public void Set(EndPoint endpoint, HealthCheckResult result) => _answers[endpoint] = result;

        public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
        {
            var endpoint = context.Server.EndPoint;
            Calls.AddOrUpdate(endpoint, 1, (_, n) => n + 1);
            return Task.FromResult(_answers.TryGetValue(endpoint, out var result) ? result : HealthCheckResult.Healthy);
        }
    }

    private static HealthCheck Single(HealthCheckProbe probe) => new HealthCheck.Builder { Probe = probe, ProbeCount = 1 };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly InProcessTestServer[] _servers;
        private readonly DnsEndPoint[] _endpoints;

        public Fixture(ITestOutputHelper log, params (DnsEndPoint Endpoint, double Weight)[] members)
        {
            _servers = new InProcessTestServer[members.Length];
            _endpoints = Array.ConvertAll(members, m => m.Endpoint);
            Members = new ConnectionGroupMember[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                _servers[i] = new InProcessTestServer(log, endpoint: members[i].Endpoint);
                Members[i] = new(_servers[i].GetClientConfig(), members[i].Endpoint.Host) { Weight = members[i].Weight };
            }
        }

        public ConnectionGroupMember[] Members { get; }

        public ScriptedProbe Liveness { get; } = new();

        public ScriptedProbe Failback { get; } = new();

        public IConnectionGroup Group { get; private set; } = null!;

        public async Task ConnectAsync()
        {
            MultiGroupOptions options = new MultiGroupOptions.Builder
            {
                HealthCheck = Single(Liveness),
                FailbackHealthCheck = Single(Failback),
                HealthCheckInterval = TimeSpan.MaxValue, // passes are driven by the test
            };
            // Hold every member down until all of their connections are up, so the first real selection
            // sees the whole group: otherwise whichever member connects first can be selected by the liveness
            // fallback (provisionally) before the others are up - a race, not the rule under test.
            foreach (var endpoint in _endpoints) Liveness.Set(endpoint, HealthCheckResult.Unhealthy);
            Group = await ConnectionMultiplexer.ConnectGroupAsync(Members, options);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!Array.TrueForAll(Members, m => m.Multiplexer.IsConnected) && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
            Assert.True(Array.TrueForAll(Members, m => m.Multiplexer.IsConnected), "members did not connect");

            foreach (var endpoint in _endpoints) Liveness.Set(endpoint, HealthCheckResult.Healthy);
            await PassAsync();
            await GroupWait.AssertConnectedAsync(Group);
        }

        public void MarkDown(EndPoint endpoint) => Liveness.Set(endpoint, HealthCheckResult.Unhealthy);

        public Task PassAsync() => MultiGroupMultiplexer.TryHealthCheckAndSelectPreferredGroupAsync(Group);

        public string? Active => Group.ActiveMember?.Name;

        public async ValueTask DisposeAsync()
        {
            if (Group is not null) await Group.DisposeAsync();
            foreach (var server in _servers) server.Dispose();
        }
    }

    [Fact]
    public async Task IneligibleMemberIsNotSelectedUntilItPasses()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        f.Failback.Set(Beta, HealthCheckResult.Unhealthy); // beta: higher weight, but behind
        await f.ConnectAsync();
        Assert.Equal("alpha", f.Active);

        f.Failback.Set(Beta, HealthCheckResult.Healthy); // caught up
        await f.PassAsync();
        Assert.Equal("beta", f.Active);
        Assert.False(f.Members[1].IsUnhealthy); // eligibility is not health
    }

    [Fact]
    public async Task InconclusiveIsNotEligible()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        f.Failback.Set(Beta, HealthCheckResult.Inconclusive); // "could not ask"
        await f.ConnectAsync();
        Assert.Equal("alpha", f.Active);
    }

    [Fact]
    public async Task ActiveMemberIsNeverEvictedByItsFailbackCheck()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active);

        f.Failback.Set(Beta, HealthCheckResult.Unhealthy);
        await f.PassAsync();
        Assert.Equal("beta", f.Active);

        // checked once, on the first real pass when nothing was active yet; never while active
        Assert.True(f.Failback.Calls.TryGetValue(Beta, out var calls));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NoEligibleMemberFallsBackToLiveness()
    {
        // every member lagging - the normal state of a partition between regions - must still serve
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        f.Failback.Set(Alpha, HealthCheckResult.Unhealthy);
        f.Failback.Set(Beta, HealthCheckResult.Unhealthy);
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active);
    }

    [Fact]
    public async Task AFallbackChoiceIsLeftForAnEligibleMember()
    {
        // other clients never select a member that fails its lag check; the nearest equivalent that still
        // serves when every member is behind is to leave a fallback choice as soon as anything is eligible
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        f.Failback.Set(Alpha, HealthCheckResult.Unhealthy);
        f.Failback.Set(Beta, HealthCheckResult.Unhealthy);
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active); // provisional

        f.Failback.Set(Alpha, HealthCheckResult.Healthy);
        await f.PassAsync();
        Assert.Equal("alpha", f.Active); // eligible beats provisional, whatever the weights

        f.Failback.Set(Beta, HealthCheckResult.Healthy);
        await f.PassAsync();
        Assert.Equal("beta", f.Active); // and ordinary failback by weight resumes
    }

    [Fact]
    public async Task AProvisionalMemberThatCatchesUpBecomesOrdinary()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        f.Failback.Set(Alpha, HealthCheckResult.Unhealthy);
        f.Failback.Set(Beta, HealthCheckResult.Unhealthy);
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active); // provisional

        f.Failback.Set(Beta, HealthCheckResult.Healthy); // still checked while provisional
        await f.PassAsync();
        Assert.Equal("beta", f.Active); // verified: no longer provisional

        // now an ordinary active member: never evicted by its own failback check
        f.Failback.Set(Alpha, HealthCheckResult.Healthy);
        f.Failback.Set(Beta, HealthCheckResult.Unhealthy);
        await f.PassAsync();
        Assert.Equal("beta", f.Active);
    }

    [Fact]
    public async Task DeadActiveIsReplacedEvenWhenNoSurvivorIsEligible()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active);

        f.MarkDown(Beta);                          // the active region is gone...
        f.Failback.Set(Alpha, HealthCheckResult.Unhealthy); // ...and the survivor reports lag because of it
        await f.PassAsync();
        Assert.Equal("alpha", f.Active);
    }

    [Fact]
    public async Task DeadActivePrefersAnEligibleSurvivor()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Gamma, 5), (Beta, 9));
        await f.ConnectAsync();
        Assert.Equal("beta", f.Active);

        f.MarkDown(Beta);
        f.Failback.Set(Gamma, HealthCheckResult.Unhealthy); // higher weight, but behind
        await f.PassAsync();
        Assert.Equal("alpha", f.Active);
    }

    [Fact]
    public async Task ExplicitFailoverOverridesIneligibility()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        await f.ConnectAsync();
        f.Failback.Set(Alpha, HealthCheckResult.Unhealthy);
        await f.PassAsync();
        Assert.Equal("beta", f.Active);

        Assert.True(f.Group.TryFailoverTo(f.Members[0]));
        Assert.Equal("alpha", f.Active);
    }

    [Fact]
    public async Task FormerActiveMustBeCheckedAfreshBeforeReturning()
    {
        await using var f = new Fixture(log, (Alpha, 1), (Beta, 9));
        await f.ConnectAsync();
        await f.PassAsync(); // beta active: its verdict is not kept while active
        Assert.Equal("beta", f.Active);

        Assert.True(f.Group.TryFailoverTo(f.Members[0]));
        Assert.Equal("alpha", f.Active);

        // removing the override reselects at once, but beta has no current verdict, so it is not eligible yet
        Assert.True(f.Group.TryFailoverTo(null));
        Assert.Equal("alpha", f.Active);

        await f.PassAsync(); // now checked, and healthy
        Assert.Equal("beta", f.Active);
    }

    [Fact]
    public async Task MemberScopedProbeRunsOncePerMember()
    {
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var other = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"scope-key", other);
        await using var conn = await server.ConnectAsync();
        Assert.Equal(2, conn.GetServers().Length);

        // an endpoint that is not yet connected is reported unhealthy without being probed, so wait for both
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!Array.TrueForAll(conn.GetServers(), s => s.IsConnected) && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(Array.TrueForAll(conn.GetServers(), s => s.IsConnected));

        var perEndpoint = new ScriptedProbe(HealthCheckProbeScope.Endpoint);
        var perMember = new ScriptedProbe(HealthCheckProbeScope.Member);
        Assert.Equal(HealthCheckResult.Healthy, await Single(perEndpoint).CheckHealthAsync(conn));
        Assert.Equal(HealthCheckResult.Healthy, await Single(perMember).CheckHealthAsync(conn));

        Assert.Equal(2, perEndpoint.Calls.Count);
        Assert.Single(perMember.Calls);
        Assert.All(perMember.Calls.Values, n => Assert.Equal(1, n));
    }

    [Fact]
    public void OptionsCarryTheFailbackHealthCheck()
    {
        Assert.Null(MultiGroupOptions.Default.FailbackHealthCheck);
        Assert.Same(MultiGroupOptions.Default, new MultiGroupOptions.Builder().Create());

        var check = Single(new ScriptedProbe());
        MultiGroupOptions options = new MultiGroupOptions.Builder { FailbackHealthCheck = check };
        Assert.Same(check, options.FailbackHealthCheck);
        Assert.Same(check, new MultiGroupOptions.Builder(options).Create().FailbackHealthCheck);
    }
}
