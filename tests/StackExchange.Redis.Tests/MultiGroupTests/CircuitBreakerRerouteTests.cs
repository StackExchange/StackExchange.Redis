using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

[RunPerProtocol]
public class CircuitBreakerRerouteTests(ITestOutputHelper log) : TestBase(log)
{
    // A circuit-breaker trip must steer the group away from the affected member *promptly* - i.e. via
    // the shim's ConnectionFailed(CircuitBreaker) fast-path, not by waiting for the next health-check
    // poll. To isolate that mechanism we make the poll interval enormous, so the *only* thing that can
    // reroute inside the test window is the fast-path; and we hold the tripped member unhealthy via a
    // controllable probe, so the reroute is deterministic even though the physical connection reconnects
    // immediately after being torn down.
    [Fact]
    public async Task CircuitBreakerTrip_ReroutesAwayFromMember()
    {
        EndPoint alpha = new DnsEndPoint("alpha", 6379);
        EndPoint bravo = new DnsEndPoint("bravo", 6379);
        EndPoint charlie = new DnsEndPoint("charlie", 6379);

        using var serverA = new InProcessTestServer(Output, endpoint: alpha);
        using var serverB = new InProcessTestServer(Output, endpoint: bravo);
        using var serverC = new InProcessTestServer(Output, endpoint: charlie);

        var breaker = new FlipBreaker();
        var probe = new ControllableProbe();

        // only member A carries the trippable breaker; B and C are left with the default (which requires
        // an implausible number of failures to trip, so they never interfere)
        var configA = serverA.GetClientConfig();
        configA.CircuitBreaker = breaker;

        ConnectionGroupMember[] members =
        [
            new(configA, "A") { Weight = 9 },                     // highest weight -> initially active
            new(serverB.GetClientConfig(), "B") { Weight = 3 },   // preferred failover target
            new(serverC.GetClientConfig(), "C") { Weight = 1 },
        ];

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            // enormous, so the poll loop cannot be what reroutes us during the test
            HealthCheckInterval = TimeSpan.FromMinutes(30),
            HealthCheck = new HealthCheck.Builder
            {
                Probe = probe,
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);

        // completes the first time we see a ConnectionFailed(CircuitBreaker); we await this (rather than
        // polling a counter) so the assertion is deterministic and not subject to event-timing races
        var circuitBreakerEvents = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        typed.ConnectionFailed += (_, e) =>
        {
            Log($"ConnectionFailed: {e.FailureType} @ {e.EndPoint}");
            if (e.FailureType == ConnectionFailureType.CircuitBreaker)
            {
                circuitBreakerEvents.TrySetResult(true);
            }
        };

        // sanity: A (highest weight) is the active member to begin with
        await GroupWait.AssertConnectedAsync(conn);
        Assert.Same(members[0], conn.ActiveMember);

        // arm the breaker and hold A unhealthy, then drive a *faulting* command to the active member (A):
        // the fault is what the breaker evaluates, and a tripped breaker tears the connection down
        probe.MarkDown(alpha);
        breaker.Trip();
        var db = conn.GetDatabase();
        var fault = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync("nonesuch"));
        Log($"observed fault: {fault.GetType().Name}: {fault.Message}");

        // wait (briefly) for the fast-path to react; nothing else can move us within this window
        var moved = await WaitForActiveAsync(conn, notMember: members[0], timeout: TimeSpan.FromSeconds(5));

        Assert.True(moved, "expected the circuit-breaker trip to reroute away from member A");
        Assert.Same(members[1], conn.ActiveMember); // B is the next-highest weight

        // a ConnectionFailed event with FailureType == CircuitBreaker must have fired; await it (with a
        // timeout linked to the ambient test cancellation) rather than racing on a counter read
        using var cbCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cbCts.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.True(await circuitBreakerEvents.Task.WaitAsync(cbCts.Token), "expected a ConnectionFailed event with FailureType == CircuitBreaker");
    }

    private static async Task<bool> WaitForActiveAsync(
        IConnectionGroup conn, ConnectionGroupMember notMember, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!ReferenceEquals(conn.ActiveMember, notMember) && conn.ActiveMember is not null)
            {
                return true;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return false;
    }

    // trips on demand: healthy until Trip() is called; the trip only actuates on a *fault* observation
    // (successes are never evaluated, by design), which the test provides via a bad command
    private sealed class FlipBreaker : CircuitBreaker
    {
        private volatile bool _tripped;
        public void Trip() => _tripped = true;

        public override Accumulator CreateAccumulator() => new Acc(this);

        private sealed class Acc(FlipBreaker owner) : Accumulator
        {
            // this breaker trips on demand regardless of *what* faulted, so treat every observed fault as a
            // failure - otherwise Trip's IsFailure gate would filter out non-failure faults (e.g. an unknown
            // command) before the tripped state is ever consulted
            protected override bool IsFailure(in FaultContext fault) => true;
            public override void ObserveResult(in FaultContext context) { }
            public override bool IsHealthy() => !owner._tripped;
            public override void Reset() { }
        }
    }
}
