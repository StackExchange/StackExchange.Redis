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
    public async Task StringBitCount()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitCount(key);
        var r2 = db.StringBitCount(key, start: 0, end: 0);
        var r3 = db.StringBitCount(key, start: 1);
        var r4 = db.StringBitCount(key, end: 1);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(22, r3);
        Assert.Equal(10, r4);

        var flags = CommandFlags.None;
        r1 = db.StringBitCount(key, flags: flags);
        r2 = db.StringBitCount(key, start: 0, end: 0, flags: flags);
        r3 = db.StringBitCount(key, start: 1, flags: flags);
        r4 = db.StringBitCount(key, end: 1, flags: flags);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(22, r3);
        Assert.Equal(10, r4);

        // Async

        r1 = await db.StringBitCountAsync(key);
        r2 = await db.StringBitCountAsync(key, start: 0, end: 0);
        r3 = await db.StringBitCountAsync(key, start: 1);
        r4 = await db.StringBitCountAsync(key, end: 1);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(22, r3);
        Assert.Equal(10, r4);

        r1 = await db.StringBitCountAsync(key, flags: flags);
        r2 = await db.StringBitCountAsync(key, start: 0, end: 0, flags: flags);
        r3 = await db.StringBitCountAsync(key, start: 1, flags: flags);
        r4 = await db.StringBitCountAsync(key, end: 1, flags: flags);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(22, r3);
        Assert.Equal(10, r4);
    }

    [Fact]
    public async Task StringBitPosition()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foo", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitPosition(key, true);
        var r2 = db.StringBitPosition(key, true, start: 1, end: 3);
        var r3 = db.StringBitPosition(key, true, start: 1);
        var r4 = db.StringBitPosition(key, true, end: 3);

        Assert.Equal(1, r1);
        Assert.Equal(9, r2);
        Assert.Equal(9, r3);
        Assert.Equal(1, r4);

        var flags = CommandFlags.None;
        r1 = db.StringBitPosition(key, true, flags: flags);
        r2 = db.StringBitPosition(key, true, start: 1, end: 3, flags: flags);
        r3 = db.StringBitPosition(key, true, start: 1, flags: flags);
        r4 = db.StringBitPosition(key, true, end: 3, flags: flags);

        Assert.Equal(1, r1);
        Assert.Equal(9, r2);
        Assert.Equal(9, r3);
        Assert.Equal(1, r4);

        // Async

        r1 = await db.StringBitPositionAsync(key, true);
        r2 = await db.StringBitPositionAsync(key, true, start: 1, end: 3);
        r3 = await db.StringBitPositionAsync(key, true, start: 1);
        r4 = await db.StringBitPositionAsync(key, true, end: 3);

        Assert.Equal(1, r1);
        Assert.Equal(9, r2);
        Assert.Equal(9, r3);
        Assert.Equal(1, r4);

        r1 = await db.StringBitPositionAsync(key, true, flags: flags);
        r2 = await db.StringBitPositionAsync(key, true, start: 1, end: 3, flags: flags);
        r3 = await db.StringBitPositionAsync(key, true, start: 1, flags: flags);
        r4 = await db.StringBitPositionAsync(key, true, end: 3, flags: flags);

        Assert.Equal(1, r1);
        Assert.Equal(9, r2);
        Assert.Equal(9, r3);
        Assert.Equal(1, r4);
    }

    [Fact]
    public async Task StringGet()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisValue val = "myval";
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
