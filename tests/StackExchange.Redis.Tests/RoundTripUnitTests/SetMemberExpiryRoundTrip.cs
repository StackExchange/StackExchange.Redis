using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Exact-bytes tests for the Redis 8.12 set-member expiration family:
/// <c>SADDEX</c>, <c>SEXPIRE</c>/<c>SPEXPIRE</c>/<c>SEXPIREAT</c>/<c>SPEXPIREAT</c>,
/// <c>SPTTL</c>, <c>SPEXPIRETIME</c> and <c>SPERSIST</c>.
/// </summary>
public class SetMemberExpiryRoundTrip(ITestOutputHelper log)
{
    // 2101-01-01T00:00:00Z
    private static readonly DateTime NextCentury = new(2101, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long NextCenturyUnixSeconds = 4133980800;

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_Ex()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", "m", Expiration.CreateOrKeepTtl(TimeSpan.FromSeconds(15), keepTtl: false), When.Always, CommandFlags.None);
        const string RequestResp = "*7\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$2\r\nEX\r\n$2\r\n15\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_Px()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", "m", Expiration.CreateOrKeepTtl(TimeSpan.FromMilliseconds(1500), keepTtl: false), When.Always, CommandFlags.None);
        const string RequestResp = "*7\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$2\r\nPX\r\n$4\r\n1500\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_KeepTtl()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", "m", Expiration.CreateOrKeepTtl((TimeSpan?)null, keepTtl: true), When.Always, CommandFlags.None);
        const string RequestResp = "*6\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$7\r\nKEEPTTL\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_Bare_DiscardsTtl()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", "m", Expiration.CreateOrKeepTtl((TimeSpan?)null, keepTtl: false), When.Always, CommandFlags.None);
        const string RequestResp = "*5\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":0\r\n", log: log);
        Assert.False(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_ExAt()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", "m", new Expiration(NextCentury), When.Always, CommandFlags.None);
        const string RequestResp = "*7\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$4\r\nEXAT\r\n$10\r\n4133980800\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_Mnx()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", new RedisValue[] { "a", "b" }, Expiration.CreateOrKeepTtl(TimeSpan.FromSeconds(15), keepTtl: false), When.NotExists, CommandFlags.None);
        const string RequestResp = "*9\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$3\r\nMNX\r\n$2\r\nEX\r\n$2\r\n15\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n";

        // one existing member vetoes the whole batch: reply 0, nothing added
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":0\r\n", log: log);
        Assert.False(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_Mxx()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", new RedisValue[] { "a", "b" }, new Expiration(NextCentury), When.Exists, CommandFlags.None);
        const string RequestResp = "*9\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$3\r\nMXX\r\n$4\r\nEXAT\r\n$10\r\n4133980800\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetAddAndSetExpiry_SingleElementArray_SameAsSingle()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.SetAddAndSetExpiryMessage("k", new RedisValue[] { "m" }, Expiration.CreateOrKeepTtl(TimeSpan.FromSeconds(15), keepTtl: false), When.Always, CommandFlags.None);
        const string RequestResp = "*7\r\n$6\r\nSADDEX\r\n$1\r\nk\r\n$2\r\nEX\r\n$2\r\n15\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\nm\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, RequestResp, ":1\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberExpire_SecondsPrecision()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberExpireMessage("k", 90_000, ExpireWhen.Always, RedisDatabase.PickSetMemberExpireCommandByPrecision, CommandFlags.None, "a", "b");
        const string RequestResp = "*7\r\n$7\r\nSEXPIRE\r\n$1\r\nk\r\n$2\r\n90\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.ExpireResultArray, RequestResp, "*2\r\n:1\r\n:-2\r\n", log: log);
        Assert.Equal(new[] { ExpireResult.Success, ExpireResult.NoSuchField }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberExpire_MillisecondsPrecision()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberExpireMessage("k", 1500, ExpireWhen.Always, RedisDatabase.PickSetMemberExpireCommandByPrecision, CommandFlags.None, "a");
        const string RequestResp = "*6\r\n$8\r\nSPEXPIRE\r\n$1\r\nk\r\n$4\r\n1500\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\na\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.ExpireResultArray, RequestResp, "*1\r\n:1\r\n", log: log);
        Assert.Equal(new[] { ExpireResult.Success }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberExpire_Gt()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberExpireMessage("k", 90_000, ExpireWhen.GreaterThanCurrentExpiry, RedisDatabase.PickSetMemberExpireCommandByPrecision, CommandFlags.None, "a");
        const string RequestResp = "*7\r\n$7\r\nSEXPIRE\r\n$1\r\nk\r\n$2\r\n90\r\n$2\r\nGT\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\na\r\n";

        // GT loses to a member with no expiry (no expiry counts as infinite)
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.ExpireResultArray, RequestResp, "*1\r\n:0\r\n", log: log);
        Assert.Equal(new[] { ExpireResult.ConditionNotMet }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberExpireAt_SecondsPrecision()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberExpireMessage("k", NextCenturyUnixSeconds * 1000, ExpireWhen.Always, RedisDatabase.PickSetMemberExpireAtCommandByPrecision, CommandFlags.None, "a");
        const string RequestResp = "*6\r\n$9\r\nSEXPIREAT\r\n$1\r\nk\r\n$10\r\n4133980800\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\na\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.ExpireResultArray, RequestResp, "*1\r\n:1\r\n", log: log);
        Assert.Equal(new[] { ExpireResult.Success }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberExpireAt_MillisecondsPrecision()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberExpireMessage("k", (NextCenturyUnixSeconds * 1000) + 500, ExpireWhen.Always, RedisDatabase.PickSetMemberExpireAtCommandByPrecision, CommandFlags.None, "a");
        const string RequestResp = "*6\r\n$10\r\nSPEXPIREAT\r\n$1\r\nk\r\n$13\r\n4133980800500\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\na\r\n";

        // an expiration already in the past deletes the member: reply 2
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.ExpireResultArray, RequestResp, "*1\r\n:2\r\n", log: log);
        Assert.Equal(new[] { ExpireResult.Due }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberGetTimeToLive_SendsMembersBlock()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberMessage(RedisCommand.SPTTL, "k", CommandFlags.None, "a", "b");
        const string RequestResp = "*6\r\n$5\r\nSPTTL\r\n$1\r\nk\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, RequestResp, "*2\r\n:9500\r\n:-1\r\n", log: log);
        Assert.Equal(new long[] { 9500, -1 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberGetExpireDateTime_SendsMembersBlock()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberMessage(RedisCommand.SPEXPIRETIME, "k", CommandFlags.None, "a");
        const string RequestResp = "*5\r\n$12\r\nSPEXPIRETIME\r\n$1\r\nk\r\n$7\r\nMEMBERS\r\n$1\r\n1\r\n$1\r\na\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, RequestResp, "*1\r\n:4133980800000\r\n", log: log);
        Assert.Equal(new[] { NextCenturyUnixSeconds * 1000 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SetMemberPersist_SendsMembersBlock()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetSetMemberMessage(RedisCommand.SPERSIST, "k", CommandFlags.None, "a", "b");
        const string RequestResp = "*6\r\n$8\r\nSPERSIST\r\n$1\r\nk\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.PersistResultArray, RequestResp, "*2\r\n:1\r\n:-2\r\n", log: log);
        Assert.Equal(new[] { PersistResult.Success, PersistResult.NoSuchField }, result);
    }
}
