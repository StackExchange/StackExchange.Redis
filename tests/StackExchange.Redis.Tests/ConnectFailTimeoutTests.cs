using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class ConnectFailTimeoutTests : TestBase
{
    public ConnectFailTimeoutTests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public async Task NoticesConnectFail()
    {
        SetExpectedAmbientFailureCount(-1);
        using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);

        var server = conn.GetServer(conn.GetEndPoints()[0]);

        await RunBlockingSynchronousWithExtraThreadAsync(innerScenario).ForAwait();
        void innerScenario()
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
        var time = server.Ping();
        Log("pinged");
        Log(time.ToString());
    }
}
