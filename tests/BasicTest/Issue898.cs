using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis;

namespace BasicTest;

[Config(typeof(SlowConfig))]
public class Issue898 : IDisposable
{
    private readonly ConnectionMultiplexer mux;
    private readonly IDatabase db;

    public void Dispose()
    {
        mux?.Dispose();
        GC.SuppressFinalize(this);
    }

    public Issue898()
    {
        mux = ConnectionMultiplexer.Connect("127.0.0.1:6379");
        db = mux.GetDatabase();
    }

    private const int Max = 100000;

    [Benchmark(OperationsPerInvoke = Max)]
    public void Load()
    {
        for (int i = 0; i < Max; ++i)
        {
            db.StringSet(i.ToString(), i);
        }
    }

    [Benchmark(OperationsPerInvoke = Max)]
    public async Task LoadAsync()
    {
        for (int i = 0; i < Max; ++i)
        {
            await db.StringSetAsync(i.ToString(), i).ConfigureAwait(false);
        }
    }

    [Benchmark(OperationsPerInvoke = Max)]
    public void Sample()
    {
        var rnd = new Random();

        for (int i = 0; i < Max; ++i)
        {
            var r = rnd.Next(0, Max - 1);

            var rv = db.StringGet(r.ToString());
            if (rv != r)
            {
                throw new Exception($"Unexpected {rv}, expected {r}");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = Max)]
    public async Task SampleAsync()
    {
        var rnd = new Random();

        for (int i = 0; i < Max; ++i)
        {
            var r = rnd.Next(0, Max - 1);

            var rv = await db.StringGetAsync(r.ToString()).ConfigureAwait(false);
            if (rv != r)
            {
                throw new Exception($"Unexpected {rv}, expected {r}");
            }
        }
    }
}
