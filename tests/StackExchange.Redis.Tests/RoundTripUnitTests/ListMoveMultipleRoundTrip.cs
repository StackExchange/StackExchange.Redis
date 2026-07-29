using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class ListMoveMultipleRoundTrip(ITestOutputHelper log)
{
    // builds the message exactly as RedisDatabase.ListMove(... count ...) does, and asserts the wire bytes.
    private static Message CreateMessage(ListSide from, ListSide to, long count, ListMoveCount mode, ListMoveOrder order) =>
        Message.Create(
            0,
            CommandFlags.None,
            RedisCommand.LMOVEM,
            (RedisKey)"s",
            (RedisKey)"d",
            from.ToLiteral(),
            to.ToLiteral(),
            mode.ToLiteral(),
            count,
            order.ToLiteral());

    [Fact(Timeout = 1000)]
    public async Task UpTo_Bulk_RoundTrips()
    {
        var msg = CreateMessage(ListSide.Left, ListSide.Right, 2, ListMoveCount.UpTo, ListMoveOrder.Bulk);
        const string requestResp =
            "*8\r\n$6\r\nLMOVEM\r\n$1\r\ns\r\n$1\r\nd\r\n$4\r\nLEFT\r\n$5\r\nRIGHT\r\n$5\r\nCOUNT\r\n$1\r\n2\r\n$4\r\nBULK\r\n";

        var result = await TestConnection.ExecuteAsync(
            msg, ResultProcessor.NullableRedisValueArray, requestResp, "*2\r\n$1\r\na\r\n$1\r\nb\r\n", log: log);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result[0].ToString());
        Assert.Equal("b", result[1].ToString());
    }

    [Fact(Timeout = 1000)]
    public async Task Exactly_OneByOne_NotSatisfied_RoundTripsNull()
    {
        var msg = CreateMessage(ListSide.Right, ListSide.Left, 3, ListMoveCount.Exactly, ListMoveOrder.OneByOne);
        const string requestResp =
            "*8\r\n$6\r\nLMOVEM\r\n$1\r\ns\r\n$1\r\nd\r\n$5\r\nRIGHT\r\n$4\r\nLEFT\r\n$7\r\nEXACTLY\r\n$1\r\n3\r\n$3\r\nOBO\r\n";

        var result = await TestConnection.ExecuteAsync(
            msg, ResultProcessor.NullableRedisValueArray, requestResp, "*-1\r\n", log: log);

        Assert.Null(result);
    }
}
