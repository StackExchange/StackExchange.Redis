using System;
using System.Buffers;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.Benchmarks;

// Requires a real server on localhost:6379 (e.g. `docker compose -f tests/RedisConfigs/docker-compose.yml up -d --wait`
// from the repo root) - this is deliberately a round-trip benchmark, not a pure in-memory one, since the thing under
// test (ExecuteResp's low-allocation request/response path) only pays off relative to real request/response traffic.
//
// Field values are plain integers, and field names are precomputed once (not built per call): the point is to
// isolate what Execute/ExecuteResp themselves cost - object[] boxing and a RedisResult wrapper per element on the
// classic side, versus neither on the Resp side - rather than paying for a fresh string concatenation on every
// iteration of both benchmarks alike, which would swamp that difference.
[Config(typeof(CustomConfig))]
public class ExecuteRespBenchmarks
{
    private const int FieldCount = 32;

    private ConnectionMultiplexer _conn = null!;
    private IDatabase _db = null!;
    private RedisKey _key;
    private string[] _fieldNames = null!;

    [GlobalSetup]
    public void Setup()
    {
        _conn = ConnectionMultiplexer.Connect("127.0.0.1:6379");
        _db = _conn.GetDatabase();
        _key = "ExecuteRespBenchmarks:" + Guid.NewGuid();
        _fieldNames = new string[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            _fieldNames[i] = "field" + i;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.KeyDelete(_key);
        _conn.Dispose();
    }

    // Sum of 0..FieldCount-1, i.e. the values HSET below writes; both benchmarks must return this same
    // constant, read back via HGETALL, as a correctness check that the two paths agree.
    private const long ExpectedTally = FieldCount * (FieldCount - 1) / 2;

    // Each call below rents/returns (or leases/disposes) its own buffer, matching a single realistic
    // call rather than sharing one buffer across many iterations - see docs/Execute.md.

    [Benchmark(Baseline = true)]
    public async Task<long> Classic()
    {
        var args = new object[1 + (FieldCount * 2)];
        args[0] = _key;
        for (int i = 0; i < FieldCount; i++)
        {
            args[1 + (i * 2)] = _fieldNames[i];
            args[2 + (i * 2)] = (long)i;
        }
        await _db.ExecuteAsync("HSET", args).ConfigureAwait(false);

        var reply = await _db.ExecuteAsync("HGETALL", _key).ConfigureAwait(false);
        var pairs = (RedisResult[])reply!;
        long tally = 0;
        // pairs alternate field, value, field, value, ... regardless of which fields the server returned
        for (int i = 1; i < pairs.Length; i += 2)
        {
            tally += (long)pairs[i];
        }
        if (tally != ExpectedTally) throw new InvalidOperationException($"tally mismatch: {tally}");
        return tally;
    }

    [Benchmark]
    public async Task<long> Resp()
    {
        var setArgs = ArrayPool<RedisKeyOrValue>.Shared.Rent(1 + (FieldCount * 2));
        Task<RespResult> pendingSet;
        try
        {
            setArgs[0] = _key;
            for (int i = 0; i < FieldCount; i++)
            {
                setArgs[1 + (i * 2)] = (RedisValue)_fieldNames[i];
                setArgs[2 + (i * 2)] = (RedisValue)(long)i;
            }
            pendingSet = _db.ExecuteRespAsync("HSET", setArgs.AsMemory(0, 1 + (FieldCount * 2)));
        }
        finally
        {
            ArrayPool<RedisKeyOrValue>.Shared.Return(setArgs, clearArray: true);
        }
        using (await pendingSet.ConfigureAwait(false)) { }

        var getArgs = ArrayPool<RedisKeyOrValue>.Shared.Rent(1);
        Task<RespResult> pendingGet;
        try
        {
            getArgs[0] = _key;
            pendingGet = _db.ExecuteRespAsync("HGETALL", getArgs.AsMemory(0, 1));
        }
        finally
        {
            ArrayPool<RedisKeyOrValue>.Shared.Return(getArgs, clearArray: true);
        }

        using RespResult result = await pendingGet.ConfigureAwait(false);
        var children = result.Read().AggregateChildren();
        long tally = 0;
        int index = 0;
        while (children.MoveNext())
        {
            // even index = field name, odd index = value - see Classic() above
            if ((index & 1) == 1) tally += children.Value.ReadInt64();
            index++;
        }
        if (tally != ExpectedTally) throw new InvalidOperationException($"tally mismatch: {tally}");
        return tally;
    }
}
