using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Exact-bytes tests for the Redis 8.12 sorted-set moves, <c>ZMOVE</c> and <c>ZMOVEMEMBER</c>.
/// </summary>
public class SortedSetMoveRoundTrip(ITestOutputHelper log)
{
    private const string Prefix = "$1\r\ns\r\n$1\r\nd\r\n"; // the two keys
    private const string TwoPairsResp2 = "*2\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n*2\r\n$1\r\nb\r\n$1\r\n2\r\n";
    private const string TwoPairsResp3 = "*2\r\n*2\r\n$1\r\na\r\n,1\r\n*2\r\n$1\r\nb\r\n,2\r\n";

    private static RedisDatabase Db() => new(null!, 0, null);

    [Theory(Timeout = 1000)]
    [InlineData(Order.Ascending, null, "*4\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n")]
    [InlineData(Order.Descending, null, "*4\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMAX\r\n")]
    [InlineData(Order.Ascending, 10L, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n$5\r\nCOUNT\r\n$2\r\n10\r\n")]
    [InlineData(Order.Descending, 10L, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMAX\r\n$5\r\nCOUNT\r\n$2\r\n10\r\n")]
    // the count passes through: an explicit 1 is not elided (as SortedSetPop does), and values the server rejects are still sent
    [InlineData(Order.Ascending, 1L, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n$5\r\nCOUNT\r\n$1\r\n1\r\n")]
    [InlineData(Order.Ascending, 0L, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n$5\r\nCOUNT\r\n$1\r\n0\r\n")]
    [InlineData(Order.Ascending, -1L, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n$5\r\nCOUNT\r\n$2\r\n-1\r\n")]
    public async Task Move_ByEnd(Order order, long? count, string requestResp)
    {
        var msg = Db().GetSortedSetMoveMessage("s", "d", order, count, CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, requestResp, TwoPairsResp2, log: log);

        AssertTwoPairs(result);
    }

    [Theory(Timeout = 1000)]
    [InlineData(1d, 2d, Exclude.None, null, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$1\r\n1\r\n$1\r\n2\r\n")]
    [InlineData(1d, 2d, Exclude.None, 500L, "*8\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$1\r\n1\r\n$1\r\n2\r\n$5\r\nCOUNT\r\n$3\r\n500\r\n")]
    [InlineData(1d, 2d, Exclude.Start, null, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$2\r\n(1\r\n$1\r\n2\r\n")]
    [InlineData(1d, 2d, Exclude.Stop, null, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$1\r\n1\r\n$2\r\n(2\r\n")]
    [InlineData(1.5d, 2.5d, Exclude.Both, 3L, "*8\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$4\r\n(1.5\r\n$4\r\n(2.5\r\n$5\r\nCOUNT\r\n$1\r\n3\r\n")]
    [InlineData(double.NegativeInfinity, double.PositiveInfinity, Exclude.None, null, "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$4\r\n-inf\r\n$4\r\n+inf\r\n")]
    [InlineData(double.NegativeInfinity, 1700000000d, Exclude.Stop, 500L, "*8\r\n$5\r\nZMOVE\r\n" + Prefix + "$7\r\nBYSCORE\r\n$4\r\n-inf\r\n$11\r\n(1700000000\r\n$5\r\nCOUNT\r\n$3\r\n500\r\n")]
    public async Task Move_ByScore(double start, double stop, Exclude exclude, long? count, string requestResp)
    {
        var msg = Db().GetSortedSetMoveByScoreMessage("s", "d", start, stop, exclude, count, CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, requestResp, TwoPairsResp2, log: log);

        AssertTwoPairs(result);
    }

    [Theory(Timeout = 1000)]
    [InlineData(When.Always, "*7\r\n$11\r\nZMOVEMEMBER\r\n" + Prefix + "$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n")]
    [InlineData(When.NotExists, "*8\r\n$11\r\nZMOVEMEMBER\r\n" + Prefix + "$2\r\nNX\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n")]
    [InlineData(When.Exists, "*8\r\n$11\r\nZMOVEMEMBER\r\n" + Prefix + "$2\r\nXX\r\n$7\r\nMEMBERS\r\n$1\r\n2\r\n$1\r\na\r\n$1\r\nb\r\n")]
    public async Task MoveMembers_Condition(When when, string requestResp)
    {
        var msg = Db().GetSortedSetMoveMembersMessage("s", "d", ["a", "b"], when, CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, requestResp, TwoPairsResp2, log: log);

        AssertTwoPairs(result);
    }

    [Fact(Timeout = 1000)]
    public async Task MoveMembers_PassThrough_KeywordsDuplicatesAndOrder()
    {
        // members that look like keywords are data, and duplicates / order are not "tidied"
        var msg = Db().GetSortedSetMoveMembersMessage("s", "d", ["NX", "MEMBERS", "b", "b", "a"], When.Always, CommandFlags.None);
        const string RequestResp = "*10\r\n$11\r\nZMOVEMEMBER\r\n" + Prefix + "$7\r\nMEMBERS\r\n$1\r\n5\r\n$2\r\nNX\r\n$7\r\nMEMBERS\r\n$1\r\nb\r\n$1\r\nb\r\n$1\r\na\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, RequestResp, "*-1\r\n", log: log);

        Assert.Null(result);
    }

    [Fact(Timeout = 1000)]
    public async Task MoveMembers_Empty_IsSentForTheServerToReject()
    {
        var msg = Db().GetSortedSetMoveMembersMessage("s", "d", [], When.Always, CommandFlags.None);
        const string RequestResp = "*5\r\n$11\r\nZMOVEMEMBER\r\n" + Prefix + "$7\r\nMEMBERS\r\n$1\r\n0\r\n";

        var ex = await Assert.ThrowsAsync<RedisServerException>(() => TestConnection.ExecuteAsync(
            msg, ResultProcessor.NullableSortedSetEntryArray, RequestResp, "-ERR wrong number of arguments for 'zmovemember' command\r\n", log: log));

        Assert.Equal("ERR wrong number of arguments for 'zmovemember' command", ex.Message);
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var db = Db();
        Assert.Throws<ArgumentNullException>("members", () => db.GetSortedSetMoveMembersMessage("s", "d", null!, When.Always, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>("when", () => db.GetSortedSetMoveMembersMessage("s", "d", ["a"], (When)42, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>("order", () => db.GetSortedSetMoveMessage("s", "d", (Order)42, null, CommandFlags.None));
    }

    [Theory(Timeout = 1000)]
    [InlineData(RedisProtocol.Resp2, TwoPairsResp2)]
    [InlineData(RedisProtocol.Resp3, TwoPairsResp3)]
    public async Task Reply_SameResultInBothProtocols(RedisProtocol protocol, string responseResp)
    {
        var msg = Db().GetSortedSetMoveMessage("s", "d", Order.Ascending, 2, CommandFlags.None);
        const string RequestResp = "*6\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n$5\r\nCOUNT\r\n$1\r\n2\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, RequestResp, responseResp, protocol: protocol, log: log);

        AssertTwoPairs(result);
    }

    [Theory(Timeout = 1000)]
    [InlineData(RedisProtocol.Resp2, "*-1\r\n")]
    [InlineData(RedisProtocol.Resp3, "_\r\n")]
    public async Task Reply_NothingMoved_IsNull(RedisProtocol protocol, string responseResp)
    {
        var msg = Db().GetSortedSetMoveMessage("s", "d", Order.Ascending, null, CommandFlags.None);
        const string RequestResp = "*4\r\n$5\r\nZMOVE\r\n" + Prefix + "$3\r\nMIN\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray, RequestResp, responseResp, protocol: protocol, log: log);

        Assert.Null(result);
    }

    [Fact]
    public void BothAreWritesToThePrimary()
    {
        Assert.True(RedisCommand.ZMOVE.IsPrimaryOnly());
        Assert.True(RedisCommand.ZMOVEMEMBER.IsPrimaryOnly());
    }

    private static void AssertTwoPairs(SortedSetEntry[]? result)
    {
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result[0].Element);
        Assert.Equal(1, result[0].Score);
        Assert.Equal("b", result[1].Element);
        Assert.Equal(2, result[1].Score);
    }
}
