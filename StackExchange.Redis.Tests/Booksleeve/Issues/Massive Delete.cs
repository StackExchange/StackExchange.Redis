using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve.Issues
{
    public class Massive_Delete : BookSleeveTestBase
    {
        public Massive_Delete(ITestOutputHelper output) : base(output)
        {
            using (var muxer = GetUnsecuredConnection(allowAdmin: true))
            {
                GetServer(muxer).FlushDatabase(db);
                Task last = null;
                var conn = muxer.GetDatabase(db);
                for (int i = 0; i < 100000; i++)
                {
                    string key = "key" + i;
                    conn.StringSetAsync(key, key);
                    last = conn.SetAddAsync(todoKey, key);
                }
                conn.Wait(last);
            }
        }

        private const int db = 4;
        private const string todoKey = "todo";

        [Fact]
        public async Task ExecuteMassiveDelete()
        {
            var watch = Stopwatch.StartNew();
            using (var muxer = GetUnsecuredConnection())
            using (var throttle = new SemaphoreSlim(1))
            {
                var conn = muxer.GetDatabase(db);
                var originally = await conn.SetLengthAsync(todoKey);
                int keepChecking = 1;
                Task last = null;
                while (Volatile.Read(ref keepChecking) == 1)
                {
                    throttle.Wait(); // acquire
                    var x = conn.SetPopAsync(todoKey).ContinueWith(task =>
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
                long remaining = await conn.SetLengthAsync(todoKey);
                Output.WriteLine("From {0} to {1}; {2}ms", originally, remaining,
                    watch.ElapsedMilliseconds);

                var counters = GetServer(muxer).GetCounters();
                Output.WriteLine("Completions: {0} sync, {1} async", counters.Interactive.CompletedSynchronously, counters.Interactive.CompletedAsynchronously);
            }
        }
    }
}
