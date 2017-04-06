using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tests.Issues
{
    [TestFixture]
    public class Massive_Delete
    {
        [OneTimeSetUpAttribute]
        public void Init()
        {
            using (var muxer = Config.GetUnsecuredConnection(allowAdmin: true))
            {
                Config.GetServer(muxer).FlushDatabase(db);
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

        const int db = 4;
        const string todoKey = "todo";

        [Test]
        public void ExecuteMassiveDelete()
        {
            var watch = Stopwatch.StartNew();
            using (var muxer = Config.GetUnsecuredConnection())
            using (var throttle = new SemaphoreSlim(1))
            {
                var conn = muxer.GetDatabase(db);
                var originallyTask = conn.SetLengthAsync(todoKey);
                int keepChecking = 1;
                Task last = null;
                while (Volatile.Read(ref keepChecking) == 1)
                {
                    throttle.Wait(); // acquire
                    conn.SetPopAsync(todoKey).ContinueWith(task =>
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
                }
                if (last != null)
                {
                    conn.Wait(last);
                }
                watch.Stop();
                long originally = conn.Wait(originallyTask),
                    remaining = conn.SetLength(todoKey);
                Console.WriteLine("From {0} to {1}; {2}ms", originally, remaining,
                    watch.ElapsedMilliseconds);

                var counters = Config.GetServer(muxer).GetCounters();
                Console.WriteLine("Completions: {0} sync, {1} async", counters.Interactive.CompletedSynchronously, counters.Interactive.CompletedAsynchronously);
            }
        }
    }
}
