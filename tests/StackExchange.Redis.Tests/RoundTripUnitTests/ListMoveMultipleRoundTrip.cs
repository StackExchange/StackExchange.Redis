using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class ListMoveMultipleRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 10000)]
    public async Task UpTo_Bulk_RoundTrips()
    {
        const string requestResp =
            "*8\r\n$6\r\nLMOVEM\r\n$1\r\ns\r\n$1\r\nd\r\n$4\r\nLEFT\r\n$5\r\nRIGHT\r\n$5\r\nCOUNT\r\n$1\r\n2\r\n$4\r\nBULK\r\n";

        var result = await RoundTrip.ExecuteAsync(
            db => db.ListMoveAsync("s", "d", ListSide.Left, ListSide.Right, 2, ListMoveCount.UpTo, ListMoveOrder.Bulk),
            requestResp,
            "*2\r\n$1\r\na\r\n$1\r\nb\r\n",
            log: log);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result[0].ToString());
        Assert.Equal("b", result[1].ToString());
    }

    [Fact(Timeout = 10000)]
    public async Task Exactly_OneByOne_NotSatisfied_RoundTripsNull()
    {
        const string requestResp =
            "*8\r\n$6\r\nLMOVEM\r\n$1\r\ns\r\n$1\r\nd\r\n$5\r\nRIGHT\r\n$4\r\nLEFT\r\n$7\r\nEXACTLY\r\n$1\r\n3\r\n$3\r\nOBO\r\n";

        var result = await RoundTrip.ExecuteAsync(
            db => db.ListMoveAsync("s", "d", ListSide.Right, ListSide.Left, 3, ListMoveCount.Exactly, ListMoveOrder.OneByOne),
            requestResp,
            "*-1\r\n",
            log: log);

        Assert.Null(result);
    }
}
