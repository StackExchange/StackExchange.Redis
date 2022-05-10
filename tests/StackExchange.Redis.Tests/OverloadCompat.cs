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
public class OverloadCompat : TestBase
{
    public OverloadCompat(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

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

    public async Task StringBitCount()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);

        db.StringBitCount(key);
        db.StringBitCount(key, 1);
        db.StringBitCount(key, 0, 0);
        db.StringBitCount(key, start: 1);
        db.StringBitCount(key, end: 1);
        db.StringBitCount(key, start: 1, end: 1);

        var flags = CommandFlags.None;
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

        var flags = CommandFlags.None;
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
}
