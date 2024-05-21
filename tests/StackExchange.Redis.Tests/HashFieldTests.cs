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
    public void HashFieldGetExpireDateTimeIsDue()
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

    [Fact]
    public void HashFieldGetExpireDateTime()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, nextCentury);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetExpireDateTime(hashKey, "f1");
        Assert.Equal(ms, result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new[] { ms, ms }, fieldsResult);
    }

    [Fact]
    public void HashFieldExpireFieldNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldGetExpireDateTime(hashKey, "f1");
        Assert.Equal(-1, result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var result = db.HashFieldGetExpireDateTime(hashKey, "f1");
        Assert.Null(result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var result = db.HashFieldGetExpireDateTime(hashKey, "notExistingField1");
        Assert.Equal(-2, result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLive()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetTimeToLive(hashKey, "f1");
        Assert.NotNull(result);
        Assert.True(result > 0);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(fieldsResult);
        Assert.True(fieldsResult.Length > 0);
        Assert.True(fieldsResult.All(x => x > 0));
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldGetTimeToLive(hashKey, "f1");
        Assert.Equal(-1, result);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal(new long[] { -1, -1, }, fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var result = db.HashFieldGetTimeToLive(hashKey, "f1");
        Assert.Null(result);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var result = db.HashFieldGetTimeToLive(hashKey, "notExistingField1");
        Assert.Equal(-2, result);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new long[] { -2, -2, }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersist()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldPersist(hashKey, "f1");
        Assert.Equal(PersistResult.Success, result);

        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal(new[] { PersistResult.Success, PersistResult.Success }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldPersist(hashKey, "f1");
        Assert.Equal(PersistResult.ConditionNotMet, result);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal(new[] { PersistResult.ConditionNotMet, PersistResult.ConditionNotMet }, fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var result = db.HashFieldPersist(hashKey, "f1");
        Assert.Null(result);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var result = db.HashFieldPersist(hashKey, "notExistingField1");
        Assert.Equal(PersistResult.NoSuchField, result);

        var fieldsResult = db.HashFieldPersist(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" });
        Assert.Equal(new[] { PersistResult.NoSuchField, PersistResult.NoSuchField }, fieldsResult);
    }

    [Fact]
    public void HashFieldGet()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, oneYearInMs);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var ttlResults = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(ttlResults);
        Assert.True(ttlResults.Length > 0);
        Assert.True(ttlResults.All(x => x > 0));

        fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, nextCentury);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var expireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();
        Assert.Equal(new[] { ms, ms }, expireDates);


        fieldsResult = db.HashGetAndPersistFields(hashKey, fields);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var fieldsNoExpireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldsNoExpireDates);
    }

    [Fact]
    public void HashFieldGetWithExpireConditions()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var ttlResults = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(ttlResults);
        Assert.True(ttlResults.Length > 0);
        Assert.True(ttlResults.All(x => x > 0));

        fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, nextCentury, ExpireWhen.HasNoExpiry);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var expireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();
        Assert.NotEqual(new[] { ms, ms }, expireDates);


        fieldsResult = db.HashGetAndPersistFields(hashKey, fields);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var fieldsNoExpireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldsNoExpireDates);
    }

    [Fact]
    public void HashFieldGetNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, oneYearInMs);
        Assert.Null(fieldsResult);

        fieldsResult = db.HashGetAndSetExpiry(hashKey, fields, nextCentury);
        Assert.Null(fieldsResult);
    }

    [Fact]
    public void HashFieldGetNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashGetAndSetExpiry(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" }, oneYearInMs);
        Assert.NotNull(fieldsResult);
        Assert.Equal(new RedisValue[] { RedisValue.Null, RedisValue.Null }, fieldsResult);

        fieldsResult = db.HashGetAndSetExpiry(hashKey, new RedisValue[] { "notExistingField1", "notExistingField2" }, nextCentury);
        Assert.NotNull(fieldsResult);
        Assert.Equal(new RedisValue[] { RedisValue.Null, RedisValue.Null }, fieldsResult);
    }

    [Fact]
    public void HashFieldSet()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashSetAndSetExpiry(hashKey, new[] { new HashEntry("f1", 1), new HashEntry("f2", 2) }, oneYearInMs);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var ttlResults = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(ttlResults);
        Assert.True(ttlResults.Length > 0);
        Assert.True(ttlResults.All(x => x > 0));

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, nextCentury);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var expireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();
        Assert.Equal(new[] { ms, ms }, expireDates);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, false);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var fieldsNoExpireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldsNoExpireDates);
    }

    [Fact]
    public void HashFieldSetNoKey()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, oneYearInMs);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, nextCentury);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);
    }

    [Fact]
    public void HashFieldSetNoField()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashSetAndSetExpiry(hashKey, new[] { new HashEntry("notExistingField1", 1), new HashEntry("notExistingField2", 2) }, oneYearInMs);
        Assert.NotNull(fieldsResult);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, new[] { new HashEntry("notExistingField1", 1), new HashEntry("notExistingField2", 2) }, nextCentury);
        Assert.NotNull(fieldsResult);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);
    }

    [Fact]
    public void HashFieldSetWithExpireConditions()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var ttlResults = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(ttlResults);
        Assert.True(ttlResults.Length > 0);
        Assert.True(ttlResults.All(x => x > 0));

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, nextCentury, ExpireWhen.HasNoExpiry);
        Assert.Equal(new RedisValue[] { 1, 1, }, fieldsResult);

        var expireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();
        Assert.NotEqual(new[] { ms, ms }, expireDates);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, false);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var fieldsNoExpireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldsNoExpireDates);
    }

    [Fact]
    public void HashFieldSetWithFlags()
    {
        var db = Create(require: RedisFeatures.v7_2_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);

        var fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, oneYearInMs, ExpireWhen.HasNoExpiry, HashFieldFlags.DontCreate);
        Assert.Null(fieldsResult);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, oneYearInMs, ExpireWhen.HasNoExpiry, HashFieldFlags.DontCreateFields);
        Assert.Null(fieldsResult);

        fieldsResult = db.HashSetAndSetExpiry(hashKey, entries, oneYearInMs, ExpireWhen.HasNoExpiry, HashFieldFlags.DontOverwriteFields);
        Assert.Equal(new RedisValue[] { 3, 3 }, fieldsResult);

        var ttlResults = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.NotNull(ttlResults);
        Assert.True(ttlResults.Length > 0);
        Assert.True(ttlResults.All(x => x > 0));

        fieldsResult = db.HashSetAndSetExpiry(hashKey, new HashEntry[] { new("f1", "a"), new("f2", "b") }, nextCentury, ExpireWhen.HasNoExpiry, HashFieldFlags.GetOld);
        Assert.Equal(entries.Select(i => i.Value).ToArray(), fieldsResult);

        var expireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();
        Assert.NotEqual(new[] { ms, ms }, expireDates);


        fieldsResult = db.HashSetAndSetExpiry(hashKey, new HashEntry[] { new("f1", "x"), new("f2", "y") }, false, HashFieldFlags.GetNew);
        Assert.Equal(new RedisValue[] { "x", "y" }, fieldsResult);

        var fieldsNoExpireDates = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldsNoExpireDates);
    }
}
