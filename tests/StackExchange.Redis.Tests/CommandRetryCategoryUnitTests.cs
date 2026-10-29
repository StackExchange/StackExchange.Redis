using System;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.RoundTripUnitTests;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Covers the commands whose retry category depends on their *arguments* rather than just the command name
/// (see https://github.com/StackExchange/StackExchange.Redis/issues/3148). Each command is issued through the
/// new surface against a fake executor, and the category is read off the flags the request reached the
/// executor with - so nothing here needs a real Redis.
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

    private const string Ok = "+OK\r\n", One = ":1\r\n", Empty = "*0\r\n", Nil = "*-1\r\n", Score = "$1\r\n1\r\n", Id = "$3\r\n1-0\r\n";

    /// <summary>Issue one command through <see cref="IDatabaseAsync"/> and assert the category it was sent with.</summary>
    private async Task AssertCategory(CommandFlags expected, string reply, Func<IDatabaseAsync, Task> call, string because)
    {
        var executor = new RoundTripExecutor(reply);
        await call(RoundTrip.Database(executor));
        AssertCategory(expected, executor, because);
    }

    /// <summary>Issue one command through the context surface and assert the category it was sent with.</summary>
    private async Task AssertCategory(CommandFlags expected, string reply, Func<RespDatabaseContext, ValueTask> call, string because)
    {
        var executor = new RoundTripExecutor(reply);
        await call(RoundTrip.Context(executor));
        AssertCategory(expected, executor, because);
    }

    /// <summary>Assert the category of the one request <paramref name="executor"/> saw, and return its flags.</summary>
    private CommandFlags AssertCategory(CommandFlags expected, FakeExecutor executor, string because)
    {
        var flags = Assert.Single(executor.Flags);
        var actual = CommandFlagsInternal.GetRetryCategory(flags);
        log.WriteLine("{0}: {1} (expected {2}) - {3}", executor.Sent[0], actual, expected, because);
        Assert.Equal(expected, actual);
        return flags;
    }

    private static RespServerContext Server(FakeExecutor executor) => new(new RespContext().WithExecutor(executor));

    private static async ValueTask Discard<T>(ValueTask<T> pending) => await pending;

    [Fact]
    public async Task StringSet_CategoryFollowsCondition()
    {
        RedisKey key = "k";
        RedisValue val = "v";
        var ttl = TimeSpan.FromMinutes(5);

        // the plain form is an unconditional overwrite
        await AssertCategory(LastWins, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Always), "SET");
        await AssertCategory(LastWins, Ok, db => db.StringSetAsync(key, val, ttl, When.Always), "SETEX");

        // ...but NX/XX make it conditional, whichever spelling we emit
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.NotExists), "SETNX");
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Exists), "SET XX");
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, ttl, When.NotExists), "SET EX NX");
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, ttl, When.Exists), "SET EX XX");

        // ...as does a compare-and-set; this is the case named in #3148
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Equal("old")), "SET IFEQ");
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.NotEqual("old")), "SET IFNE");
        await AssertCategory(Checked, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.DigestEqual("old")), "SET IFDEQ");
    }

    [Fact]
    public async Task StringSet_CallerCategoryWins()
    {
        RedisKey key = "k";
        RedisValue val = "v";

        // the whole point of the first-wins rule: it is ultimately the caller's data
        await AssertCategory(CallerOverride, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Always, CallerOverride), "SET, caller override");
        await AssertCategory(CallerOverride, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Exists, CallerOverride), "SET XX, caller override");
        await AssertCategory(CallerOverride, Ok, db => db.StringSetAsync(key, val, Expiration.Default, ValueCondition.Equal("old"), CallerOverride), "SET IFEQ, caller override");
    }

    [Fact]
    public async Task Sort_StoreIsAWriteNotARead()
    {
        RedisKey key = "k";

        // a bare SORT is a read...
        await AssertCategory(ReadOnly, Empty, db => db.SortAsync(key, 0, -1, Order.Ascending, SortType.Numeric), "SORT");
        await AssertCategory(ReadOnly, Empty, db => db.SortAsync(key, 5, 10, Order.Descending, SortType.Alphabetic, "by_*"), "SORT BY LIMIT");

        // ...but the STORE variant writes the destination key, and was previously mis-categorized as a read
        await AssertCategory(LastWins, One, db => db.SortAndStoreAsync("dest", key, 0, -1, Order.Ascending, SortType.Numeric), "SORT STORE");
        await AssertCategory(CallerOverride, One, db => db.SortAndStoreAsync("dest", key, 0, -1, Order.Ascending, SortType.Numeric, flags: CallerOverride), "SORT STORE, caller override");
    }

    [Fact]
    public async Task SortedSetAdd_IncrementAccumulates()
    {
        RedisKey key = "k";
        RedisValue member = "m";

        // plain ZADD overwrites the score
        await AssertCategory(LastWins, One, db => db.SortedSetAddAsync(key, member, 1.0, SortedSetWhen.Always), "ZADD");

        // NX/XX are conditional; GT/LT are monotone, so re-applying converges
        await AssertCategory(Checked, One, db => db.SortedSetAddAsync(key, member, 1.0, SortedSetWhen.NotExists), "ZADD NX");
        await AssertCategory(Checked, One, db => db.SortedSetAddAsync(key, member, 1.0, SortedSetWhen.Exists), "ZADD XX");
        await AssertCategory(Checked, One, db => db.SortedSetAddAsync(key, member, 1.0, SortedSetWhen.GreaterThan), "ZADD GT");
        await AssertCategory(Checked, One, db => db.SortedSetAddAsync(key, member, 1.0, SortedSetWhen.LessThan), "ZADD LT");

        // ZINCRBY compounds, and so does the ZADD ... INCR form it degrades to under XX - which previously
        // inherited ZADD's "last wins" and was therefore retried by the default policy
        await AssertCategory(Accumulating, Score, db => db.SortedSetIncrementAsync(key, member, 1.0, ValueCondition.Always, CommandFlags.None), "ZINCRBY");
        await AssertCategory(Accumulating, Score, db => db.SortedSetIncrementAsync(key, member, 1.0, ValueCondition.Exists, CommandFlags.None), "ZADD XX INCR");

        // ...except under NX, where a replay can only find the member present and no-op
        await AssertCategory(Checked, Score, db => db.SortedSetIncrementAsync(key, member, 1.0, ValueCondition.NotExists, CommandFlags.None), "ZADD NX INCR");
    }

    private static StreamAddOptions Options(RedisValue messageId, in StreamIdempotentId idempotentId) =>
        new() { MessageId = messageId, IdempotentId = idempotentId };

    [Fact]
    public async Task StreamAdd_ExplicitAndIdempotentIdsAreReplaySafe()
    {
        RedisKey key = "k";
        var noId = default(StreamIdempotentId);

        // "*" lets the server pick the id, so a replay appends a second entry
        var auto = Options("*", in noId);
        await AssertCategory(Accumulating, Id, db => db.StreamAddAsync(key, "f", "v", auto), "XADD *");

        // an explicit id is rejected second time round ("equal or smaller")
        var explicitId = Options("5-5", in noId);
        await AssertCategory(Checked, Id, db => db.StreamAddAsync(key, "f", "v", explicitId), "XADD with explicit id");

        // IDMP producer id: the server deduplicates. (No MessageId: the public entry point refuses one alongside
        // an idempotent id, where the old message builder let "*" through.)
        var idmp = new StreamAddOptions { IdempotentId = new StreamIdempotentId("producer", "item-1") };
        await AssertCategory(Checked, Id, db => db.StreamAddAsync(key, "f", "v", idmp), "XADD IDMP");

        // IDMPAUTO producer: same, with the id derived from the entry content
        var idmpAuto = new StreamAddOptions { IdempotentId = new StreamIdempotentId("producer") };
        await AssertCategory(Checked, Id, db => db.StreamAddAsync(key, "f", "v", idmpAuto), "XADD IDMPAUTO");
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
        RedisKey key = "k";

        Task Add(RedisValue id, CommandFlags expected, string because)
        {
            var options = Options(id, default);
            return AssertCategory(expected, Id, db => db.StreamAddAsync(key, "f", "v", options), because);
        }

        // anything the server completes accumulates...
        await Add("*", Accumulating, "XADD *");
        await Add("5-*", Accumulating, "XADD <ms>-* (server picks the sequence)");
        await Add("1526919030474-*", Accumulating, "XADD <ms>-* (realistic ms)");

        // ...and only a *fully* specified id cannot be appended twice
        await Add("5-5", Checked, "XADD with a fully explicit id");
        await Add("1526919030474-0", Checked, "XADD with a fully explicit id (realistic ms)");
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
        // ">" consumes undelivered entries and advances the group cursor: never retry
        await ReadGroupSingle(">", null, Never, "XREADGROUP >");

        // CLAIM never demotes, whatever the position
        await ReadGroupSingle("0-0", Idle, Never, "XREADGROUP with explicit id + CLAIM");
        await ReadGroupSingle(">", Idle, Never, "XREADGROUP > + CLAIM");

        // and the same for the multi-stream form
        await ReadGroupMulti([">", "0-0"], null, Never, "XREADGROUP multi, one \">\" anywhere");
        await ReadGroupMulti(["0-0", "0-0"], Idle, Never, "XREADGROUP multi, all explicit + CLAIM");
    }

    /// <summary>The demotion itself: an explicit id re-reads our own pending list, so it is a read.</summary>
    [Fact]
    public async Task StreamReadGroup_ExplicitIdsAreDemotedToARead()
    {
        await ReadGroupSingle("0-0", null, ReadOnly, "XREADGROUP with explicit id");
        await ReadGroupMulti(["0-0", "0-0"], null, ReadOnly, "XREADGROUP multi, all explicit");
    }

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    private Task ReadGroupSingle(RedisValue position, TimeSpan? claim, CommandFlags expected, string because) =>
        AssertCategory(expected, Nil, db => db.StreamReadGroupAsync("k", "g", "c", position, count: null, noAck: false, claimMinIdleTime: claim), because);

    private Task ReadGroupMulti(RedisValue[] positions, TimeSpan? claim, CommandFlags expected, string because)
    {
        var streams = Array.ConvertAll(positions, p => new StreamPosition("k", p));
        return AssertCategory(expected, Nil, db => db.StreamReadGroupAsync(streams, "g", "c", countPerStream: null, noAck: false, claimMinIdleTime: claim), because);
    }

    /// <summary>
    /// The hash-field TTL commands take the same NX/XX/GT/LT conditions as the key-level ones, so they get the same
    /// rule; without this, HEXPIRE ... NX is categorized as a blind overwrite while EXPIRE ... NX is not.
    /// </summary>
    [Fact]
    public async Task Expire_CategoryFollowsCondition()
    {
        RedisKey key = "k";
        var ttl = TimeSpan.FromMinutes(5);
        var deadline = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const string Expired = "*1\r\n:1\r\n";

        // a bare EXPIRE/EXPIREAT is an unconditional overwrite of the TTL
        await AssertCategory(LastWins, One, db => db.KeyExpireAsync(key, ttl, ExpireWhen.Always), "EXPIRE");
        await AssertCategory(LastWins, One, db => db.KeyExpireAsync(key, deadline, ExpireWhen.Always), "EXPIREAT");

        // NX/XX are conditional; GT/LT are monotone, so re-applying converges on the same deadline
        foreach (var when in new[] { ExpireWhen.HasNoExpiry, ExpireWhen.HasExpiry, ExpireWhen.GreaterThanCurrentExpiry, ExpireWhen.LessThanCurrentExpiry })
        {
            await AssertCategory(Checked, One, db => db.KeyExpireAsync(key, ttl, when), $"EXPIRE {when.ToLiteral()}");
            await AssertCategory(Checked, One, db => db.KeyExpireAsync(key, deadline, when), $"EXPIREAT {when.ToLiteral()}");
        }

        // and the hash-field forms follow the same rule
        await AssertCategory(LastWins, Expired, db => db.HashFieldExpireAsync(key, ["f"], ttl, ExpireWhen.Always), "HEXPIRE");
        foreach (var when in new[] { ExpireWhen.HasNoExpiry, ExpireWhen.HasExpiry, ExpireWhen.GreaterThanCurrentExpiry, ExpireWhen.LessThanCurrentExpiry })
        {
            await AssertCategory(Checked, Expired, db => db.HashFieldExpireAsync(key, ["f"], ttl, when), $"HEXPIRE {when.ToLiteral()}");
        }
    }

    /// <summary>
    /// GETEX/HGETEX read like a GET until any of EX/PX/EXAT/PXAT/PERSIST is supplied, at which point they mutate
    /// the TTL. The bare form is the interesting control: it must stay a read.
    /// </summary>
    /// <remarks>
    /// Through the context surface rather than <see cref="IDatabase"/>, because only it can express the bare
    /// form: the <see cref="IDatabase"/> overloads map a null TTL to PERSIST.
    /// </remarks>
    [Fact]
    public async Task GetEx_TtlOptionsMakeItAWrite()
    {
        RedisKey key = "k";
        const string Value = "$1\r\nv\r\n";

        await AssertCategory(ReadOnly, Value, ctx => Discard(ctx.Strings.GetSetExpiryAsync(key, Expiration.Default)), "GETEX");
        await AssertCategory(LastWins, Value, ctx => Discard(ctx.Strings.GetSetExpiryAsync(key, TimeSpan.FromMinutes(5))), "GETEX EX");
        await AssertCategory(LastWins, Value, ctx => Discard(ctx.Strings.GetSetExpiryAsync(key, Expiration.Persist)), "GETEX PERSIST");

        const string Values = "*1\r\n$1\r\nv\r\n";
        await AssertCategory(ReadOnly, Values, ctx => Discard(ctx.Hashes.GetSetExpiryAsync(key, "f", Expiration.Default)), "HGETEX");
        await AssertCategory(LastWins, Values, ctx => Discard(ctx.Hashes.GetSetExpiryAsync(key, "f", TimeSpan.FromMinutes(5))), "HGETEX EX");
        await AssertCategory(LastWins, Values, ctx => Discard(ctx.Hashes.GetSetExpiryAsync(key, "f", Expiration.Persist)), "HGETEX PERSIST");
    }

    [Fact]
    public async Task Copy_WithoutReplaceIsChecked()
    {
        // without REPLACE, COPY fails if the destination exists, so a replay is a no-op
        await AssertCategory(Checked, One, db => db.KeyCopyAsync("src", "dest", -1, replace: false), "COPY");
        await AssertCategory(Checked, One, db => db.KeyCopyAsync("src", "dest", 3, replace: false), "COPY DB");
    }

    [Fact]
    public async Task Copy_ReplaceIsAnUnconditionalOverwrite()
    {
        // with REPLACE, the destination is overwritten whatever was there
        await AssertCategory(LastWins, One, db => db.KeyCopyAsync("src", "dest", -1, replace: true), "COPY REPLACE");
        await AssertCategory(LastWins, One, db => db.KeyCopyAsync("src", "dest", 3, replace: true), "COPY DB REPLACE");
    }

    [Fact]
    public async Task StreamClaim_JustIdDoesNotBumpDeliveryCounts()
    {
        RedisKey key = "k";
        RedisValue[] ids = ["5-5"];
        const string AutoClaimed = "*3\r\n$3\r\n0-0\r\n*0\r\n*0\r\n";

        // reassignment plus a delivery-count bump: leave the per-command default
        await AssertCategory(LastWins, Empty, db => db.StreamClaimAsync(key, "g", "c", 1000, ids), "XCLAIM");
        await AssertCategory(LastWins, AutoClaimed, db => db.StreamAutoClaimAsync(key, "g", "c", 1000, "0-0"), "XAUTOCLAIM");

        // JUSTID explicitly does not bump the counter, so reassignment alone is idempotent
        await AssertCategory(Checked, Empty, db => db.StreamClaimIdsOnlyAsync(key, "g", "c", 1000, ids), "XCLAIM JUSTID");
        await AssertCategory(Checked, AutoClaimed, db => db.StreamAutoClaimIdsOnlyAsync(key, "g", "c", 1000, "0-0"), "XAUTOCLAIM JUSTID");
    }

    /// <summary>
    /// GEORADIUS[BYMEMBER] defaults to a write because of the STORE/STOREDIST variants that <c>Execute</c> could
    /// carry; this typed API cannot emit them, so it is a pure query.
    /// </summary>
    [Fact]
    public async Task GeoRadius_TypedApiIsAlwaysARead()
    {
        RedisKey key = "k";

        await AssertCategory(ReadOnly, Empty, db => db.GeoRadiusAsync(key, 1.5, 2.5, 100, GeoUnit.Meters), "GEORADIUS");
        await AssertCategory(ReadOnly, Empty, db => db.GeoRadiusAsync(key, "member", 100, GeoUnit.Meters, 5, Order.Ascending), "GEORADIUSBYMEMBER");
    }

    /// <summary>
    /// SCRIPT as a whole is server-admin *and* node-scoped; LOAD is neither. It has no keyspace effect, and the SHA
    /// it returns is a pure function of the script, so the same answer comes back from any node - and the hash is
    /// recorded against whichever endpoint actually replied. The absent node-scoped bit is the load-bearing half
    /// here: it is what separates LOAD from every other SCRIPT subcommand, and asserting the category alone would
    /// not see it.
    /// </summary>
    [Fact]
    public async Task ScriptLoad_IsConnectionLevelAndNotNodeScoped()
    {
        var executor = new RoundTripExecutor("$40\r\n" + new string('a', 40) + "\r\n");
        await Server(executor).Scripts.LoadHex("return 1");
        var flags = AssertCategory(CommandFlags.CommandRetryConnection, executor, "SCRIPT LOAD");
        Assert.False((flags & CommandFlagsInternal.CommandServerSpecific) != 0, "SCRIPT LOAD returns the same SHA from any node");

        // the control: the whole-command default it is departing from differs on *both* axes
        var fallback = CommandFlags.None.WithDefaultCategory(RedisCommand.SCRIPT);
        Assert.Equal(CommandFlags.CommandRetryServerAdmin, CommandFlagsInternal.GetRetryCategory(fallback));
        Assert.True((fallback & CommandFlagsInternal.CommandServerSpecific) != 0, "bare SCRIPT stays node-scoped");
    }

    /// <summary>
    /// CLIENT/CLUSTER/CONFIG/SCRIPT/SLOWLOG/LATENCY/MEMORY are each a single <see cref="RedisCommand"/> spanning
    /// very different verbs, so the whole-command default has to assume the worst (or, for MEMORY, assumed the
    /// best). Where the subcommand is known we categorize it properly.
    /// </summary>
    [Fact]
    public async Task ServerSubCommands_AreCategorizedBySubCommand()
    {
        // CLUSTER defaults to server-admin, but NODES only reads - and stays node-scoped: the answer belongs
        // to the server we asked
        var executor = new RoundTripExecutor("$0\r\n\r\n");
        await Server(executor).Diagnostics.ClusterNodesRaw();
        var flags = AssertCategory(ReadOnly, executor, "CLUSTER NODES");
        Assert.True((flags & CommandFlagsInternal.CommandServerSpecific) != 0, "CLUSTER NODES should be node-scoped");
    }

    [Fact]
    public void ScanCursor_OnlyResumedCursorsAreServerSpecific()
    {
        // a fresh iteration can start on any node...
        var fresh = CommandFlags.None.WithScanCursorCategory(0);
        Assert.Equal(ReadOnly, CommandFlagsInternal.GetRetryCategory(fresh));
        Assert.False((fresh & CommandFlagsInternal.CommandServerSpecific) != 0, "cursor 0 should not be server-specific");

        // ...but a resumed cursor only means something on the node that issued it
        var resumed = CommandFlags.None.WithScanCursorCategory(12341234);
        Assert.Equal(ReadOnly, CommandFlagsInternal.GetRetryCategory(resumed));
        Assert.True((resumed & CommandFlagsInternal.CommandServerSpecific) != 0, "a resumed cursor should be server-specific");

        // the server-specific bit is orthogonal to the ladder, so a caller category must not suppress it
        var overridden = CallerOverride.WithScanCursorCategory(12341234);
        Assert.Equal(CallerOverride, CommandFlagsInternal.GetRetryCategory(overridden));
        Assert.True((overridden & CommandFlagsInternal.CommandServerSpecific) != 0, "caller category must not clear server-specific");
    }
}
