using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

#pragma warning disable SER002 // 8.4

public class DigestIntegrationTests(ITestOutputHelper output, SharedConnectionFixture fixture)
    : TestBase(output, fixture)
{
    [Fact]
    public async Task ReadDigest()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        byte[] blob = new byte[1024];
        new Random().NextBytes(blob);
        var local = ValueCondition.CalculateDigest(blob);
        Assert.Equal(ValueCondition.ConditionKind.DigestEquals, local.Kind);
        Assert.Equal(RedisValue.StorageType.Int64, local.Value.Type);
        Log("Local digest: " + local);

        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);

        // test without a value
        var digest = await db.StringDigestAsync(key);
        Assert.Null(digest);

        // test with a value
        await db.StringSetAsync(key, blob, flags: CommandFlags.FireAndForget);
        digest = await db.StringDigestAsync(key);
        Assert.NotNull(digest);
        Assert.Equal(ValueCondition.ConditionKind.DigestEquals, digest.Value.Kind);
        Assert.Equal(RedisValue.StorageType.Int64, digest.Value.Value.Type);
        Log("Server digest: " + digest);
        Assert.Equal(local, digest.Value);
    }

    [Theory]
    [InlineData(null, (int)ValueCondition.ConditionKind.NotExists)]
    [InlineData("new value", (int)ValueCondition.ConditionKind.NotExists)]
    [InlineData(null, (int)ValueCondition.ConditionKind.ValueEquals)]
    [InlineData(null, (int)ValueCondition.ConditionKind.DigestEquals)]
    public async Task InvalidConditionalDelete(string? testValue, int rawKind)
    {
        await using var conn = Create(); // no server requirement, since fails locally
        var key = Me();
        var db = conn.GetDatabase();
        var condition = CreateCondition(testValue, rawKind);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await db.StringDeleteAsync(key, when: condition);
        });
        Assert.StartsWith("StringDeleteAsync cannot be used with a NotExists condition.", ex.Message);
    }

    [Theory]
    [InlineData(null, null, (int)ValueCondition.ConditionKind.Always)]
    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.Always)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.Always, true)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.Always, true)]

    [InlineData(null, null, (int)ValueCondition.ConditionKind.Exists)]
    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.Exists)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.Exists, true)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.Exists, true)]

    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.DigestEquals)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.DigestEquals)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.DigestEquals, true)]

    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.ValueEquals)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.ValueEquals)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.ValueEquals, true)]

    [InlineData(null, null, (int)ValueCondition.ConditionKind.DigestNotEquals)]
    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.DigestNotEquals)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.DigestNotEquals, true)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.DigestNotEquals)]

    [InlineData(null, null, (int)ValueCondition.ConditionKind.ValueNotEquals)]
    [InlineData(null, "new value", (int)ValueCondition.ConditionKind.ValueNotEquals)]
    [InlineData("old value", "new value", (int)ValueCondition.ConditionKind.ValueNotEquals, true)]
    [InlineData("new value", "new value", (int)ValueCondition.ConditionKind.ValueNotEquals)]
    public async Task ConditionalDelete(string? dbValue, string? testValue, int rawKind, bool expectDelete = false)
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);
        if (dbValue != null) await db.StringSetAsync(key, dbValue, flags: CommandFlags.FireAndForget);

        var condition = CreateCondition(testValue, rawKind);

        var pendingDelete = db.StringDeleteAsync(key, when: condition);
        var exists = await db.KeyExistsAsync(key);
        var deleted = await pendingDelete;

        if (dbValue is null)
        {
            // didn't exist to be deleted
            Assert.False(expectDelete);
            Assert.False(exists);
            Assert.False(deleted);
        }
        else
        {
            Assert.Equal(expectDelete, deleted);
            Assert.Equal(!expectDelete, exists);
        }
    }

    private ValueCondition CreateCondition(string? testValue, int rawKind)
    {
        var condition = (ValueCondition.ConditionKind)rawKind switch
        {
            ValueCondition.ConditionKind.Always => ValueCondition.Always,
            ValueCondition.ConditionKind.Exists => ValueCondition.Exists,
            ValueCondition.ConditionKind.NotExists => ValueCondition.NotExists,
            ValueCondition.ConditionKind.ValueEquals => ValueCondition.Equal(testValue),
            ValueCondition.ConditionKind.ValueNotEquals => ValueCondition.NotEqual(testValue),
            ValueCondition.ConditionKind.DigestEquals => ValueCondition.DigestEqual(testValue),
            ValueCondition.ConditionKind.DigestNotEquals => ValueCondition.DigestNotEqual(testValue),
            _ => throw new ArgumentOutOfRangeException(nameof(rawKind)),
        };
        Log($"Condition: {condition}");
        return condition;
    }
}
