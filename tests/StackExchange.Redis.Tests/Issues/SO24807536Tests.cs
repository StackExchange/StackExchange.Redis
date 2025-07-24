﻿using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class SO24807536Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task Exec()
    {
        await using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();

        // setup some data
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.HashSet(key, "full", "some value", flags: CommandFlags.FireAndForget);
        db.KeyExpire(key, TimeSpan.FromSeconds(2), CommandFlags.FireAndForget);

        // test while exists
        var keyExists = db.KeyExists(key);
        var ttl = db.KeyTimeToLive(key);
        var fullWait = db.HashGetAsync(key, "full", flags: CommandFlags.None);
        Assert.True(keyExists, "key exists");
        Assert.NotNull(ttl);
        Assert.Equal("some value", await fullWait);

        // wait for expiry
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => !db.KeyExists(key)).ForAwait();

        // test once expired
        keyExists = db.KeyExists(key);
        ttl = db.KeyTimeToLive(key);
        fullWait = db.HashGetAsync(key, "full", flags: CommandFlags.None);

        Assert.False(keyExists);
        Assert.Null(ttl);
        var r = await fullWait;
        Assert.True(r.IsNull);
        Assert.Null((string?)r);
    }
}
