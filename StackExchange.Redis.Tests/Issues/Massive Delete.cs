using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Massive_Delete : TestBase
    {
        public Massive_Delete(ITestOutputHelper output) : base(output) { }

        private void Prep(int db, string key)
        {
            var prefix = Me();
            using (var muxer = Create(allowAdmin: true))
            {
                Skip.IfMissingDatabase(muxer, db);
                GetServer(muxer).FlushDatabase(db);
                Task last = null;
                var conn = muxer.GetDatabase(db);
                for (int i = 0; i < 10000; i++)
                {
                    string iKey = prefix + i;
                    conn.StringSetAsync(iKey, iKey);
                    last = conn.SetAddAsync(key, iKey);
                }
                conn.Wait(last);
            }
        }

        [FactLongRunning]
        public async Task ExecuteMassiveDelete()
        {
            var dbId = TestConfig.GetDedicatedDB();
            var key = Me();
            Prep(dbId, key);
            var watch = Stopwatch.StartNew();
            using (var muxer = Create())
            using (var throttle = new SemaphoreSlim(1))
            {
                var conn = muxer.GetDatabase(dbId);
                var originally = await conn.SetLengthAsync(key).ForAwait();
                int keepChecking = 1;
                Task last = null;
                while (Volatile.Read(ref keepChecking) == 1)
                {
                    throttle.Wait(); // acquire
                    var x = conn.SetPopAsync(key).ContinueWith(task =>
                    {
                        throttle.Release();
                        if (task.IsCompleted)
                        {
                            if ((string)task.Result == null)
                            {
                                Volatile.Write(ref keepChecking, 0);
                            }
                            else
                            {
                                last = conn.KeyDeleteAsync((string)task.Result);
                            }
                        }
                    });
                    GC.KeepAlive(x);
                }
                if (last != null)
                {
                    await last;
                }
                watch.Stop();
                long remaining = await conn.SetLengthAsync(key).ForAwait();
                Log("From {0} to {1}; {2}ms", originally, remaining,
                    watch.ElapsedMilliseconds);

                var counters = GetServer(muxer).GetCounters();
                Log("Completions: {0} sync, {1} async", counters.Interactive.CompletedSynchronously, counters.Interactive.CompletedAsynchronously);
            }
        }
    }
}
