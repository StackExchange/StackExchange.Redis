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
