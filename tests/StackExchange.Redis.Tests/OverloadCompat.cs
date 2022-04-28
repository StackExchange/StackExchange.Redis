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
        RedisKey key = Me();
        var expiresIn = TimeSpan.FromSeconds(10);
        var expireTime = DateTime.UtcNow.AddHours(1);
        var when = ExpireWhen.Always;
        var flags = CommandFlags.None;

        db.KeyExpire(key, expiresIn);
        db.KeyExpire(key, expiresIn, when);
        db.KeyExpire(key, expiresIn, flags);
        db.KeyExpire(key, expiresIn, when, flags);

        db.KeyExpire(key, expireTime);
        db.KeyExpire(key, expireTime, when);
        db.KeyExpire(key, expireTime, flags);
        db.KeyExpire(key, expireTime, when, flags);

        // Async

        await db.KeyExpireAsync(key, expiresIn);
        await db.KeyExpireAsync(key, expiresIn, when);
        await db.KeyExpireAsync(key, expiresIn, flags);
        await db.KeyExpireAsync(key, expiresIn, when, flags);

        await db.KeyExpireAsync(key, expireTime);
        await db.KeyExpireAsync(key, expireTime, when);
        await db.KeyExpireAsync(key, expireTime, flags);
        await db.KeyExpireAsync(key, expireTime, when, flags);
    }

    [Fact]
    public async Task StringSet()
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
