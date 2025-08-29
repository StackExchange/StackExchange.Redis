using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
#if !TEST_BASELINE
using Resp;
using RESPite.Redis;
#if !PREVIEW_LANGVER
using RESPite.Redis.Alt; // needed for AsStrings() etc
#endif
#endif
using StackExchange.Redis;

namespace BasicTest;

[Config(typeof(CustomConfig))]
public class RedisBenchmarks : IDisposable
{
    private SocketManager mgr;
    private ConnectionMultiplexer connection;
    private IDatabase db;
#if !TEST_BASELINE
    private Resp.RespConnectionPool pool, customPool;
#endif

    [GlobalSetup]
    public void Setup()
    {
        // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();
#if !TEST_BASELINE
        pool = new();
#pragma warning disable CS0618 // Type or member is obsolete
        customPool = new() { UseCustomNetworkStream = true };
#pragma warning restore CS0618 // Type or member is obsolete
#endif
        // var options = ConfigurationOptions.Parse("127.0.0.1:6379");
        // connection = ConnectionMultiplexer.Connect(options);
        // db = connection.GetDatabase(3);
        //
        // db.KeyDelete(GeoKey);
        // db.KeyDelete(StringKey_K);
        // db.StringSet(StringKey_K, StringValue_S);
        // db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
        // db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");
        //
        // db.KeyDelete(HashKey);
        // for (int i = 0; i < 1000; i++)
        // {
        //     db.HashSet(HashKey, i, i);
        // }
    }

    public const string StringKey_S = "string", StringValue_S = "some suitably non-trivial value";

    public static readonly RedisKey GeoKey = "GeoTest",
        IncrByKey = "counter",
        StringKey_K = StringKey_S,
        HashKey = "hash";

    public static readonly RedisValue StringValue_V = StringValue_S;

    void IDisposable.Dispose()
    {
#if !TEST_BASELINE
        pool?.Dispose();
        customPool?.Dispose();
#endif
        mgr?.Dispose();
        connection?.Dispose();
        mgr = null;
        db = null;
        connection = null;
        GC.SuppressFinalize(this);
    }

    public const int OperationsPerInvoke = 128;

    /// <summary>
    /// Run INCRBY lots of times.
    /// </summary>
    // [Benchmark(Description = "INCRBY/s", OperationsPerInvoke = COUNT)]
    public int ExecuteIncrBy()
    {
        var rand = new Random(12345);

        db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
        int expected = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
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
    /// Run INCRBY lots of times.
    /// </summary>
    // [Benchmark(Description = "INCRBY/a", OperationsPerInvoke = COUNT)]
    public async Task<int> ExecuteIncrByAsync()
    {
        var rand = new Random(12345);

        db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
        int expected = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
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
    /// Run GEORADIUS lots of times.
    /// </summary>
    // [Benchmark(Description = "GEORADIUS/s", OperationsPerInvoke = COUNT)]
    public int ExecuteGeoRadius()
    {
        int total = 0;
        const GeoRadiusOptions options = GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance |
                                         GeoRadiusOptions.WithGeoHash;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            var results = db.GeoRadius(
                GeoKey,
                15,
                37,
                200,
                GeoUnit.Kilometers,
                options: options);
            total += results.Length;
        }

        return total;
    }

    /// <summary>
    /// Run GEORADIUS lots of times.
    /// </summary>
    // [Benchmark(Description = "GEORADIUS/a", OperationsPerInvoke = COUNT)]
    public async Task<int> ExecuteGeoRadiusAsync()
    {
        var options = GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance |
                      GeoRadiusOptions.WithGeoHash;
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            var results = await db.GeoRadiusAsync(
                    GeoKey,
                    15,
                    37,
                    200,
                    GeoUnit.Kilometers,
                    options: options)
                .ConfigureAwait(false);
            total += results.Length;
        }

        return total;
    }

    /// <summary>
    /// Run StringSet lots of times.
    /// </summary>
    // [Benchmark(Description = "StringSet/s", OperationsPerInvoke = COUNT)]
    public void StringSet()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            db.StringSet(StringKey_K, StringValue_V);
        }
    }

    /// <summary>
    /// Run StringGet lots of times.
    /// </summary>
    // [Benchmark(Description = "StringGet/s", OperationsPerInvoke = COUNT)]
    public void StringGet()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            db.StringGet(StringKey_K);
        }
    }
#if !TEST_BASELINE
    /// <summary>
    /// Run StringSet lots of times.
    /// </summary>
    // [Benchmark(Description = "C StringSet/s", OperationsPerInvoke = COUNT)]
    public void StringSet_Core()
    {
        using var conn = pool.GetConnection();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            s.Set(StringKey_S, StringValue_S);
        }
    }

    /// <summary>
    /// Run StringGet lots of times.
    /// </summary>
    // [Benchmark(Description = "C StringGet/s", OperationsPerInvoke = COUNT)]
    public void StringGet_Core()
    {
        using var conn = pool.GetConnection();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            s.Get(StringKey_S);
        }
    }

    /// <summary>
    /// Run StringSet lots of times.
    /// </summary>
    // [Benchmark(Description = "PC StringSet/s", OperationsPerInvoke = COUNT)]
    public void StringSet_Pipelined_Core()
    {
        using var conn = pool.GetConnection().ForPipeline();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            s.Set(StringKey_S, StringValue_S);
        }
    }

    /// <summary>
    /// Run StringSet lots of times.
    /// </summary>
    // [Benchmark(Description = "PCA StringSet/s", OperationsPerInvoke = COUNT)]
    public async Task StringSet_Pipelined_Core_Async()
    {
        using var conn = pool.GetConnection().ForPipeline();
        var ctx = conn.Context;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#if PREVIEW_LANGVER
            await ctx.Strings.SetAsync(StringKey_S, StringValue_S);
#else
            await ctx.AsStrings().SetAsync(StringKey_S, StringValue_S);
#endif
        }
    }

    /// <summary>
    /// Run StringGet lots of times.
    /// </summary>
    // [Benchmark(Description = "PC StringGet/s", OperationsPerInvoke = COUNT)]
    public void StringGet_Pipelined_Core()
    {
        using var conn = pool.GetConnection().ForPipeline();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            s.Get(StringKey_S);
        }
    }

    /// <summary>
    /// Run StringGet lots of times.
    /// </summary>
    // [Benchmark(Description = "PCA StringGet/s", OperationsPerInvoke = COUNT)]
    public async Task StringGet_Pipelined_Core_Async()
    {
        using var conn = pool.GetConnection().ForPipeline();
        var ctx = conn.Context;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#if PREVIEW_LANGVER
            await ctx.Strings.GetAsync(StringKey_S);
#else
            await ctx.AsStrings().GetAsync(StringKey_S);
#endif
        }
    }
#endif

    /// <summary>
    /// Run HashGetAll lots of times.
    /// </summary>
    // [Benchmark(Description = "HashGetAll F+F/s", OperationsPerInvoke = COUNT)]
    public void HashGetAll_FAF()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            db.HashGetAll(HashKey, CommandFlags.FireAndForget);
            db.Ping(); // to wait for response
        }
    }

    /// <summary>
    /// Run HashGetAll lots of times.
    /// </summary>
    // [Benchmark(Description = "HashGetAll F+F/a", OperationsPerInvoke = COUNT)]
    public async Task HashGetAllAsync_FAF()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            await db.HashGetAllAsync(HashKey, CommandFlags.FireAndForget);
            await db.PingAsync(); // to wait for response
        }
    }

    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    // [Benchmark(Description = "old incr", OperationsPerInvoke = OperationsPerInvoke)]
    public int IncrBy_Old()
    {
        RedisValue value = 0;
        db.StringSet(StringKey_K, value);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            value = db.StringIncrement(StringKey_K);
        }

        return (int)value;
    }

#if !TEST_BASELINE
    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    [Benchmark(Description = "new incr /p", OperationsPerInvoke = OperationsPerInvoke)]
    public int IncrBy_New_Pipelined()
    {
        using var conn = pool.GetConnection().ForPipeline();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        int value = 0;
        s.Set(StringKey_S, value);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            value = s.Incr(StringKey_K);
        }

        return value;
    }

    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    [Benchmark(Description = "new incr /p/a", OperationsPerInvoke = OperationsPerInvoke)]
    public async Task<int> IncrBy_New_Pipelined_Async()
    {
        using var conn = pool.GetConnection().ForPipeline();
        var ctx = conn.Context;
        int value = 0;
#if PREVIEW_LANGVER
        await ctx.Strings.SetAsync(StringKey_S, value);
#else
        await ctx.AsStrings().SetAsync(StringKey_S, value);
#endif
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#if PREVIEW_LANGVER
            value = await ctx.Strings.IncrAsync(StringKey_K);
#else
            value = await ctx.AsStrings().IncrAsync(StringKey_K);
#endif
        }

        return value;
    }

    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    [Benchmark(Description = "new incr", OperationsPerInvoke = OperationsPerInvoke)]
    public int IncrBy_New()
    {
        using var conn = pool.GetConnection();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        int value = 0;
        s.Set(StringKey_S, value);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            value = s.Incr(StringKey_K);
        }

        return value;
    }

    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    // [Benchmark(Description = "new incr /pc", OperationsPerInvoke = OperationsPerInvoke)]
    public int IncrBy_New_Pipelined_Custom()
    {
        using var conn = customPool.GetConnection().ForPipeline();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        int value = 0;
        s.Set(StringKey_S, value);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            value = s.Incr(StringKey_K);
        }

        return value;
    }

    /// <summary>
    /// Run incr lots of times.
    /// </summary>
    // [Benchmark(Description = "new incr /c", OperationsPerInvoke = OperationsPerInvoke)]
    public int IncrBy_New_Custom()
    {
        using var conn = customPool.GetConnection();
#if PREVIEW_LANGVER
        ref readonly RedisStrings s = ref conn.Context.Strings;
#else
        var s = conn.Context.AsStrings();
#endif
        int value = 0;
        s.Set(StringKey_S, value);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            value = s.Incr(StringKey_K);
        }

        return value;
    }
#endif
}
