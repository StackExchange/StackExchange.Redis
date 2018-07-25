using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

static class Program
{
    static async Task Main()
    {
        const int ClientCount = 50;
        CancellationTokenSource cancel = new CancellationTokenSource();

        var config = new ConfigurationOptions
        {
            EndPoints = { new IPEndPoint(IPAddress.Loopback, 6379) }
        };

        using (var muxer = await ConnectionMultiplexer.ConnectAsync(config))
        {
            var tasks = new Task[ClientCount + 1];
            tasks[0] = Task.Run(() => ShowState(cancel.Token));
            for (int i = 1; i < tasks.Length; i++)
            {
                int seed = i;
                var key = "test_client_" + i;
                tasks[i] = Task.Run(() => RunClient(key, seed, muxer, cancel.Token));
            }

            Console.ReadLine();
            cancel.Cancel();
            await Task.WhenAll(tasks);
        }
    }

    static int success, failure, clients;
    static long bytesChecked;
    static async Task ShowState(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(2000);
            Console.WriteLine($"[{Thread.VolatileRead(ref clients)}] Success: {Thread.VolatileRead(ref success)}, Failure: {Thread.VolatileRead(ref failure)}, Bytes: {Format(Thread.VolatileRead(ref bytesChecked))}");
        }
    }
    static string Format(long value)
    {
        if (value < 1024) return value + " B";
        if (value < (1024 * 1024)) return (value >> 10) + " KiB";
        if (value < (1024 * 1024 * 1024)) return (value >> 20) + " MiB";
        return (value >> 30) + " GiB";
    }
    static async Task RunClient(RedisKey key, int seed, ConnectionMultiplexer client, CancellationToken cancellation)
    {
        Interlocked.Increment(ref clients);
        try
        {
            var rand = new Random(seed);
            byte[] payload = new byte[65536];
            while (!cancellation.IsCancellationRequested)
            {
                var db = client.GetDatabase(rand.Next(0, 10));


                rand.NextBytes(payload);
                int len = rand.Next(1024, payload.Length);
                var memory = new ReadOnlyMemory<byte>(payload, 0, len);
                var set = db.StringSetAsync(key, memory);
                var get = db.StringGetAsync(key);

                await set;
                ReadOnlyMemory<byte> result = await get;

                Interlocked.Add(ref bytesChecked, len);
                if (memory.Span.SequenceEqual(result.Span))
                {
                    Interlocked.Increment(ref success);
                }
                else
                {
                    Interlocked.Increment(ref failure);
                    Console.Error.WriteLine("Expectation failed on " + key.ToString());
                }
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
