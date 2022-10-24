using System;
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
}
