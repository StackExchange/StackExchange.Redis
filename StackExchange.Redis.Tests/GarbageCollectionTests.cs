using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)] // because I need to measure some things that could get confused
    public class GarbageCollectionTests : TestBase
    {
        public GarbageCollectionTests(ITestOutputHelper helper) : base(helper) { }

        private static void ForceGC()
        {
            for(int i = 0; i < 3; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }
        }

#if DEBUG
        [Fact]
        public void MuxerIsCollected()
        {
            // first check WeakReference works like we expect
            var obj = new object();
            var wr = new WeakReference(obj);
            obj = null;
            ForceGC();
            Assert.Null(wr.Target);

            var muxer = Create(); // deliberately not "using"
            muxer.GetDatabase().Ping();

            ForceGC();
            int before = ConnectionMultiplexer.CollectedWithoutDispose;

            wr = new WeakReference(muxer);
            muxer = null;
            ForceGC();

            int after = ConnectionMultiplexer.CollectedWithoutDispose;


            Thread.Sleep(TimeSpan.FromSeconds(60));
            Assert.Null(wr.Target);
            Assert.Equal(before + 1, after);
        }
#endif
    }
}
