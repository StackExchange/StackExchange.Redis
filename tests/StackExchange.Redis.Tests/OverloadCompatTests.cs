using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

/// <summary>
/// This test set is for when we add an overload, to making sure all
/// past versions work correctly and aren't source breaking.
/// </summary>
[Collection(SharedConnectionFixture.Key)]
public class OverloadCompatTests : TestBase
{
    public OverloadCompatTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    [Fact]
    public async Task KeyExpire()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        var expiresIn = TimeSpan.FromSeconds(10);
        var expireTime = DateTime.UtcNow.AddHours(1);
        var when = ExpireWhen.Always;
        var flags = CommandFlags.None;

        db.KeyExpire(key, expiresIn);
        db.KeyExpire(key, expiresIn, when);
        db.KeyExpire(key, expiresIn, when: when);
        db.KeyExpire(key, expiresIn, flags);
        db.KeyExpire(key, expiresIn, flags: flags);
        db.KeyExpire(key, expiresIn, when, flags);
        db.KeyExpire(key, expiresIn, when: when, flags: flags);

        db.KeyExpire(key, expireTime);
        db.KeyExpire(key, expireTime, when);
        db.KeyExpire(key, expireTime, when: when);
        db.KeyExpire(key, expireTime, flags);
        db.KeyExpire(key, expireTime, flags: flags);
        db.KeyExpire(key, expireTime, when, flags);
        db.KeyExpire(key, expireTime, when: when, flags: flags);

        // Async

        await db.KeyExpireAsync(key, expiresIn);
        await db.KeyExpireAsync(key, expiresIn, when);
        await db.KeyExpireAsync(key, expiresIn, when: when);
        await db.KeyExpireAsync(key, expiresIn, flags);
        await db.KeyExpireAsync(key, expiresIn, flags: flags);
        await db.KeyExpireAsync(key, expiresIn, when, flags);
        await db.KeyExpireAsync(key, expiresIn, when: when, flags: flags);

        await db.KeyExpireAsync(key, expireTime);
        await db.KeyExpireAsync(key, expireTime, when);
        await db.KeyExpireAsync(key, expireTime, when: when);
        await db.KeyExpireAsync(key, expireTime, flags);
        await db.KeyExpireAsync(key, expireTime, flags: flags);
        await db.KeyExpireAsync(key, expireTime, when, flags);
        await db.KeyExpireAsync(key, expireTime, when: when, flags: flags);
    }

    [Fact]
    public async Task StringBitCount()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        var flags = CommandFlags.None;

        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);

        db.StringBitCount(key);
        db.StringBitCount(key, 1);
        db.StringBitCount(key, 0, 0);
        db.StringBitCount(key, start: 1);
        db.StringBitCount(key, end: 1);
        db.StringBitCount(key, start: 1, end: 1);

        db.StringBitCount(key, flags: flags);
        db.StringBitCount(key, 0, 0, flags);
        db.StringBitCount(key, 1, flags: flags);
        db.StringBitCount(key, 1, 1, flags: flags);
        db.StringBitCount(key, start: 1, flags: flags);
        db.StringBitCount(key, end: 1, flags: flags);
        db.StringBitCount(key, start: 1, end: 1, flags);
        db.StringBitCount(key, start: 1, end: 1, flags: flags);

        // Async

        await db.StringBitCountAsync(key);
        await db.StringBitCountAsync(key, 1);
        await db.StringBitCountAsync(key, 0, 0);
        await db.StringBitCountAsync(key, start: 1);
        await db.StringBitCountAsync(key, end: 1);
        await db.StringBitCountAsync(key, start: 1, end: 1);

        await db.StringBitCountAsync(key, flags: flags);
        await db.StringBitCountAsync(key, 0, 0, flags);
        await db.StringBitCountAsync(key, 1, flags: flags);
        await db.StringBitCountAsync(key, 1, 1, flags: flags);
        await db.StringBitCountAsync(key, start: 1, flags: flags);
        await db.StringBitCountAsync(key, end: 1, flags: flags);
        await db.StringBitCountAsync(key, start: 1, end: 1, flags);
        await db.StringBitCountAsync(key, start: 1, end: 1, flags: flags);
    }

    [Fact]
    public async Task StringBitPosition()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        var flags = CommandFlags.None;

        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foo", flags: CommandFlags.FireAndForget);

        db.StringBitPosition(key, true);
        db.StringBitPosition(key, true, 1);
        db.StringBitPosition(key, true, 1, 3);
        db.StringBitPosition(key, bit: true);
        db.StringBitPosition(key, bit: true, start: 1);
        db.StringBitPosition(key, bit: true, end: 1);
        db.StringBitPosition(key, bit: true, start: 1, end: 1);
        db.StringBitPosition(key, true, start: 1, end: 1);

        db.StringBitPosition(key, true, flags: flags);
        db.StringBitPosition(key, true, 1, 3, flags);
        db.StringBitPosition(key, true, 1, flags: flags);
        db.StringBitPosition(key, bit: true, flags: flags);
        db.StringBitPosition(key, bit: true, start: 1, flags: flags);
        db.StringBitPosition(key, bit: true, end: 1, flags: flags);
        db.StringBitPosition(key, bit: true, start: 1, end: 1, flags: flags);
        db.StringBitPosition(key, true, start: 1, end: 1, flags: flags);

        // Async

        await db.StringBitPositionAsync(key, true);
        await db.StringBitPositionAsync(key, true, 1);
        await db.StringBitPositionAsync(key, true, 1, 3);
        await db.StringBitPositionAsync(key, bit: true);
        await db.StringBitPositionAsync(key, bit: true, start: 1);
        await db.StringBitPositionAsync(key, bit: true, end: 1);
        await db.StringBitPositionAsync(key, bit: true, start: 1, end: 1);
        await db.StringBitPositionAsync(key, true, start: 1, end: 1);

        await db.StringBitPositionAsync(key, true, flags: flags);
        await db.StringBitPositionAsync(key, true, 1, 3, flags);
        await db.StringBitPositionAsync(key, true, 1, flags: flags);
        await db.StringBitPositionAsync(key, bit: true, flags: flags);
        await db.StringBitPositionAsync(key, bit: true, start: 1, flags: flags);
        await db.StringBitPositionAsync(key, bit: true, end: 1, flags: flags);
        await db.StringBitPositionAsync(key, bit: true, start: 1, end: 1, flags: flags);
        await db.StringBitPositionAsync(key, true, start: 1, end: 1, flags: flags);
    }

    [Fact]
    public async Task SortedSetAdd()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisValue val = "myval";
        var score = 1.0d;
        var values = new SortedSetEntry[]{new SortedSetEntry(val, score)};
        var when = When.Exists;
        var flags = CommandFlags.None;

        db.SortedSetAdd(key, val, score);
        db.SortedSetAdd(key, val, score, when);
        db.SortedSetAdd(key, val, score, when: when);
        db.SortedSetAdd(key, val, score, flags);
        db.SortedSetAdd(key, val, score, flags: flags);
        db.SortedSetAdd(key, val, score, when, flags);
        db.SortedSetAdd(key, val, score, when, flags: flags);
        db.SortedSetAdd(key, val, score, when: when, flags);
        db.SortedSetAdd(key, val, score, when: when, flags: flags);

        db.SortedSetAdd(key, values);
        db.SortedSetAdd(key, values, when);
        db.SortedSetAdd(key, values, when: when);
        db.SortedSetAdd(key, values, flags);
        db.SortedSetAdd(key, values, flags: flags);
        db.SortedSetAdd(key, values, when, flags);
        db.SortedSetAdd(key, values, when, flags: flags);
        db.SortedSetAdd(key, values, when: when, flags);
        db.SortedSetAdd(key, values, when: when, flags: flags);

        // Async

        await db.SortedSetAddAsync(key, val, score);
        await db.SortedSetAddAsync(key, val, score, when);
        await db.SortedSetAddAsync(key, val, score, when: when);
        await db.SortedSetAddAsync(key, val, score, flags);
        await db.SortedSetAddAsync(key, val, score, flags: flags);
        await db.SortedSetAddAsync(key, val, score, when, flags);
        await db.SortedSetAddAsync(key, val, score, when, flags: flags);
        await db.SortedSetAddAsync(key, val, score, when: when, flags);
        await db.SortedSetAddAsync(key, val, score, when: when, flags: flags);

        await db.SortedSetAddAsync(key, values);
        await db.SortedSetAddAsync(key, values, when);
        await db.SortedSetAddAsync(key, values, when: when);
        await db.SortedSetAddAsync(key, values, flags);
        await db.SortedSetAddAsync(key, values, flags: flags);
        await db.SortedSetAddAsync(key, values, when, flags);
        await db.SortedSetAddAsync(key, values, when, flags: flags);
        await db.SortedSetAddAsync(key, values, when: when, flags);
        await db.SortedSetAddAsync(key, values, when: when, flags: flags);
    }

    [Fact]
    public async Task StringSet()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        var val = "myval";
        var expiresIn = TimeSpan.FromSeconds(10);
        var when = When.Always;
        var flags = CommandFlags.None;

        db.StringSet(key, val);
        db.StringSet(key, val, expiry: expiresIn);
        db.StringSet(key, val, when: when);
        db.StringSet(key, val, flags: flags);
        db.StringSet(key, val, expiry: expiresIn, when: when);
        db.StringSet(key, val, expiry: expiresIn, when: when, flags: flags);
        db.StringSet(key, val, expiry: expiresIn, when: when, flags: flags);

        db.StringSet(key, val, expiresIn, When.NotExists);
        db.StringSet(key, val, expiresIn, When.NotExists, flags);
        db.StringSet(key, val, null);
        db.StringSet(key, val, null, When.NotExists);
        db.StringSet(key, val, null, When.NotExists, flags);

        // Async

        await db.StringSetAsync(key, val);
        await db.StringSetAsync(key, val, expiry: expiresIn);
        await db.StringSetAsync(key, val, when: when);
        await db.StringSetAsync(key, val, flags: flags);
        await db.StringSetAsync(key, val, expiry: expiresIn, when: when);
        await db.StringSetAsync(key, val, expiry: expiresIn, when: when, flags: flags);
        await db.StringSetAsync(key, val, expiry: expiresIn, when: when, flags: flags);

        await db.StringSetAsync(key, val, expiresIn, When.NotExists);
        await db.StringSetAsync(key, val, expiresIn, When.NotExists, flags);
        await db.StringSetAsync(key, val, null);
        await db.StringSetAsync(key, val, null, When.NotExists);
        await db.StringSetAsync(key, val, null, When.NotExists, flags);
    }

    [Fact]
    public async Task ScriptEvaluate()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        RedisKey[] keyArr = Array.Empty<RedisKey>();
        ReadOnlyMemory<RedisKey> keyRom = keyArr;

        RedisValue[] valueArr= Array.Empty<RedisValue>();
        ReadOnlyMemory<RedisValue> valueRom = valueArr;

        const string script = "return 0";

        // sync

        db.ScriptEvaluate(script);
        db.ScriptEvaluate(script, keyArr);
        db.ScriptEvaluate(script, keyRom);
        // db.ScriptEvaluate(script, default); // BOOM
        // db.ScriptEvaluate(script, null); // BOOM
        db.ScriptEvaluate(script, (RedisKey)default);
        db.ScriptEvaluate(script, (RedisKey[]?)null);
        db.ScriptEvaluate(script, (ReadOnlyMemory<RedisKey>)null);
        db.ScriptEvaluate(script, (RedisKey[]?)default);
        db.ScriptEvaluate(script, (ReadOnlyMemory<RedisKey>)default);
        db.ScriptEvaluate(script, (RedisKey)default);
        db.ScriptEvaluate(script, default(RedisKey[]?));
        db.ScriptEvaluate(script, default(RedisKey));
        db.ScriptEvaluate(script, default(ReadOnlyMemory<RedisKey>));

        db.ScriptEvaluate(script, values: valueArr);
        db.ScriptEvaluate(script, keyArr, values: valueArr);
        db.ScriptEvaluate(script, keyRom, values: valueArr);
        db.ScriptEvaluate(script, null, values: valueArr);
        db.ScriptEvaluate(script, default, values: valueArr);

        db.ScriptEvaluate(script, keyArr, values: valueRom);
        db.ScriptEvaluate(script, keyRom, values: valueRom);
        // db.ScriptEvaluate(script, default, values: valueRom); // BOOM
        // db.ScriptEvaluate(script, null, values: valueRom); // BOOM
        db.ScriptEvaluate(script, (RedisKey)default, values: valueRom);
        db.ScriptEvaluate(script, (RedisKey[]?)null, values: valueRom);
        db.ScriptEvaluate(script, (ReadOnlyMemory<RedisKey>)null, values: valueRom);
        db.ScriptEvaluate(script, (RedisKey[]?)default, values: valueRom);
        db.ScriptEvaluate(script, (ReadOnlyMemory<RedisKey>)default, values: valueRom);
        db.ScriptEvaluate(script, (RedisKey)default, values: valueRom);
        db.ScriptEvaluate(script, default(RedisKey[]?), values: valueRom);
        db.ScriptEvaluate(script, default(RedisKey), values: valueRom);
        db.ScriptEvaluate(script, default(ReadOnlyMemory<RedisKey>), values: valueRom);

        db.ScriptEvaluate(script, values: null);
        db.ScriptEvaluate(script, keyArr, values: null);
        db.ScriptEvaluate(script, keyRom, values: null);
        db.ScriptEvaluate(script, null, values: null);
        db.ScriptEvaluate(script, default, values: null);

        db.ScriptEvaluate(script, values: default);
        db.ScriptEvaluate(script, keyArr, values: default);
        db.ScriptEvaluate(script, keyRom, values: default);
        db.ScriptEvaluate(script, null, values: default);
        db.ScriptEvaluate(script, default, values: default);

        // async

        await db.ScriptEvaluateAsync(script);
        await db.ScriptEvaluateAsync(script, keyArr);
        await db.ScriptEvaluateAsync(script, keyRom);
        // await db.ScriptEvaluateAsync(script, default); // BOOM
        // await db.ScriptEvaluateAsync(script, null); // BOOM
        await db.ScriptEvaluateAsync(script, (RedisKey)default);
        await db.ScriptEvaluateAsync(script, (RedisKey[]?)null);
        await db.ScriptEvaluateAsync(script, (ReadOnlyMemory<RedisKey>)null);
        await db.ScriptEvaluateAsync(script, (RedisKey[]?)default);
        await db.ScriptEvaluateAsync(script, (ReadOnlyMemory<RedisKey>)default);
        await db.ScriptEvaluateAsync(script, (RedisKey)default);
        await db.ScriptEvaluateAsync(script, default(RedisKey[]?));
        await db.ScriptEvaluateAsync(script, default(RedisKey));
        await db.ScriptEvaluateAsync(script, default(ReadOnlyMemory<RedisKey>));

        await db.ScriptEvaluateAsync(script, values: valueArr);
        await db.ScriptEvaluateAsync(script, keyArr, values: valueArr);
        await db.ScriptEvaluateAsync(script, keyRom, values: valueArr);
        await db.ScriptEvaluateAsync(script, null, values: valueArr);
        await db.ScriptEvaluateAsync(script, default, values: valueArr);

        await db.ScriptEvaluateAsync(script, keyArr, values: valueRom);
        await db.ScriptEvaluateAsync(script, keyRom, values: valueRom);
        // await db.ScriptEvaluateAsync(script, default, values: valueRom); // BOOM
        // await db.ScriptEvaluateAsync(script, null, values: valueRom); // BOOM
        await db.ScriptEvaluateAsync(script, (RedisKey)default, values: valueRom);
        await db.ScriptEvaluateAsync(script, (RedisKey[]?)null, values: valueRom);
        await db.ScriptEvaluateAsync(script, (ReadOnlyMemory<RedisKey>)null, values: valueRom);
        await db.ScriptEvaluateAsync(script, (RedisKey[]?)default, values: valueRom);
        await db.ScriptEvaluateAsync(script, (ReadOnlyMemory<RedisKey>)default, values: valueRom);
        await db.ScriptEvaluateAsync(script, (RedisKey)default, values: valueRom);
        await db.ScriptEvaluateAsync(script, default(RedisKey[]?), values: valueRom);
        await db.ScriptEvaluateAsync(script, default(RedisKey), values: valueRom);
        await db.ScriptEvaluateAsync(script, default(ReadOnlyMemory<RedisKey>), values: valueRom);

        await db.ScriptEvaluateAsync(script, values: null);
        await db.ScriptEvaluateAsync(script, keyArr, values: null);
        await db.ScriptEvaluateAsync(script, keyRom, values: null);
        await db.ScriptEvaluateAsync(script, null, values: null);
        await db.ScriptEvaluateAsync(script, default, values: null);

        await db.ScriptEvaluateAsync(script, values: default);
        await db.ScriptEvaluateAsync(script, keyArr, values: default);
        await db.ScriptEvaluateAsync(script, keyRom, values: default);
        await db.ScriptEvaluateAsync(script, null, values: default);
        await db.ScriptEvaluateAsync(script, default, values: default);
    }
}
