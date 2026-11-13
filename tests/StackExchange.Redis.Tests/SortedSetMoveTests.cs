using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Live tests for the Redis 8.12 (rc3 onwards) atomic sorted-set moves:
/// <see href="https://redis.io/commands/zmove"/> and <see href="https://redis.io/commands/zmovemember"/>.
/// </summary>
[RunPerProtocol]
public class SortedSetMoveTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static readonly SortedSetEntry[] Entries =
    [
        new("a", 1), new("b", 2), new("c", 3), new("d", 4), new("e", 5),
    ];

    private async Task<(IDatabase Db, RedisKey Source, RedisKey Destination)> InitAsync(IConnectionMultiplexer conn, string caller)
    {
        var db = conn.GetDatabase();
        // one slot, so that the same tests hold against a cluster
        RedisKey source = "{" + caller + "}:src", destination = "{" + caller + "}:dst";
        await db.KeyDeleteAsync([source, destination]);
        await db.SortedSetAddAsync(source, Entries);
        return (db, source, destination);
    }

    private static string Elements(SortedSetEntry[]? entries) => entries is null ? "(null)" : string.Join(",", entries.Select(x => x.Element.ToString()));

    [Fact]
    public async Task Move_DefaultIsOneFromTheLowEnd()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var moved = db.SortedSetMove(src, dst);

        var entry = Assert.Single(moved!); // one member is still a list of one
        Assert.Equal("a", entry.Element);
        Assert.Equal(1, entry.Score);
        Assert.Equal(4, db.SortedSetLength(src));
        Assert.Equal(1, db.SortedSetScore(dst, "a")); // the score travels
    }

    [Fact]
    public async Task Move_HighEndWithCount_IsDescending()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var moved = await db.SortedSetMoveAsync(src, dst, Order.Descending, 2);

        Assert.Equal("e,d", Elements(moved));
        Assert.Equal(new double[] { 5, 4 }, moved!.Select(x => x.Score));
        Assert.Equal(3, await db.SortedSetLengthAsync(src));
        Assert.Equal(2, await db.SortedSetLengthAsync(dst));
    }

    [Fact]
    public async Task Move_CountBeyondLength_MovesAllAndDeletesSource()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var moved = await db.SortedSetMoveAsync(src, dst, count: 100);

        Assert.Equal("a,b,c,d,e", Elements(moved));
        Assert.False(await db.KeyExistsAsync(src));
        Assert.Equal(5, await db.SortedSetLengthAsync(dst));
    }

    [Fact]
    public async Task Move_NothingToMove_IsNull()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        await db.KeyDeleteAsync(src);

        Assert.Null(db.SortedSetMove(src, dst)); // missing source
        Assert.Null(await db.SortedSetMoveAsync(src, dst, Order.Descending, 10));
        Assert.Null(await db.SortedSetMoveByScoreAsync(src, dst, 0, 100));
        Assert.Null(await db.SortedSetMoveMembersAsync(src, dst, ["a"]));
        Assert.False(await db.KeyExistsAsync(dst)); // and nothing is created
    }

    [Fact]
    public async Task Move_SameKey_IsNull()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, _) = await InitAsync(conn, Me());

        Assert.Null(await db.SortedSetMoveAsync(src, src, count: 2));
        Assert.Equal(5, await db.SortedSetLengthAsync(src));
    }

    [Fact]
    public async Task Move_CountZero_IsTheServersError()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        // not swallowed or "fixed" client-side
        var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.SortedSetMoveAsync(src, dst, count: 0));
        Assert.Equal("ERR count should be greater than 0", ex.Message);
        Assert.Equal(5, await db.SortedSetLengthAsync(src));
    }

    [Fact]
    public async Task MoveByScore_NoCount_MovesTheWholeRange()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var moved = db.SortedSetMoveByScore(src, dst, 2, 4);

        Assert.Equal("b,c,d", Elements(moved));
        Assert.Equal(2, db.SortedSetLength(src));
    }

    [Fact]
    public async Task MoveByScore_ExclusiveAndInfiniteBounds_WithCount()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        // (2, 4]
        Assert.Equal("c,d", Elements(await db.SortedSetMoveByScoreAsync(src, dst, 2, 4, Exclude.Start)));

        // [-inf, 5) with a count: the lowest first; a, b and e remain, e is excluded
        Assert.Equal("a", Elements(await db.SortedSetMoveByScoreAsync(src, dst, double.NegativeInfinity, 5, Exclude.Stop, count: 1)));
        Assert.Equal("b", Elements(await db.SortedSetMoveByScoreAsync(src, dst, double.NegativeInfinity, 5, Exclude.Stop)));

        Assert.Null(await db.SortedSetMoveByScoreAsync(src, dst, 10, 20)); // empty range
        Assert.Equal("e", Elements(await db.SortedSetRangeByRankWithScoresAsync(src)));
    }

    [Fact]
    public async Task MoveMembers_ArgumentOrder_SkipsMissingAndDuplicates()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var moved = db.SortedSetMoveMembers(src, dst, ["d", "nope", "b", "d"]);

        Assert.Equal("d,b", Elements(moved));
        Assert.Equal(new double[] { 4, 2 }, moved!.Select(x => x.Score));
        Assert.Equal("a,c,e", Elements(db.SortedSetRangeByRankWithScores(src)));
    }

    [Fact]
    public async Task MoveMembers_Always_OverwritesDestinationScore()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        await db.SortedSetAddAsync(dst, "a", 100);

        var moved = await db.SortedSetMoveMembersAsync(src, dst, ["a"]);

        Assert.Equal(1, Assert.Single(moved!).Score);
        Assert.Equal(1, await db.SortedSetScoreAsync(dst, "a"));
        Assert.Null(await db.SortedSetScoreAsync(src, "a"));
    }

    [Fact]
    public async Task MoveMembers_NotExists_LeavesClashesInTheSource()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        await db.SortedSetAddAsync(dst, "a", 100);

        var moved = await db.SortedSetMoveMembersAsync(src, dst, ["a", "b"], When.NotExists);

        Assert.Equal("b", Elements(moved)); // a member the condition rejects is not in the reply
        Assert.Equal(100, await db.SortedSetScoreAsync(dst, "a"));
        Assert.Equal(1, await db.SortedSetScoreAsync(src, "a"));
    }

    [Fact]
    public async Task MoveMembers_Exists_OnlyMovesWhatTheDestinationHas()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        // XX against a missing destination moves nothing and creates nothing
        Assert.Null(await db.SortedSetMoveMembersAsync(src, dst, ["a", "b"], When.Exists));
        Assert.False(await db.KeyExistsAsync(dst));

        await db.SortedSetAddAsync(dst, "a", 100);
        var moved = await db.SortedSetMoveMembersAsync(src, dst, ["a", "b"], When.Exists);

        Assert.Equal("a", Elements(moved));
        Assert.Equal(1, await db.SortedSetScoreAsync(dst, "a"));
        Assert.Equal(2, await db.SortedSetScoreAsync(src, "b"));
    }

    [Fact]
    public async Task MoveMembers_KeywordLikeMembers_AreData()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        await db.SortedSetAddAsync(src, [new("NX", 10), new("MEMBERS", 11), new("COUNT", 12)]);

        var moved = await db.SortedSetMoveMembersAsync(src, dst, ["NX", "MEMBERS", "COUNT"]);

        Assert.Equal("NX,MEMBERS,COUNT", Elements(moved));
    }

    [Fact]
    public async Task Move_WrongType_IsTheServersError()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        await db.StringSetAsync(dst, "not a sorted set");

        var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.SortedSetMoveAsync(src, dst));
        Assert.StartsWith("WRONGTYPE", ex.Message);
        Assert.Equal(5, await db.SortedSetLengthAsync(src)); // nothing moved
    }

    [Fact]
    public async Task Move_InTransaction_HasTheSameTwoReplies()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        var tran = db.CreateTransaction();
        var first = tran.SortedSetMoveAsync(src, dst, count: 5);
        var second = tran.SortedSetMoveAsync(src, dst); // the source is gone by now
        Assert.True(await tran.ExecuteAsync());

        Assert.Equal("a,b,c,d,e", Elements(await first));
        Assert.Null(await second);
    }

    [Fact]
    public async Task Cluster_SameSlot_RoutesAndMoves()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());

        Assert.Equal("a,b", Elements(await db.SortedSetMoveAsync(src, dst, count: 2)));
        Assert.Equal("c", Elements(await db.SortedSetMoveByScoreAsync(src, dst, 3, 3)));
        Assert.Equal("e,d", Elements(await db.SortedSetMoveMembersAsync(src, dst, ["e", "d"])));
        Assert.False(await db.KeyExistsAsync(src));
        Assert.Equal(5, await db.SortedSetLengthAsync(dst));
    }

    [Fact]
    public async Task Cluster_CrossSlot_FailsAndMovesNothing()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_12_0_rc3);
        var db = conn.GetDatabase();
        RedisKey src = "{" + Me() + ":1}:src", dst = "{" + Me() + ":2}:dst";
        Assert.NotEqual(conn.HashSlot(src), conn.HashSlot(dst));
        await db.KeyDeleteAsync(src);
        await db.KeyDeleteAsync(dst);
        await db.SortedSetAddAsync(src, Entries);

        // One command, never split across slots. Which layer says no depends on the message shape: the bare form is
        // refused by the library ("Multi-key operations must involve a single slot"), the longer forms are sent to
        // the source's node and refused by the server (CROSSSLOT). Either way it is an error that names the slot
        // rule, and nothing moves.
        async Task AssertRefused(System.Func<Task<SortedSetEntry[]?>> move)
        {
            var ex = await Assert.ThrowsAnyAsync<System.Exception>(move);
            Assert.Contains("slot", ex.Message, System.StringComparison.OrdinalIgnoreCase);
        }

        await AssertRefused(() => db.SortedSetMoveAsync(src, dst));
        await AssertRefused(() => db.SortedSetMoveAsync(src, dst, count: 2));
        await AssertRefused(() => db.SortedSetMoveByScoreAsync(src, dst, 0, 10));
        await AssertRefused(() => db.SortedSetMoveMembersAsync(src, dst, ["a", "b"]));

        Assert.Equal(5, await db.SortedSetLengthAsync(src));
        Assert.False(await db.KeyExistsAsync(dst));
    }

    [Fact]
    public async Task Move_ViaKeyPrefix()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0_rc3);
        var (db, src, dst) = await InitAsync(conn, Me());
        var prefixed = StackExchange.Redis.KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix(db, "{" + Me() + "}:");

        var moved = await prefixed.SortedSetMoveMembersAsync("src", "dst", ["c"]);

        Assert.Equal("c", Elements(moved));
        Assert.Equal(3, await db.SortedSetScoreAsync(dst, "c"));
    }
}
