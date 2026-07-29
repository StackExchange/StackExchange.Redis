using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class SetCardinalityRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 1000)]
    public async Task SDiffCard_NoLimit_RoundTrips()
    {
        var msg = new SetOperationCardinalityMessage(0, CommandFlags.None, RedisCommand.SDIFFCARD, ["s1", "s2"], 0, approximate: false);
        const string requestResp = "*4\r\n$9\r\nSDIFFCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, requestResp, ":2\r\n", log: log);
        Assert.Equal(2, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SUnionCard_WithLimit_RoundTrips()
    {
        var msg = new SetOperationCardinalityMessage(0, CommandFlags.None, RedisCommand.SUNIONCARD, ["s1", "s2"], 3, approximate: false);
        const string requestResp = "*6\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SUnionCard_ApproxWithLimit_RoundTrips()
    {
        // APPROX is written before LIMIT
        var msg = new SetOperationCardinalityMessage(0, CommandFlags.None, RedisCommand.SUNIONCARD, ["s1", "s2"], 3, approximate: true);
        const string requestResp = "*7\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$6\r\nAPPROX\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }
}
