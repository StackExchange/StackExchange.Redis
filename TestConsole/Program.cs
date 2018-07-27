using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

static class Program
{
    static void Main()
    {
        using (var muxer = ConnectionMultiplexer.Connect("localhost:6379", Console.Out))
        {
            muxer.GetDatabase().Ping();
        }
    }
    static async Task Main2()
    {
        const int ClientCount = 150, ConnectionCount = 10;
        CancellationTokenSource cancel = new CancellationTokenSource();

        var config = new ConfigurationOptions
        {
            EndPoints = { new IPEndPoint(IPAddress.Loopback, 6379) }
        };
        var muxers = new ConnectionMultiplexer[ConnectionCount];
        try
        {
            for(int i = 0; i < muxers.Length; i++)
            {
                muxers[i] = await ConnectionMultiplexer.ConnectAsync(config);
            }
            var tasks = new Task[ClientCount + 1];
            tasks[0] = Task.Run(() => ShowState(cancel.Token));

            for (int i = 1; i < tasks.Length; i++)
            {
                var db = muxers[i % muxers.Length].GetDatabase();
                int seed = i;
                var key = "test_client_" + i;
                tasks[i] = Task.Run(() => RunClient(key, seed, db, cancel.Token));
            }

            Console.ReadLine();
            cancel.Cancel();
            await Task.WhenAll(tasks);
        }
        finally
        {
            for (int i = 0; i < muxers.Length; i++)
            {
                try { muxers[i]?.Dispose(); } catch { }
            }
        }
    }

    static int clients;
    static long totalPings, pings, lastTicks;
    static async Task ShowState(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(2000);
            var nowTicks = DateTime.UtcNow.Ticks;
            var thenTicks = Interlocked.Exchange(ref lastTicks, nowTicks);
            long pingsInInterval = Interlocked.Exchange(ref pings, 0);
            var newTotalPings = Interlocked.Add(ref totalPings, pingsInInterval);

            var deltaTicks = nowTicks - thenTicks;

            Console.WriteLine($"[{Thread.VolatileRead(ref clients)}], Pings: {newTotalPings} ({pingsInInterval}, {Rate(pingsInInterval, deltaTicks)}/s)");
        }
    }

    private static string Rate(long pingsInInterval, long deltaTicks)
    {
        if (deltaTicks == 0) return "n/a";
        if (pingsInInterval == 0) return "0";

        var seconds = ((decimal)deltaTicks) / TimeSpan.TicksPerSecond;
        return (pingsInInterval / seconds).ToString("0.0");
    }

    static async Task RunClient(RedisKey key, int seed, IDatabase db, CancellationToken cancellation)
    {
        Interlocked.Increment(ref clients);
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await db.PingAsync();
                Interlocked.Increment(ref pings);
            }
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref clients);
        }
    }
}
