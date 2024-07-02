using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using System.Threading.Tasks;
using System.Globalization;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands#hash"/>.
/// </summary>
[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class HashFieldTests : TestBase
{
    private readonly DateTime nextCentury = new DateTime(2101, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeSpan oneYearInMs = TimeSpan.FromMilliseconds(31536000000);

    private readonly HashEntry[] entries = new HashEntry[] { new("f1", 1), new("f2", 2) };

    private readonly RedisValue[] fields = new RedisValue[] { "f1", "f2" };


    public HashFieldTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture)
    {
    }

    [Fact]
    public void HashFieldExpire()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, }, fieldsResult);
    }

    [Fact]
    public void HashFieldExpireNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.NoSuchField, ExpireResult.NoSuchField }, fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.NoSuchField, ExpireResult.NoSuchField }, fieldsResult);
    }

    [Fact]
    public async void HashFieldExpireAsync()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);
    }

    [Fact]
    public async void HashFieldExpireAsyncNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.NoSuchField, ExpireResult.NoSuchField }, fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.NoSuchField, ExpireResult.NoSuchField }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeIsDue()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, new RedisValue[] { "f1" }, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { ExpireResult.Due }, result);
    }

    [Fact]
    public void HashFieldExpireNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, new RedisValue[] { "nonExistingField" }, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.NoSuchField }, result);
    }

    [Fact]
    public void HashFieldExpireConditionsSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, new HashEntry[] { new("f3", 3), new("f4", 4) });
        var initialExpire = db.HashFieldExpire(hashKey, new RedisValue[] { "f2", "f3", "f4" }, new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, ExpireResult.Success }, initialExpire);

        var result = db.HashFieldExpire(hashKey, new RedisValue[] { "f1" }, oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(new[] { ExpireResult.Success }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f2" }, oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal(new[] { ExpireResult.Success }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f3" }, nextCentury, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal(new[] { ExpireResult.Success }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f4" }, oneYearInMs, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal(new[] { ExpireResult.Success }, result);
    }

    [Fact]
    public void HashFieldExpireConditionsNotSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, new HashEntry[] { new("f3", 3), new("f4", 4) });
        var initialExpire = db.HashFieldExpire(hashKey, new RedisValue[] { "f2", "f3", "f4" }, new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, ExpireResult.Success }, initialExpire);

        var result = db.HashFieldExpire(hashKey, new RedisValue[] { "f1" }, oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal(new[] { ExpireResult.ConditionNotMet }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f2" }, oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(new[] { ExpireResult.ConditionNotMet }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f3" }, nextCentury, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal(new[] { ExpireResult.ConditionNotMet }, result);

        result = db.HashFieldExpire(hashKey, new RedisValue[] { "f4" }, oneYearInMs, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal(new[] { ExpireResult.ConditionNotMet }, result);
    }

    [Fact]
    public void HashFieldGetExpireDateTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, nextCentury);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetExpireDateTime(hashKey, new RedisValue[] { "f1" });
        Assert.Equal(new[] { ms }, result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new[] { ms, ms }, fieldsResult);
    }

    [Fact]
    public void HashFieldExpireFieldNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldGetExpireDateTime(hashKey, new RedisValue[] { "f1" });
        Assert.Equal(new[] { -1L }, result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLive()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" });
        Assert.NotNull(result);
        Assert.True(result.Length == 1);
        Assert.True(result[0] > 0);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(fieldsResult);
        Assert.True(fieldsResult.Length > 0);
        Assert.True(fieldsResult.All(x => x > 0));
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal(new long[] { -1, -1, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersist()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldPersist(hashKey, new RedisValue[] { "f1" });
        Assert.Equal(new[] { PersistResult.Success }, result);

        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal(new[] { PersistResult.Success, PersistResult.Success }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal(new[] { PersistResult.ConditionNotMet, PersistResult.ConditionNotMet }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal(new[] { PersistResult.NoSuchField, PersistResult.NoSuchField }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldPersist(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new[] { PersistResult.NoSuchField, PersistResult.NoSuchField }, fieldsResult);
    }
}
