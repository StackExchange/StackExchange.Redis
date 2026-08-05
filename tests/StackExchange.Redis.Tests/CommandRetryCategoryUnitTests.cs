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
                               Accumulating = CommandFlags.CommandRetryWriteAccumulating;

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
            db.GetStreamAddMessage(key, "*", in noId, null, false, pair, null, StreamTrimMode.KeepReferences, CommandFlags.None),
            "XADD *");

        // an explicit id is rejected second time round ("equal or smaller")
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, "5-5", in noId, null, false, pair, null, StreamTrimMode.KeepReferences, CommandFlags.None),
            "XADD with explicit id");

        // IDMP producer id: the server deduplicates
        var idmp = new StreamIdempotentId("producer", "item-1");
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, "*", in idmp, null, false, pair, null, StreamTrimMode.KeepReferences, CommandFlags.None),
            "XADD IDMP");

        // IDMPAUTO producer: same, with the id derived from the entry content
        var idmpAuto = new StreamIdempotentId("producer");
        AssertCategory(
            Checked,
            db.GetStreamAddMessage(key, "*", in idmpAuto, null, false, pair, null, StreamTrimMode.KeepReferences, CommandFlags.None),
            "XADD IDMPAUTO");
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
