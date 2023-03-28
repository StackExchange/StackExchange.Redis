using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)] // because I need to measure some things that could get confused
public class GarbageCollectionTests : TestBase
{
    public GarbageCollectionTests(ITestOutputHelper helper) : base(helper) { }

    private static void ForceGC()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
        }
    }

    [Fact]
    public async Task MuxerIsCollected()
    {
#if DEBUG
        Skip.Inconclusive("Only predictable in release builds");
#endif
        // this is more nuanced than it looks; multiple sockets with
        // async callbacks, plus a heartbeat on a timer

        // deliberately not "using" - we *want* to leak this
        var conn = Create();
        conn.GetDatabase().Ping(); // smoke-test

        ForceGC();

//#if DEBUG // this counter only exists in debug
//            int before = ConnectionMultiplexer.CollectedWithoutDispose;
//#endif
        var wr = new WeakReference(conn);
        conn = null;

        ForceGC();
        await Task.Delay(2000).ForAwait(); // GC is twitchy
        ForceGC();

        // should be collectable
        Assert.Null(wr.Target);

//#if DEBUG // this counter only exists in debug
//            int after = ConnectionMultiplexer.CollectedWithoutDispose;
//            Assert.Equal(before + 1, after);
//#endif
    }

    [Fact]
    public async Task MuxerIsNotCollectedWhenHasBacklog()
    {
        // Run on a separate thread to ensure no references to the connection from the main thread
        var startGC = new TaskCompletionSource<bool>();
        var testTask = Task.Run(async () =>
        {
            using var conn = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions()
            {
                BacklogPolicy = BacklogPolicy.Default,
                ConnectTimeout = 50,
                SyncTimeout = 1000,
                AllowAdmin = true,
                EndPoints = { GetConfiguration() },
            }, Writer);
            var db = conn.GetDatabase();
            await db.PingAsync();

            // Disconnect and don't allow re-connection
            conn.AllowConnect = false;
            var server = conn.GetServerSnapshot()[0];
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(conn.IsConnected);

            var pingTask = Assert.ThrowsAsync<RedisConnectionException>(() => db.PingAsync());
            startGC.SetResult(true);
            await pingTask;
        }).WithTimeout(5000);

        // Use sync wait and sleep to ensure a more timely GC
        Task.WaitAny(startGC.Task, testTask);
        while (!testTask.IsCompleted)
        {
            ForceGC();
            Thread.Sleep(200);
        }
        await testTask;
    }
}
