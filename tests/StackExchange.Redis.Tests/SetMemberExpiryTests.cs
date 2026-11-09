using System;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for the Redis 8.12 set-member expiration family:
/// <see href="https://redis.io/commands/saddex"/> and friends.
/// </summary>
[RunPerProtocol]
public class SetMemberExpiryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    // see HashFieldTests: absolute expiries (the DateTime overloads) are computed against the client clock and read
    // back against the server's, which can sit a few ms off on CI
    private const double ClockSkewToleranceMs = 1000;

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DateTime nextCentury = new(2101, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeSpan oneYear = TimeSpan.FromMilliseconds(31536000000);

    private readonly RedisValue[] members = ["m1", "m2"];

    [Fact]
    public void SetAddWithExpiry()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        Assert.True(db.SetAdd(key, "m1", oneYear));
        var ttl = Assert.Single(db.SetMemberGetTimeToLive(key, ["m1"]));
        Assert.InRange(ttl, oneYear.TotalMilliseconds - 10_000, oneYear.TotalMilliseconds);

        // absolute form
        Assert.True(db.SetAdd(key, "m2", nextCentury));
        var expireTime = Assert.Single(db.SetMemberGetExpireDateTime(key, ["m2"]));
        var expected = (nextCentury - UnixEpoch).TotalMilliseconds;
        Assert.InRange(expireTime, expected - ClockSkewToleranceMs, expected + ClockSkewToleranceMs);
    }

    [Fact]
    public async Task SetAddWithExpiryAsync()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        Assert.True(await db.SetAddAsync(key, members, oneYear));
        var ttls = await db.SetMemberGetTimeToLiveAsync(key, members);
        Assert.All(ttls, ttl => Assert.InRange(ttl, oneYear.TotalMilliseconds - 10_000, oneYear.TotalMilliseconds));
    }

    [Fact]
    public void SetAddWithExpiryKeepTtl()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        Assert.True(db.SetAdd(key, "m1", oneYear));

        // KEEPTTL: the existing expiration survives the re-add
        Assert.False(db.SetAdd(key, "m1", Expiration.KeepTtl));
        var ttl = Assert.Single(db.SetMemberGetTimeToLive(key, ["m1"]));
        Assert.True(ttl > 0);

        // the bare form discards it
        Assert.True(db.SetAdd(key, "m1", Expiration.Default));
        ttl = Assert.Single(db.SetMemberGetTimeToLive(key, ["m1"]));
        Assert.Equal(-1, ttl); // present, no expiration
    }

    [Fact]
    public void SetAddWithExpiryWhen()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        // MNX on a fresh key: all new, applies
        Assert.True(db.SetAdd(key, members, oneYear, when: When.NotExists));

        // MNX with one existing member vetoes the whole batch: nothing is added at all
        Assert.False(db.SetAdd(key, [(RedisValue)"m1", "newcomer"], oneYear, when: When.NotExists));
        Assert.False(db.SetContains(key, "newcomer"));

        // MXX: all exist, applies
        Assert.True(db.SetAdd(key, members, oneYear + TimeSpan.FromMinutes(1), when: When.Exists));

        // MXX with one missing member vetoes the whole batch
        Assert.False(db.SetAdd(key, [(RedisValue)"m1", "ghost"], oneYear, when: When.Exists));
    }

    [Fact]
    public void SetAddWithExpiryPastDeletes()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        db.SetAdd(key, "m1");

        // an expiration already in the past deletes the member - and that counts as a change
        Assert.True(db.SetAdd(key, "m1", DateTime.UtcNow.AddMinutes(-5)));
        Assert.False(db.SetContains(key, "m1"));
    }

    [Fact]
    public void SetMemberExpire()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, members);

        var result = db.SetMemberExpire(key, members, oneYear);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], result);

        result = db.SetMemberExpire(key, members, nextCentury);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], result);
    }

    [Fact]
    public async Task SetMemberExpireAsync()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        await db.SetAddAsync(key, members);

        var result = await db.SetMemberExpireAsync(key, members, oneYear);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], result);

        result = await db.SetMemberExpireAsync(key, members, nextCentury);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], result);
    }

    [Fact]
    public void SetMemberExpireNoKey()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();

        var result = db.SetMemberExpire(key, members, oneYear);
        Assert.Equal([ExpireResult.NoSuchField, ExpireResult.NoSuchField], result);
    }

    [Fact]
    public void SetMemberExpireMissingMember()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, "m1");

        var result = db.SetMemberExpire(key, [(RedisValue)"m1", "ghost"], oneYear);
        Assert.Equal([ExpireResult.Success, ExpireResult.NoSuchField], result);
    }

    [Fact]
    public void SetMemberExpireConditions()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, members);

        // NX: only when the member has no expiry
        var result = db.SetMemberExpire(key, ["m1"], oneYear, ExpireWhen.HasNoExpiry);
        Assert.Equal([ExpireResult.Success], result);
        result = db.SetMemberExpire(key, ["m1"], oneYear, ExpireWhen.HasNoExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        // GT: a shorter expiry loses
        result = db.SetMemberExpire(key, ["m1"], TimeSpan.FromMinutes(5), ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        // GT vs no expiry: no expiry counts as infinite, so GT loses
        result = db.SetMemberExpire(key, ["m2"], oneYear, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        // LT against the pinned member wins
        result = db.SetMemberExpire(key, ["m2"], oneYear, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal([ExpireResult.Success], result);
    }

    [Fact]
    public void SetMemberExpirePastDeletes()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, "m1");

        var result = db.SetMemberExpire(key, ["m1"], DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal([ExpireResult.Due], result);
        Assert.False(db.SetContains(key, "m1"));
    }

    [Fact]
    public void SetMemberGetTimeToLive()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, members);
        db.SetMemberExpire(key, ["m1"], oneYear);

        var result = db.SetMemberGetTimeToLive(key, [(RedisValue)"m1", "m2", "ghost"]);
        Assert.Equal(3, result.Length);
        Assert.InRange(result[0], oneYear.TotalMilliseconds - 10_000, oneYear.TotalMilliseconds);
        Assert.Equal(-1, result[1]); // present, no expiration
        Assert.Equal(-2, result[2]); // no such member
    }

    [Fact]
    public async Task SetMemberGetTimeToLiveAsync()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        await db.SetAddAsync(key, "m1");
        await db.SetMemberExpireAsync(key, ["m1"], oneYear);

        var ttl = Assert.Single(await db.SetMemberGetTimeToLiveAsync(key, ["m1"]));
        Assert.InRange(ttl, oneYear.TotalMilliseconds - 10_000, oneYear.TotalMilliseconds);
    }

    [Fact]
    public void SetMemberGetExpireDateTime()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, members);
        db.SetMemberExpire(key, ["m1"], nextCentury);

        var result = db.SetMemberGetExpireDateTime(key, [(RedisValue)"m1", "m2", "ghost"]);
        Assert.Equal(3, result.Length);
        var expected = (nextCentury - UnixEpoch).TotalMilliseconds;
        Assert.InRange(result[0], expected - ClockSkewToleranceMs, expected + ClockSkewToleranceMs);
        Assert.Equal(-1, result[1]);
        Assert.Equal(-2, result[2]);
    }

    [Fact]
    public void SetMemberPersist()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        db.SetAdd(key, members);
        db.SetMemberExpire(key, ["m1"], oneYear);

        var result = db.SetMemberPersist(key, [(RedisValue)"m1", "m2", "ghost"]);
        Assert.Equal([PersistResult.Success, PersistResult.ConditionNotMet, PersistResult.NoSuchField], result);

        // pinned, not permanent: the member is still an ordinary member
        Assert.Equal(-1, Assert.Single(db.SetMemberGetTimeToLive(key, ["m1"])));
        Assert.True(db.SetRemove(key, "m1"));
    }

    [Fact]
    public async Task SetMemberPersistAsync()
    {
        var db = Create(require: RedisFeatures.v8_12_0).GetDatabase();
        var key = Me();
        await db.SetAddAsync(key, "m1");
        await db.SetMemberExpireAsync(key, ["m1"], oneYear);

        var result = await db.SetMemberPersistAsync(key, ["m1"]);
        Assert.Equal([PersistResult.Success], result);
    }

    [Fact]
    public void SetMemberExpiryWithKeyPrefix()
    {
        var conn = Create(require: RedisFeatures.v8_12_0);
        var key = Me();
        var prefixed = conn.GetDatabase().WithKeyPrefix("prefix:");

        Assert.True(prefixed.SetAdd(key, "m1", oneYear));
        var ttl = Assert.Single(prefixed.SetMemberGetTimeToLive(key, ["m1"]));
        Assert.True(ttl > 0);

        // the write really landed on the prefixed key
        var raw = conn.GetDatabase();
        Assert.True(raw.SetContains("prefix:" + key, "m1"));
        Assert.False(raw.KeyExists(key));
    }
}
