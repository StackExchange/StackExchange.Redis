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
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, "f1", oneYearInMs);
        Assert.Equal(ExpireResult.Success, result);

        result = db.HashFieldExpire(hashKey, "f1", nextCentury);
        Assert.Equal(ExpireResult.Success, result);

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, }, fieldsResult);
    }

    [Fact]
    public void HashFieldExpireNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var result = db.HashFieldExpire(hashKey, "f1", oneYearInMs);
        Assert.Null(result);

        result = db.HashFieldExpire(hashKey, "f1", nextCentury);
        Assert.Null(result);

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Null(fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public async void HashFieldExpireAsync()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = await db.HashFieldExpireAsync(hashKey, "f1", oneYearInMs);
        Assert.Equal(ExpireResult.Success, result);

        result = await db.HashFieldExpireAsync(hashKey, "f1", nextCentury);
        Assert.Equal(ExpireResult.Success, result);

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success }, fieldsResult);
    }

    [Fact]
    public async void HashFieldExpireAsyncNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var result = await db.HashFieldExpireAsync(hashKey, "f1", oneYearInMs);
        Assert.Null(result);

        result = await db.HashFieldExpireAsync(hashKey, "f1", nextCentury);
        Assert.Null(result);

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Null(fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public void HashFieldExpireTimeIsDue()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, "f1", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ExpireResult.Due, result);
    }

    [Fact]
    public void HashFieldExpireNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, "nonExistingField", oneYearInMs);
        Assert.Equal(ExpireResult.NoSuchField, result);
    }

    [Fact]
    public void HashFieldExpireConditionsSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, new HashEntry[] { new("f3", 3), new("f4", 4) });
        var initialExpire = db.HashFieldExpire(hashKey, new RedisValue[] { "f2", "f3", "f4" }, new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, ExpireResult.Success }, initialExpire);

        var result = db.HashFieldExpire(hashKey, "f1", oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(ExpireResult.Success, result);

        result = db.HashFieldExpire(hashKey, "f2", oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal(ExpireResult.Success, result);

        result = db.HashFieldExpire(hashKey, "f3", nextCentury, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal(ExpireResult.Success, result);

        result = db.HashFieldExpire(hashKey, "f4", oneYearInMs, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal(ExpireResult.Success, result);
    }

    [Fact]
    public void HashFieldExpireConditionsNotSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, new HashEntry[] { new("f3", 3), new("f4", 4) });
        var initialExpire = db.HashFieldExpire(hashKey, new RedisValue[] { "f2", "f3", "f4" }, new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.Success, ExpireResult.Success }, initialExpire);

        var result = db.HashFieldExpire(hashKey, "f1", oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal(ExpireResult.ConditionNotMet, result);

        result = db.HashFieldExpire(hashKey, "f2", oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(ExpireResult.ConditionNotMet, result);

        result = db.HashFieldExpire(hashKey, "f3", nextCentury, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal(ExpireResult.ConditionNotMet, result);

        result = db.HashFieldExpire(hashKey, "f4", oneYearInMs, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal(ExpireResult.ConditionNotMet, result);
    }
}
