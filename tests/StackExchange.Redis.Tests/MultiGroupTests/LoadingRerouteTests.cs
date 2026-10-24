using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

[RunPerProtocol]
public class LoadingRerouteTests(ITestOutputHelper log) : TestBase(log)
{
    // A member answering LOADING must steer the group away promptly, via the same ConnectionFailed fast-path
    // as a circuit-breaker trip. The breaker cannot be what moves us here: once the server is loading, every
    // reconnect fails during the handshake, so later commands die in the backlog without a reply, and the
    // breaker only ever observes replies. As with the breaker test, the poll interval is enormous so that the
    // fast-path is the only thing that can reroute within the test window, and the probe holds A down so the
    // reroute is deterministic.
    [Fact]
    public async Task LoadingMember_ReroutesAwayPromptly()
    {
        EndPoint alpha = new DnsEndPoint("alpha", 6379);
        EndPoint bravo = new DnsEndPoint("bravo", 6379);

        using var serverA = new InProcessTestServer(Output, endpoint: alpha);
        using var serverB = new InProcessTestServer(Output, endpoint: bravo);

        var probe = new ControllableProbe();

        ConnectionGroupMember[] members =
        [
            new(serverA.GetClientConfig(), "A") { Weight = 9 }, // highest weight -> initially active
            new(serverB.GetClientConfig(), "B") { Weight = 1 }, // failover target
        ];

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.FromMinutes(30), // enormous: the fast-path is what reroutes us
            HealthCheck = new HealthCheck.Builder
            {
                Probe = probe,
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);

        var loadingEvents = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        typed.ConnectionFailed += (_, e) =>
        {
            Log($"ConnectionFailed: {e.FailureType} @ {e.EndPoint}");
            if (e.FailureType == ConnectionFailureType.Loading)
            {
                loadingEvents.TrySetResult(true);
            }
        };

        await GroupWait.AssertConnectedAsync(conn);
        Assert.Same(members[0], conn.ActiveMember);

        // knock A into LOADING and hold it down; the next command to A is what surfaces the failure
        probe.MarkDown(alpha);
        serverA.IsLoading = true;
        var fault = await Assert.ThrowsAnyAsync<Exception>(() => conn.GetDatabase().PingAsync());
        Log($"observed fault: {fault.GetType().Name}: {fault.Message}");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && ReferenceEquals(conn.ActiveMember, members[0]))
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Same(members[1], conn.ActiveMember);

        var completed = await Task.WhenAny(loadingEvents.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(ReferenceEquals(completed, loadingEvents.Task), "expected a ConnectionFailed event with FailureType == Loading");
    }
}
