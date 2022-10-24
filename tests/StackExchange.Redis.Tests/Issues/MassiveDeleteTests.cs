using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class MassiveDeleteTests : TestBase
{
    public MassiveDeleteTests(ITestOutputHelper output) : base(output) { }

    private void Prep(int dbId, string key)
    {
        using var conn = Create(allowAdmin: true);

        var prefix = Me();
        Skip.IfMissingDatabase(conn, dbId);
        GetServer(conn).FlushDatabase(dbId);
        Task? last = null;
        var db = conn.GetDatabase(dbId);
        for (int i = 0; i < 10000; i++)
        {
            string iKey = prefix + i;
            db.StringSetAsync(iKey, iKey);
            last = db.SetAddAsync(key, iKey);
        }
        db.Wait(last!);
    }

    [FactLongRunning]
    public async Task ExecuteMassiveDelete()
    {
        var dbId = TestConfig.GetDedicatedDB();
        var key = Me();
        Prep(dbId, key);
        var watch = Stopwatch.StartNew();
        using var conn = Create();
        using var throttle = new SemaphoreSlim(1);
        var db = conn.GetDatabase(dbId);
        var originally = await db.SetLengthAsync(key).ForAwait();
        int keepChecking = 1;
        Task? last = null;
        while (Volatile.Read(ref keepChecking) == 1)
        {
            throttle.Wait(); // acquire
            var x = db.SetPopAsync(key).ContinueWith(task =>
            {
                throttle.Release();
                if (task.IsCompleted)
                {
                    if ((string?)task.Result == null)
                    {
                        Volatile.Write(ref keepChecking, 0);
                    }
                    else
                    {
                        last = db.KeyDeleteAsync((string?)task.Result);
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
        long remaining = await db.SetLengthAsync(key).ForAwait();
        Log("From {0} to {1}; {2}ms", originally, remaining,
            watch.ElapsedMilliseconds);

        var counters = GetServer(conn).GetCounters();
        Log("Completions: {0} sync, {1} async", counters.Interactive.CompletedSynchronously, counters.Interactive.CompletedAsynchronously);
    }
}
