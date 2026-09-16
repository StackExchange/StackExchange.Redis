using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Covers the commands whose retry category depends on their *arguments* rather than just the command name
/// (see https://github.com/StackExchange/StackExchange.Redis/issues/3148). These call the message factories
/// directly against the in-process server, so nothing here needs a real Redis.
/// </summary>
public class CommandRetryCategoryUnitTests(ITestOutputHelper log)
{
    private const CommandFlags ReadOnly = CommandFlags.CommandRetryReadOnly,
                               Checked = CommandFlags.CommandRetryWriteChecked,
                               LastWins = CommandFlags.CommandRetryWriteLastWins,
                               Accumulating = CommandFlags.CommandRetryWriteAccumulating,
                               Never = CommandFlags.CommandRetryNever;

    /// <summary>A caller-supplied category that is deliberately absurd for every command tested here.</summary>
    private const CommandFlags CallerOverride = CommandFlags.CommandRetryAlways;

    private async Task<RedisDatabase> GetDatabaseAsync()
    {
        var server = new InProcessTestServer(log);
        var conn = await server.ConnectAsync();
        return (RedisDatabase)conn.GetDatabase(0);
    }

    private void AssertCategory(CommandFlags expected, Message message, string because)
    {
        var actual = Message.GetRetryCategory(message.Flags);
        log.WriteLine("{0}: {1} (expected {2}) - {3}", message.CommandAndKey, actual, expected, because);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task StringSet_CategoryFollowsCondition()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        RedisValue val = "v";

        // the plain form is an unconditional overwrite
        AssertCategory(LastWins, db.GetStringSetMessage(key, val, Expiration.Default, When.Always, CommandFlags.None), "SET");
        AssertCategory(LastWins, db.GetStringSetMessage(key, val, TimeSpan.FromMinutes(5), When.Always, CommandFlags.None), "SETEX");

        // ...but NX/XX make it conditional, whichever spelling we emit
        AssertCategory(Checked, db.GetStringSetMessage(key, val, Expiration.Default, When.NotExists, CommandFlags.None), "SETNX");
        AssertCategory(Checked, db.GetStringSetMessage(key, val, Expiration.Default, When.Exists, CommandFlags.None), "SET XX");
        AssertCategory(Checked, db.GetStringSetMessage(key, val, TimeSpan.FromMinutes(5), When.NotExists, CommandFlags.None), "SET EX NX");
        AssertCategory(Checked, db.GetStringSetMessage(key, val, TimeSpan.FromMinutes(5), When.Exists, CommandFlags.None), "SET EX XX");

        // ...as does a compare-and-set; this is the case named in #3148
        AssertCategory(Checked, db.GetStringSetMessage(key, val, Expiration.Default, ValueCondition.Equal("old"), CommandFlags.None), "SET IFEQ");
        AssertCategory(Checked, db.GetStringSetMessage(key, val, Expiration.Default, ValueCondition.NotEqual("old"), CommandFlags.None), "SET IFNE");
        AssertCategory(Checked, db.GetStringSetMessage(key, val, Expiration.Default, ValueCondition.DigestEqual("old"), CommandFlags.None), "SET IFDEQ");
    }

    [Fact]
    public async Task StringSet_CallerCategoryWins()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        RedisValue val = "v";

        // the whole point of the first-wins rule: it is ultimately the caller's data
        AssertCategory(CallerOverride, db.GetStringSetMessage(key, val, Expiration.Default, When.Always, CallerOverride), "SET, caller override");
        AssertCategory(CallerOverride, db.GetStringSetMessage(key, val, Expiration.Default, When.Exists, CallerOverride), "SET XX, caller override");
        AssertCategory(CallerOverride, db.GetStringSetMessage(key, val, Expiration.Default, ValueCondition.Equal("old"), CallerOverride), "SET IFEQ, caller override");
    }

    [Fact]
    public async Task Sort_StoreIsAWriteNotARead()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";

        // a bare SORT is a read...
        AssertCategory(ReadOnly, db.GetSortMessage(default, key, 0, -1, Order.Ascending, SortType.Numeric, default, null, CommandFlags.None, out _), "SORT");
        AssertCategory(ReadOnly, db.GetSortMessage(default, key, 5, 10, Order.Descending, SortType.Alphabetic, "by_*", null, CommandFlags.None, out _), "SORT BY LIMIT");

        // ...but the STORE variant writes the destination key, and was previously mis-categorized as a read
        AssertCategory(LastWins, db.GetSortMessage("dest", key, 0, -1, Order.Ascending, SortType.Numeric, default, null, CommandFlags.None, out _), "SORT STORE");
        AssertCategory(CallerOverride, db.GetSortMessage("dest", key, 0, -1, Order.Ascending, SortType.Numeric, default, null, CallerOverride, out _), "SORT STORE, caller override");
    }

    [Fact]
    public async Task SortedSetAdd_IncrementAccumulates()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        RedisValue member = "m";

        // plain ZADD overwrites the score
        AssertCategory(LastWins, db.GetSortedSetAddMessage(key, member, 1.0, SortedSetWhen.Always, change: false, CommandFlags.None), "ZADD");

        // NX/XX are conditional; GT/LT are monotone, so re-applying converges
        AssertCategory(Checked, db.GetSortedSetAddMessage(key, member, 1.0, SortedSetWhen.NotExists, change: false, CommandFlags.None), "ZADD NX");
        AssertCategory(Checked, db.GetSortedSetAddMessage(key, member, 1.0, SortedSetWhen.Exists, change: false, CommandFlags.None), "ZADD XX");
        AssertCategory(Checked, db.GetSortedSetAddMessage(key, member, 1.0, SortedSetWhen.GreaterThan, change: false, CommandFlags.None), "ZADD GT");
        AssertCategory(Checked, db.GetSortedSetAddMessage(key, member, 1.0, SortedSetWhen.LessThan, change: false, CommandFlags.None), "ZADD LT");

        // ZINCRBY compounds, and so does the ZADD ... INCR form it degrades to under XX - which previously
        // inherited ZADD's "last wins" and was therefore retried by the default policy
        AssertCategory(Accumulating, db.GetSortedSetIncrementMessage(key, member, 1.0, ValueCondition.Always, CommandFlags.None), "ZINCRBY");
        AssertCategory(Accumulating, db.GetSortedSetIncrementMessage(key, member, 1.0, ValueCondition.Exists, CommandFlags.None), "ZADD XX INCR");

        // ...except under NX, where a replay can only find the member present and no-op
        AssertCategory(Checked, db.GetSortedSetIncrementMessage(key, member, 1.0, ValueCondition.NotExists, CommandFlags.None), "ZADD NX INCR");
    }


    private static StreamAddOptions Options(RedisValue messageId, in StreamIdempotentId idempotentId) =>
        new() { MessageId = messageId, IdempotentId = idempotentId };

    [Fact]
    public async Task StreamAdd_ExplicitAndIdempotentIdsAreReplaySafe()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        var pair = new NameValueEntry("f", "v");
        var noId = default(StreamIdempotentId);

        // "*" lets the server pick the id, so a replay appends a second entry
        AssertCategory(
            Accumulating,
            db.GetStreamAddMessage(key, Options("*", in noId), pair, CommandFlags.None),
            "XADD *");

        // an explicit id is rejected second time round ("equal or smaller")
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, Options("5-5", in noId), pair, CommandFlags.None),
            "XADD with explicit id");

        // IDMP producer id: the server deduplicates
        var idmp = new StreamIdempotentId("producer", "item-1");
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, Options("*", in idmp), pair, CommandFlags.None),
            "XADD IDMP");

        // IDMPAUTO producer: same, with the id derived from the entry content
        var idmpAuto = new StreamIdempotentId("producer");
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, Options("*", in idmpAuto), pair, CommandFlags.None),
            "XADD IDMPAUTO");
    }

    /// <summary>
    /// The discriminating case for the "explicit id" rule: <c>&lt;ms&gt;-*</c> is only *partly* explicit - the server
    /// still picks the sequence, so a replay appends 5-1 after 5-0 instead of being rejected. Testing the id against
    /// the bare "*" alone reads it as explicit, which would let a double-append through under the default policy
    /// (Checked is retried; Accumulating is not).
    /// </summary>
    [Fact]
    public async Task StreamAdd_PartialAutoIdStillAccumulates()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        var pair = new NameValueEntry("f", "v");
        var noId = default(StreamIdempotentId);

        Message Add(RedisValue id) => db.GetStreamAddMessage(key, Options(id, in noId), pair, CommandFlags.None);

        // anything the server completes accumulates...
        AssertCategory(Accumulating, Add("*"), "XADD *");
        AssertCategory(Accumulating, Add("5-*"), "XADD <ms>-* (server picks the sequence)");
        AssertCategory(Accumulating, Add("1526919030474-*"), "XADD <ms>-* (realistic ms)");

        // ...and only a *fully* specified id cannot be appended twice
        AssertCategory(Checked, Add("5-5"), "XADD with a fully explicit id");
        AssertCategory(Checked, Add("1526919030474-0"), "XADD with a fully explicit id (realistic ms)");
    }

    /// <summary>
    /// XREADGROUP is demoted to a read when every position is an explicit id (re-reading this consumer's own PEL),
    /// but CLAIM is emitted regardless of the position and takes entries from *other* consumers - an ownership
    /// mutation, and the same delivery-count bump that keeps XCLAIM off the read rung. So CLAIM must suppress the
    /// demotion; without that, `position: "0-0", claimMinIdleTime: 30s` reads as a pure read.
    /// </summary>
    [Fact]
    public async Task StreamReadGroup_ClaimSuppressesTheDemotion()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        var idle = TimeSpan.FromSeconds(30);

        Message Single(RedisValue position, TimeSpan? claim) =>
            db.GetStreamReadGroupMessage(key, "g", "c", position, count: null, noAck: false, claimMinIdleTime: claim, CommandFlags.None);

        // ">" consumes undelivered entries and advances the group cursor: never retry
        AssertCategory(Never, Single(">", null), "XREADGROUP >");

        // an explicit id re-reads our own pending list
        AssertCategory(ReadOnly, Single("0-0", null), "XREADGROUP with explicit id");

        // ...unless CLAIM is also asked for
        AssertCategory(Never, Single("0-0", idle), "XREADGROUP with explicit id + CLAIM");
        AssertCategory(Never, Single(">", idle), "XREADGROUP > + CLAIM");

        // and the same for the multi-stream form
        Message Multi(RedisValue[] positions, TimeSpan? claim) => new RedisDatabase.MultiStreamReadGroupCommandMessage(
            0,
            CommandFlags.None,
            Array.ConvertAll(positions, p => new StreamPosition(key, p)),
            "g",
            "c",
            countPerStream: null,
            noAck: false,
            claimMinIdleTime: claim);

        AssertCategory(ReadOnly, Multi(["0-0", "0-0"], null), "XREADGROUP multi, all explicit");
        AssertCategory(Never, Multi([">", "0-0"], null), "XREADGROUP multi, one \">\" anywhere");
        AssertCategory(Never, Multi(["0-0", "0-0"], idle), "XREADGROUP multi, all explicit + CLAIM");
    }

    /// <summary>
    /// The hash-field TTL commands take the same NX/XX/GT/LT conditions as the key-level ones, so they get the same
    /// rule; without this, HEXPIRE ... NX is categorized as a blind overwrite while EXPIRE ... NX is not.
    /// </summary>
    [Fact]
    public async Task Expire_CategoryFollowsCondition()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        var ttl = TimeSpan.FromMinutes(5);
        var deadline = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // a bare EXPIRE/EXPIREAT is an unconditional overwrite of the TTL
        AssertCategory(LastWins, db.GetExpiryMessage(key, CommandFlags.None, ttl, ExpireWhen.Always, out _), "EXPIRE");
        AssertCategory(LastWins, db.GetExpiryMessage(key, CommandFlags.None, deadline, ExpireWhen.Always, out _), "EXPIREAT");

        // NX/XX are conditional; GT/LT are monotone, so re-applying converges on the same deadline
        foreach (var when in new[] { ExpireWhen.HasNoExpiry, ExpireWhen.HasExpiry, ExpireWhen.GreaterThanCurrentExpiry, ExpireWhen.LessThanCurrentExpiry })
        {
            AssertCategory(Checked, db.GetExpiryMessage(key, CommandFlags.None, ttl, when, out _), $"EXPIRE {when.ToLiteral()}");
            AssertCategory(Checked, db.GetExpiryMessage(key, CommandFlags.None, deadline, when, out _), $"EXPIREAT {when.ToLiteral()}");
        }

        // and the hash-field forms follow the same rule
        static RedisCommand PickHashExpire(bool useSeconds) => useSeconds ? RedisCommand.HEXPIRE : RedisCommand.HPEXPIRE;
        long ms = (long)ttl.TotalMilliseconds;

        AssertCategory(LastWins, db.GetHashFieldExpireMessage(key, ms, ExpireWhen.Always, PickHashExpire, CommandFlags.None, "f"), "HEXPIRE");
        foreach (var when in new[] { ExpireWhen.HasNoExpiry, ExpireWhen.HasExpiry, ExpireWhen.GreaterThanCurrentExpiry, ExpireWhen.LessThanCurrentExpiry })
        {
            AssertCategory(Checked, db.GetHashFieldExpireMessage(key, ms, when, PickHashExpire, CommandFlags.None, "f"), $"HEXPIRE {when.ToLiteral()}");
        }
    }

    /// <summary>
    /// GETEX/HGETEX read like a GET until any of EX/PX/EXAT/PXAT/PERSIST is supplied, at which point they mutate
    /// the TTL. The bare form is the interesting control: it must stay a read.
    /// </summary>
    [Fact]
    public async Task GetEx_TtlOptionsMakeItAWrite()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";

        AssertCategory(ReadOnly, db.GetStringGetExMessage(key, Expiration.Default), "GETEX");
        AssertCategory(LastWins, db.GetStringGetExMessage(key, TimeSpan.FromMinutes(5)), "GETEX EX");
        AssertCategory(LastWins, db.GetStringGetExMessage(key, Expiration.Persist), "GETEX PERSIST");

        AssertCategory(ReadOnly, db.HashFieldGetAndSetExpiryMessage(key, "f", Expiration.Default, CommandFlags.None), "HGETEX");
        AssertCategory(LastWins, db.HashFieldGetAndSetExpiryMessage(key, "f", TimeSpan.FromMinutes(5), CommandFlags.None), "HGETEX EX");
        AssertCategory(LastWins, db.HashFieldGetAndSetExpiryMessage(key, "f", Expiration.Persist, CommandFlags.None), "HGETEX PERSIST");
    }

    [Fact]
    public async Task Copy_ReplaceIsAnUnconditionalOverwrite()
    {
        var db = await GetDatabaseAsync();

        // without REPLACE, COPY fails if the destination exists, so a replay is a no-op
        AssertCategory(Checked, db.GetCopyMessage("src", "dest", -1, replace: false, CommandFlags.None), "COPY");
        AssertCategory(Checked, db.GetCopyMessage("src", "dest", 3, replace: false, CommandFlags.None), "COPY DB");

        // ...with it, the destination is overwritten whatever was there
        AssertCategory(LastWins, db.GetCopyMessage("src", "dest", -1, replace: true, CommandFlags.None), "COPY REPLACE");
        AssertCategory(LastWins, db.GetCopyMessage("src", "dest", 3, replace: true, CommandFlags.None), "COPY DB REPLACE");
    }

    [Fact]
    public async Task StreamClaim_JustIdDoesNotBumpDeliveryCounts()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";
        RedisValue[] ids = ["5-5"];

        // reassignment plus a delivery-count bump: leave the per-command default
        AssertCategory(LastWins, db.GetStreamClaimMessage(key, "g", "c", 1000, ids, returnJustIds: false, CommandFlags.None), "XCLAIM");
        AssertCategory(LastWins, db.GetStreamAutoClaimMessage(key, "g", "c", 1000, "0-0", null, idsOnly: false, CommandFlags.None), "XAUTOCLAIM");

        // JUSTID explicitly does not bump the counter, so reassignment alone is idempotent
        AssertCategory(Checked, db.GetStreamClaimMessage(key, "g", "c", 1000, ids, returnJustIds: true, CommandFlags.None), "XCLAIM JUSTID");
        AssertCategory(Checked, db.GetStreamAutoClaimMessage(key, "g", "c", 1000, "0-0", null, idsOnly: true, CommandFlags.None), "XAUTOCLAIM JUSTID");
    }

    /// <summary>
    /// GEORADIUS[BYMEMBER] defaults to a write because of the STORE/STOREDIST variants that <c>Execute</c> could
    /// carry; this typed API cannot emit them, so it is a pure query.
    /// </summary>
    [Fact]
    public async Task GeoRadius_TypedApiIsAlwaysARead()
    {
        var db = await GetDatabaseAsync();
        RedisKey key = "k";

        AssertCategory(
            ReadOnly,
            db.GetGeoRadiusMessage(key, null, 1.5, 2.5, 100, GeoUnit.Meters, -1, null, GeoRadiusOptions.Default, CommandFlags.None),
            "GEORADIUS");
        AssertCategory(
            ReadOnly,
            db.GetGeoRadiusMessage(key, "member", double.NaN, double.NaN, 100, GeoUnit.Meters, 5, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None),
            "GEORADIUSBYMEMBER");
    }

    /// <summary>
    /// SCRIPT as a whole is server-admin *and* node-scoped; LOAD is neither. It has no keyspace effect, and the SHA
    /// it returns is a pure function of the script, so the same answer comes back from any node - and the hash is
    /// recorded against whichever endpoint actually replied. The absent node-scoped bit is the load-bearing half
    /// here: it is what separates LOAD from every other SCRIPT subcommand, and asserting the category alone would
    /// not see it.
    /// </summary>
    [Fact]
    public void ScriptLoad_IsConnectionLevelAndNotNodeScoped()
    {
        var msg = new RedisDatabase.ScriptLoadMessage(CommandFlags.None, "return 1");
        AssertCategory(CommandFlags.CommandRetryConnection, msg, "SCRIPT LOAD");
        Assert.False((msg.Flags & Message.CommandServerSpecific) != 0, "SCRIPT LOAD returns the same SHA from any node");

        // the control: the whole-command default it is departing from differs on *both* axes
        var fallback = CommandFlags.None.WithDefaultCategory(RedisCommand.SCRIPT);
        Assert.Equal(CommandFlags.CommandRetryServerAdmin, Message.GetRetryCategory(fallback));
        Assert.True((fallback & Message.CommandServerSpecific) != 0, "bare SCRIPT stays node-scoped");
    }

    /// <summary>
    /// CLIENT/CLUSTER/CONFIG/SCRIPT/SLOWLOG/LATENCY/MEMORY are each a single <see cref="RedisCommand"/> spanning
    /// very different verbs, so the whole-command default has to assume the worst (or, for MEMORY, assumed the
    /// best). Where the subcommand is known we categorize it properly.
    /// </summary>
    [Fact]
    public void ServerSubCommands_AreCategorizedBySubCommand()
    {
        const CommandFlags ServerAdmin = CommandFlags.CommandRetryServerAdmin,
                           Connection = CommandFlags.CommandRetryConnection;

        // MEMORY defaults to read-only, so PURGE was previously treated as a harmless read
        var purge = RedisServer.GetMemoryPurgeMessage(CommandFlags.None);
        AssertCategory(ServerAdmin, purge, "MEMORY PURGE");
        Assert.True((purge.Flags & Message.CommandServerSpecific) != 0, "MEMORY PURGE is node-scoped");

        // CLUSTER/SLOWLOG default to server-admin, but these subcommands only read
        AssertCategory(ReadOnly, RedisServer.GetClusterNodesMessage(CommandFlags.None), "CLUSTER NODES");
        AssertCategory(ReadOnly, RedisServer.GetSlowlogGetMessage(0, CommandFlags.None), "SLOWLOG GET");
        AssertCategory(ReadOnly, RedisServer.GetSlowlogGetMessage(25, CommandFlags.None), "SLOWLOG GET count");

        // CONFIG defaults to server-admin; CONFIG GET is safe metadata (as the docs already claimed)
        AssertCategory(Connection, RedisServer.GetConfigGetMessage(default, CommandFlags.None), "CONFIG GET");

        // all of these stay node-scoped: the answer belongs to the server we asked
        foreach (var msg in new[]
        {
            RedisServer.GetClusterNodesMessage(CommandFlags.None),
            RedisServer.GetSlowlogGetMessage(0, CommandFlags.None),
            RedisServer.GetConfigGetMessage(default, CommandFlags.None),
        })
        {
            Assert.True((msg.Flags & Message.CommandServerSpecific) != 0, $"{msg.CommandAndKey} should be node-scoped");
        }

        // and the caller still wins on the ladder, without losing the node-scoped bit
        var overridden = RedisServer.GetMemoryPurgeMessage(CallerOverride);
        AssertCategory(CallerOverride, overridden, "MEMORY PURGE, caller override");
        Assert.True((overridden.Flags & Message.CommandServerSpecific) != 0, "override must not clear server-specific");
    }

    [Fact]
    public void ScanCursor_OnlyResumedCursorsAreServerSpecific()
    {
        // a fresh iteration can start on any node...
        var fresh = CommandFlags.None.WithScanCursorCategory(0);
        Assert.Equal(ReadOnly, Message.GetRetryCategory(fresh));
        Assert.False((fresh & Message.CommandServerSpecific) != 0, "cursor 0 should not be server-specific");

        // ...but a resumed cursor only means something on the node that issued it
        var resumed = CommandFlags.None.WithScanCursorCategory(12341234);
        Assert.Equal(ReadOnly, Message.GetRetryCategory(resumed));
        Assert.True((resumed & Message.CommandServerSpecific) != 0, "a resumed cursor should be server-specific");

        // the server-specific bit is orthogonal to the ladder, so a caller category must not suppress it
        var overridden = CallerOverride.WithScanCursorCategory(12341234);
        Assert.Equal(CallerOverride, Message.GetRetryCategory(overridden));
        Assert.True((overridden & Message.CommandServerSpecific) != 0, "caller category must not clear server-specific");
    }
}
