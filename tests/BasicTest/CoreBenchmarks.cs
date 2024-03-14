using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis;

namespace BasicTest;

/// <summary>
/// The tests
/// </summary>
[Config(typeof(CustomConfig))]
public class CoreBenchmarks : IDisposable
{
    private SocketManager mgr;
    private ConnectionMultiplexer connection;
    private IDatabase db;

    /// <summary>
    /// Create
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();

        var options = ConfigurationOptions.Parse("127.0.0.1:6379");
        connection = ConnectionMultiplexer.Connect(options);
        db = connection.GetDatabase(3);

        db.KeyDelete(GeoKey);
        db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
        db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");

        db.KeyDelete(HashKey);
        for (int i = 0; i < 1000; i++)
        {
            db.HashSet(HashKey, i, i);
        }
    }

    private static readonly RedisKey GeoKey = "GeoTest", IncrByKey = "counter", StringKey = "string", HashKey = "hash";
    void IDisposable.Dispose()
    {
        mgr?.Dispose();
        connection?.Dispose();
        mgr = null;
        db = null;
        connection = null;
        GC.SuppressFinalize(this);
    }

    private const int COUNT = 50;

    /// <summary>
    /// Run INCRBY lots of times
    /// </summary>
    // [Benchmark(Description = "INCRBY/s", OperationsPerInvoke = COUNT)]

    public int ExecuteIncrBy()
    {
        var rand = new Random(12345);

        db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
        int expected = 0;
        for (int i = 0; i < COUNT; i++)
        {
            int x = rand.Next(50);
            expected += x;
            db.StringIncrement(IncrByKey, x, CommandFlags.FireAndForget);
        }
        int actual = (int)db.StringGet(IncrByKey);
        if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
        return actual;
    }

    /// <summary>
    /// Run INCRBY lots of times
    /// </summary>
    // [Benchmark(Description = "INCRBY/a", OperationsPerInvoke = COUNT)]
    public async Task<int> ExecuteIncrByAsync()
    {
        var rand = new Random(12345);

        db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
        int expected = 0;
        for (int i = 0; i < COUNT; i++)
        {
            int x = rand.Next(50);
            expected += x;
            await db.StringIncrementAsync(IncrByKey, x, CommandFlags.FireAndForget).ConfigureAwait(false);
        }
        int actual = (int)await db.StringGetAsync(IncrByKey).ConfigureAwait(false);
        if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
        return actual;
    }

    /// <summary>
    /// Run GEORADIUS lots of times
    /// </summary>
    // [Benchmark(Description = "GEORADIUS/s", OperationsPerInvoke = COUNT)]
    public int ExecuteGeoRadius()
    {
        int total = 0;
        for (int i = 0; i < COUNT; i++)
        {
            var results = db.GeoRadius(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash);
            total += results.Length;
        }
        return total;
    }

    /// <summary>
    /// Run GEORADIUS lots of times
    /// </summary>
    // [Benchmark(Description = "GEORADIUS/a", OperationsPerInvoke = COUNT)]
    public async Task<int> ExecuteGeoRadiusAsync()
    {
        int total = 0;
        for (int i = 0; i < COUNT; i++)
        {
            var results = await db.GeoRadiusAsync(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash).ConfigureAwait(false);
            total += results.Length;
        }
        return total;
    }

    /// <summary>
    /// Run StringSet lots of times
    /// </summary>
    [Benchmark(Description = "StringSet/s", OperationsPerInvoke = COUNT)]
    public void StringSet()
    {
        for (int i = 0; i < COUNT; i++)
        {
            db.StringSet(StringKey, "hey");
        }
    }

    /// <summary>
    /// Run StringGet lots of times
    /// </summary>
    [Benchmark(Description = "StringGet/s", OperationsPerInvoke = COUNT)]
    public void StringGet()
    {
        for (int i = 0; i < COUNT; i++)
        {
            db.StringGet(StringKey);
        }
    }

    /// <summary>
    /// Run HashGetAll lots of times
    /// </summary>
    [Benchmark(Description = "HashGetAll F+F/s", OperationsPerInvoke = COUNT)]
    public void HashGetAll_FAF()
    {
        for (int i = 0; i < COUNT; i++)
        {
            db.HashGetAll(HashKey, CommandFlags.FireAndForget);
            db.Ping(); // to wait for response
        }
    }

    /// <summary>
    /// Run HashGetAll lots of times
    /// </summary>
    [Benchmark(Description = "HashGetAll F+F/a", OperationsPerInvoke = COUNT)]

    public async Task HashGetAllAsync_FAF()
    {
        for (int i = 0; i < COUNT; i++)
        {
            await db.HashGetAllAsync(HashKey, CommandFlags.FireAndForget);
            await db.PingAsync(); // to wait for response
        }
    }
}

