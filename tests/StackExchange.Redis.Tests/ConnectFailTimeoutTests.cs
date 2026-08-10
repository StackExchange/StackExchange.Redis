using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectFailTimeoutTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    [Trait(TestCategories.Category, TestCategories.SimulatedConnectionFailure)]
    public async Task NoticesConnectFail()
    {
        SetExpectedAmbientFailureCount(-1);
        // syncTimeout is explicit because this test depends on it: it simulates a failure, expects the
        // next synchronous call to give up, and then allows a fixed window for the heartbeat to
        // reconnect. A longer sync timeout (see TestConfig.MinTimeoutMilliseconds, raised on slow CI)
        // changes what that call does and breaks the scenario.
        await using var conn = Create(allowAdmin: true, backlogPolicy: BacklogPolicy.FailFast, allowSimulateConnectionFailure: true, syncTimeout: 5000);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        Assert.SkipUnless(server.CanSimulateConnectionFailure(), "Skipping because server cannot simulate connection failure");

        await RunBlockingSynchronousWithExtraThreadAsync(InnerScenario).ForAwait();

        void InnerScenario()
        {
            conn.ConnectionFailed += (s, a) =>
                Log("Disconnected: " + EndPointCollection.ToString(a.EndPoint));
            conn.ConnectionRestored += (s, a) =>
                Log("Reconnected: " + EndPointCollection.ToString(a.EndPoint));

            // No need to delay, we're going to try a disconnected connection immediately so it'll fail...
            conn.IgnoreConnect = true;
            Log("simulating failure");
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Log("simulated failure");
            conn.IgnoreConnect = false;
            Log("pinging - expect failure");
            Assert.Throws<RedisConnectionException>(() => server.Ping());
            Log("pinged");
        }

        // Heartbeat should reconnect by now
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => server.IsConnected);

        Log("pinging - expect success");
        var time = await server.PingAsync();
        Log("pinged");
        Log(time.ToString());
    }
}
