using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class ConnectionShutdownTests : TestBase
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;
    public ConnectionShutdownTests(ITestOutputHelper output) : base(output) { }

    [Fact(Skip = "Unfriendly")]
    public async Task ShutdownRaisesConnectionFailedAndRestore()
    {
        using var conn = Create(allowAdmin: true, shared: false);

        int failed = 0, restored = 0;
        Stopwatch watch = Stopwatch.StartNew();
        conn.ConnectionFailed += (sender, args) =>
        {
            Log(watch.Elapsed + ": failed: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType + ": " + args);
            Interlocked.Increment(ref failed);
        };
        conn.ConnectionRestored += (sender, args) =>
        {
            Log(watch.Elapsed + ": restored: " + EndPointCollection.ToString(args.EndPoint) + "/" + args.ConnectionType + ": " + args);
            Interlocked.Increment(ref restored);
        };
        var db = conn.GetDatabase();
        db.Ping();
        Assert.Equal(0, Interlocked.CompareExchange(ref failed, 0, 0));
        Assert.Equal(0, Interlocked.CompareExchange(ref restored, 0, 0));
        await Task.Delay(1).ForAwait(); // To make compiler happy in Release

        conn.AllowConnect = false;
        var server = conn.GetServer(TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort);

        SetExpectedAmbientFailureCount(2);
        server.SimulateConnectionFailure(SimulatedFailureType.All);

        db.Ping(CommandFlags.FireAndForget);
        await Task.Delay(250).ForAwait();
        Assert.Equal(2, Interlocked.CompareExchange(ref failed, 0, 0));
        Assert.Equal(0, Interlocked.CompareExchange(ref restored, 0, 0));
        conn.AllowConnect = true;
        db.Ping(CommandFlags.FireAndForget);
        await Task.Delay(1500).ForAwait();
        Assert.Equal(2, Interlocked.CompareExchange(ref failed, 0, 0));
        Assert.Equal(2, Interlocked.CompareExchange(ref restored, 0, 0));
        watch.Stop();
    }
}
