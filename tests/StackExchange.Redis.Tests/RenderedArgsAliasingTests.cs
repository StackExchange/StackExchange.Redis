using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The reason the arguments are rendered at the point of the call: once the call returns, the caller's
/// buffer is theirs again. These reuse a single argument array across overlapping, un-awaited calls,
/// mutating it between each - which is precisely what a caller renting one array from a pool would do,
/// and precisely what breaks if the request still refers to that memory when it is eventually written.
/// </summary>
[RunPerProtocol]
public class RenderedArgsAliasingTests(ITestOutputHelper output) : TestBase(output)
{
    private const int Iterations = 200;

    [Fact]
    public async Task ExecuteResp_SurvivesTheCallerMutatingOneSharedArgArray()
    {
        await using var conn = Create(shared: false);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        // a batch, so the writes are provably deferred: every message is queued here and none of them
        // reaches the wire until Execute() below, by which point the array holds only the last iteration.
        // Overlapping un-awaited calls on the open database are not enough on their own - an uncontended
        // write lock means each one is written inline, before the next iteration can stomp the array.
        var batch = db.CreateBatch();
        var args = new RedisKeyOrValue[3];
        var pending = new List<Task<RespResult>>(Iterations);
        for (int i = 0; i < Iterations; i++)
        {
            args[0] = key;
            args[1] = (RedisValue)("f" + i);
            args[2] = (RedisValue)("v" + i);
            pending.Add(batch.ExecuteRespAsync("HSET", args));
        }

        batch.Execute();

        foreach (var task in pending)
        {
            using var result = await task;
        }

        await AssertHashIsIntact(db, key);
    }

    [Fact]
    public async Task ScriptEvaluateResp_SurvivesTheCallerMutatingOneSharedArgArray()
    {
        await using var conn = Create(shared: false);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        const string Script = "return redis.call('hset', KEYS[1], ARGV[1], ARGV[2])";
        var batch = db.CreateBatch();
        var keys = new RedisKey[1];
        var values = new RedisValue[2];
        var pending = new List<Task<RespResult>>(Iterations);
        for (int i = 0; i < Iterations; i++)
        {
            keys[0] = key;
            values[0] = "f" + i;
            values[1] = "v" + i;
            pending.Add(batch.ScriptEvaluateRespAsync(Script, keys, values));
        }

        batch.Execute();

        foreach (var task in pending)
        {
            using var result = await task;
        }

        await AssertHashIsIntact(db, key);
    }

    [Fact]
    public async Task HashImport_SurvivesTheCallerMutatingOneSharedValuesArray()
    {
        await using var conn = Create(shared: false, require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        // the per-row bulk-import shape: one field-set, one values buffer, refilled for every row
        using var fieldSet = HashImport.Create("f");
        var batch = db.CreateBatch();
        var values = new RedisValue[1];
        var pending = new List<Task>(Iterations);
        for (int i = 0; i < Iterations; i++)
        {
            values[0] = "v" + i;
            pending.Add(batch.HashImportAsync((RedisKey)(Me() + ":" + i), fieldSet, values));
        }

        batch.Execute();
        await Task.WhenAll(pending);

        for (int i = 0; i < Iterations; i++)
        {
            Assert.Equal("v" + i, (string?)await db.HashGetAsync((RedisKey)(Me() + ":" + i), "f"));
        }
    }

    /// <summary>
    /// The shape the docs recommend: rent, issue the calls, return the buffer to the pool, and only then
    /// await. Queued through a batch so the writes are genuinely still pending when the buffer goes back -
    /// against the open database an uncontended write lock writes each call inline, so returning the array
    /// afterwards would prove nothing (a read-late implementation passes that version).
    /// </summary>
    [Fact]
    public async Task ArgumentsMayBeReturnedToThePoolBeforeAwaiting()
    {
        await using var conn = Create(shared: false);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var batch = db.CreateBatch();
        var args = ArrayPool<RedisKeyOrValue>.Shared.Rent(3);
        var pending = new List<Task<RespResult>>(Iterations);
        try
        {
            for (int i = 0; i < Iterations; i++)
            {
                args[0] = key;
                args[1] = (RedisValue)("f" + i);
                args[2] = (RedisValue)("v" + i);
                pending.Add(batch.ExecuteRespAsync("HSET", args.AsMemory(0, 3)));
            }
        }
        finally
        {
            ArrayPool<RedisKeyOrValue>.Shared.Return(args, clearArray: true);
        }

        // and behave like the next renter: take it back and fill it with something else entirely
        var stomp = ArrayPool<RedisKeyOrValue>.Shared.Rent(3);
        for (int i = 0; i < 3; i++) stomp[i] = (RedisValue)"stomped";

        batch.Execute();
        foreach (var task in pending)
        {
            using var result = await task;
        }

        ArrayPool<RedisKeyOrValue>.Shared.Return(stomp, clearArray: true);
        await AssertHashIsIntact(db, key);
    }

    private async Task AssertHashIsIntact(IDatabase db, RedisKey key)
    {
        // if any call had been written from the array *after* a later iteration overwrote it, that call
        // would have set some other field - so the hash would be short, and some values would be paired
        // with the wrong field
        var all = await db.HashGetAllAsync(key);
        var actual = new Dictionary<string, string>(all.Length);
        foreach (var entry in all) actual[entry.Name!] = entry.Value!;

        Assert.Equal(Iterations, actual.Count);
        for (int i = 0; i < Iterations; i++)
        {
            Assert.True(actual.TryGetValue("f" + i, out var value), $"field f{i} never arrived");
            Assert.Equal("v" + i, value);
        }
    }
}
