using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands#hash"/>.
/// </summary>
[RunPerProtocol]
public class HashFieldTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private readonly DateTime nextCentury = new DateTime(2101, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeSpan oneYearInMs = TimeSpan.FromMilliseconds(31536000000);

    private readonly HashEntry[] entries = [new("f1", 1), new("f2", 2)];

    private readonly RedisValue[] fields = ["f1", "f2"];

    private readonly RedisValue[] values = [1, 2];

    [Fact]
    public void HashFieldExpire()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success,], fieldsResult);
    }

    [Fact]
    public void HashFieldExpireNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldExpire(hashKey, fields, oneYearInMs);
        Assert.Equal([ExpireResult.NoSuchField, ExpireResult.NoSuchField], fieldsResult);

        fieldsResult = db.HashFieldExpire(hashKey, fields, nextCentury);
        Assert.Equal([ExpireResult.NoSuchField, ExpireResult.NoSuchField], fieldsResult);
    }

    [Fact]
    public async Task HashFieldExpireAsync()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Equal([ExpireResult.Success, ExpireResult.Success], fieldsResult);
    }

    [Fact]
    public async Task HashFieldExpireAsyncNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, oneYearInMs);
        Assert.Equal([ExpireResult.NoSuchField, ExpireResult.NoSuchField], fieldsResult);

        fieldsResult = await db.HashFieldExpireAsync(hashKey, fields, nextCentury);
        Assert.Equal([ExpireResult.NoSuchField, ExpireResult.NoSuchField], fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeIsDue()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, ["f1"], new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal([ExpireResult.Due], result);
    }

    [Fact]
    public void HashFieldExpireNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldExpire(hashKey, ["nonExistingField"], oneYearInMs);
        Assert.Equal([ExpireResult.NoSuchField], result);
    }

    [Fact]
    public void HashFieldExpireConditionsSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, [new("f3", 3), new("f4", 4)]);
        var initialExpire = db.HashFieldExpire(hashKey, ["f2", "f3", "f4"], new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal([ExpireResult.Success, ExpireResult.Success, ExpireResult.Success], initialExpire);

        var result = db.HashFieldExpire(hashKey, ["f1"], oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal([ExpireResult.Success], result);

        result = db.HashFieldExpire(hashKey, ["f2"], oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal([ExpireResult.Success], result);

        result = db.HashFieldExpire(hashKey, ["f3"], nextCentury, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal([ExpireResult.Success], result);

        result = db.HashFieldExpire(hashKey, ["f4"], oneYearInMs, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal([ExpireResult.Success], result);
    }

    [Fact]
    public void HashFieldExpireConditionsNotSatisfied()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.KeyDelete(hashKey);
        db.HashSet(hashKey, entries);
        db.HashSet(hashKey, [new("f3", 3), new("f4", 4)]);
        var initialExpire = db.HashFieldExpire(hashKey, ["f2", "f3", "f4"], new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal([ExpireResult.Success, ExpireResult.Success, ExpireResult.Success], initialExpire);

        var result = db.HashFieldExpire(hashKey, ["f1"], oneYearInMs, ExpireWhen.HasExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        result = db.HashFieldExpire(hashKey, ["f2"], oneYearInMs, ExpireWhen.HasNoExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        result = db.HashFieldExpire(hashKey, ["f3"], nextCentury, ExpireWhen.LessThanCurrentExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);

        result = db.HashFieldExpire(hashKey, ["f4"], oneYearInMs, ExpireWhen.GreaterThanCurrentExpiry);
        Assert.Equal([ExpireResult.ConditionNotMet], result);
    }

    [Fact]
    public void HashFieldGetExpireDateTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, nextCentury);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetExpireDateTime(hashKey, ["f1"]);
        Assert.Equal([ms], result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal([ms, ms], fieldsResult);
    }

    [Fact]
    public void HashFieldExpireFieldNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var result = db.HashFieldGetExpireDateTime(hashKey, ["f1"]);
        Assert.Equal([-1L], result);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal([-1, -1,], fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, fields);
        Assert.Equal([-2, -2,], fieldsResult);
    }

    [Fact]
    public void HashFieldGetExpireDateTimeNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldGetExpireDateTime(hashKey, ["notExistingField1", "notExistingField2"]);
        Assert.Equal([-2, -2,], fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLive()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldGetTimeToLive(hashKey, ["f1"]);
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
        Assert.Equal([-1, -1,], fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal([-2, -2,], fieldsResult);
    }

    [Fact]
    public void HashFieldGetTimeToLiveNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldGetTimeToLive(hashKey, ["notExistingField1", "notExistingField2"]);
        Assert.Equal([-2, -2,], fieldsResult);
    }

    [Fact]
    public void HashFieldPersist()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);
        long ms = new DateTimeOffset(nextCentury).ToUnixTimeMilliseconds();

        var result = db.HashFieldPersist(hashKey, ["f1"]);
        Assert.Equal([PersistResult.Success], result);

        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal([PersistResult.Success, PersistResult.Success], fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoExpireTime()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal([PersistResult.ConditionNotMet, PersistResult.ConditionNotMet], fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoKey()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc2).GetDatabase();
        var hashKey = Me();

        var fieldsResult = db.HashFieldPersist(hashKey, fields);
        Assert.Equal([PersistResult.NoSuchField, PersistResult.NoSuchField], fieldsResult);
    }

    [Fact]
    public void HashFieldPersistNoField()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();
        var hashKey = Me();
        db.HashSet(hashKey, entries);
        db.HashFieldExpire(hashKey, fields, oneYearInMs);

        var fieldsResult = db.HashFieldPersist(hashKey, ["notExistingField1", "notExistingField2"]);
        Assert.Equal([PersistResult.NoSuchField, PersistResult.NoSuchField], fieldsResult);
    }

    [Fact]
    public void HashFieldGetAndSetExpiry()
    {
        using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // testing with timespan
        db.HashSet(hashKey, entries);
        var fieldResult = db.HashFieldGetAndSetExpiry(hashKey, "f1", TimeSpan.FromHours(1));
        Assert.Equal(1, fieldResult);
        var fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with datetime
        db.HashSet(hashKey, entries);
        fieldResult = db.HashFieldGetAndSetExpiry(hashKey, "f1", DateTime.Now.AddMinutes(120));
        Assert.Equal(1, fieldResult);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing persist
        fieldResult = db.HashFieldGetAndSetExpiry(hashKey, "f1", persist: true);
        Assert.Equal(1, fieldResult);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.Equal(-1, fieldTtl);

        // testing multiple fields with timespan
        db.HashSet(hashKey, entries);
        var fieldResults = db.HashFieldGetAndSetExpiry(hashKey, fields, TimeSpan.FromHours(1));
        Assert.Equal(values, fieldResults);
        var fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing multiple fields with datetime
        db.HashSet(hashKey, entries);
        fieldResults = db.HashFieldGetAndSetExpiry(hashKey, fields, DateTime.Now.AddMinutes(120));
        Assert.Equal(values, fieldResults);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with persist
        fieldResults = db.HashFieldGetAndSetExpiry(hashKey, fields, persist: true);
        Assert.Equal(values, fieldResults);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldTtls);
    }

    [Fact]
    public async Task HashFieldGetAndSetExpiryAsync()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // testing with timespan
        db.HashSet(hashKey, entries);
        var fieldResult = await db.HashFieldGetAndSetExpiryAsync(hashKey, "f1", TimeSpan.FromHours(1));
        Assert.Equal(1, fieldResult);
        var fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with datetime
        db.HashSet(hashKey, entries);
        fieldResult = await db.HashFieldGetAndSetExpiryAsync(hashKey, "f1", DateTime.Now.AddMinutes(120));
        Assert.Equal(1, fieldResult);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing persist
        fieldResult = await db.HashFieldGetAndSetExpiryAsync(hashKey, "f1", persist: true);
        Assert.Equal(1, fieldResult);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.Equal(-1, fieldTtl);

        // testing multiple fields with timespan
        db.HashSet(hashKey, entries);
        var fieldResults = await db.HashFieldGetAndSetExpiryAsync(hashKey, fields, TimeSpan.FromHours(1));
        Assert.Equal(values, fieldResults);
        var fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing multiple fields with datetime
        db.HashSet(hashKey, entries);
        fieldResults = await db.HashFieldGetAndSetExpiryAsync(hashKey, fields, DateTime.Now.AddMinutes(120));
        Assert.Equal(values, fieldResults);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with persist
        fieldResults = await db.HashFieldGetAndSetExpiryAsync(hashKey, fields, persist: true);
        Assert.Equal(values, fieldResults);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.Equal(new long[] { -1, -1 }, fieldTtls);
    }

    [Fact]
    public void HashFieldSetAndSetExpiry()
    {
        using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // testing with timespan
        var result = db.HashFieldSetAndSetExpiry(hashKey, "f1", 1, TimeSpan.FromHours(1));
        Assert.Equal(1, result);
        var fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with datetime
        result = db.HashFieldSetAndSetExpiry(hashKey, "f1", 1, DateTime.Now.AddMinutes(120));
        Assert.Equal(1, result);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing with keepttl
        result = db.HashFieldSetAndSetExpiry(hashKey, "f1", 1, keepTtl: true);
        Assert.Equal(1, result);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with timespan
        result = db.HashFieldSetAndSetExpiry(hashKey, entries, TimeSpan.FromHours(1));
        Assert.Equal(1, result);
        var fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing multiple fields with datetime
        result = db.HashFieldSetAndSetExpiry(hashKey, entries, DateTime.Now.AddMinutes(120));
        Assert.Equal(1, result);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with keepttl
        result = db.HashFieldSetAndSetExpiry(hashKey, entries, keepTtl: true);
        Assert.Equal(1, result);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing with ExpireWhen.Exists
        db.KeyDelete(hashKey);
        result = db.HashFieldSetAndSetExpiry(hashKey, "f1", 1, TimeSpan.FromHours(1), when: When.Exists);
        Assert.Equal(0, result); // should not set because it doesnt exist

        // testing with ExpireWhen.NotExists
        result = db.HashFieldSetAndSetExpiry(hashKey, "f1", 1, TimeSpan.FromHours(1), when: When.NotExists);
        Assert.Equal(1, result); // should set because it doesnt exist
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with ExpireWhen.GreaterThanCurrentExpiry
        result = db.HashFieldSetAndSetExpiry(hashKey, "f1", -1, keepTtl: true, when: When.Exists);
        Assert.Equal(1, result); // should set because it exists
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
    }

    [Fact]
    public async Task HashFieldSetAndSetExpiryAsync()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // testing with timespan
        var result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", 1, TimeSpan.FromHours(1));
        Assert.Equal(1, result);
        var fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with datetime
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", 1, DateTime.Now.AddMinutes(120));
        Assert.Equal(1, result);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing with keepttl
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", 1, keepTtl: true);
        Assert.Equal(1, result);
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with timespan
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, entries, TimeSpan.FromHours(1));
        Assert.Equal(1, result);
        var fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing multiple fields with datetime
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, entries, DateTime.Now.AddMinutes(120));
        Assert.Equal(1, result);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing multiple fields with keepttl
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, entries, keepTtl: true);
        Assert.Equal(1, result);
        fieldTtls = db.HashFieldGetTimeToLive(hashKey, fields);
        Assert.InRange(fieldTtls[0], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.InRange(fieldTtls[1], TimeSpan.FromMinutes(119).TotalMilliseconds, TimeSpan.FromHours(2).TotalMilliseconds);

        // testing with ExpireWhen.Exists
        db.KeyDelete(hashKey);
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", 1, TimeSpan.FromHours(1), when: When.Exists);
        Assert.Equal(0, result); // should not set because it doesnt exist

        // testing with ExpireWhen.NotExists
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", 1, TimeSpan.FromHours(1), when: When.NotExists);
        Assert.Equal(1, result); // should set because it doesnt exist
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);

        // testing with ExpireWhen.GreaterThanCurrentExpiry
        result = await db.HashFieldSetAndSetExpiryAsync(hashKey, "f1", -1, keepTtl: true, when: When.Exists);
        Assert.Equal(1, result); // should set because it exists
        fieldTtl = db.HashFieldGetTimeToLive(hashKey, new RedisValue[] { "f1" })[0];
        Assert.InRange(fieldTtl, TimeSpan.FromMinutes(59).TotalMilliseconds, TimeSpan.FromHours(1).TotalMilliseconds);
    }
    [Fact]
    public void HashFieldGetAndDelete()
    {
        using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // single field
        db.HashSet(hashKey, entries);
        var fieldResult = db.HashFieldGetAndDelete(hashKey, "f1");
        Assert.Equal(1, fieldResult);
        Assert.False(db.HashExists(hashKey, "f1"));

        // multiple fields
        db.HashSet(hashKey, entries);
        var fieldResults = db.HashFieldGetAndDelete(hashKey, fields);
        Assert.Equal(values, fieldResults);
        Assert.False(db.HashExists(hashKey, "f1"));
        Assert.False(db.HashExists(hashKey, "f2"));
    }

    [Fact]
    public async Task HashFieldGetAndDeleteAsync()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var hashKey = Me();

        // single field
        db.HashSet(hashKey, entries);
        var fieldResult = await db.HashFieldGetAndDeleteAsync(hashKey, "f1");
        Assert.Equal(1, fieldResult);
        Assert.False(db.HashExists(hashKey, "f1"));

        // multiple fields
        db.HashSet(hashKey, entries);
        var fieldResults = await db.HashFieldGetAndDeleteAsync(hashKey, fields);
        Assert.Equal(values, fieldResults);
        Assert.False(db.HashExists(hashKey, "f1"));
        Assert.False(db.HashExists(hashKey, "f2"));
    }
}
