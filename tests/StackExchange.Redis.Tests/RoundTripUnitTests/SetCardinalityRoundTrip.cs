using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class SetCardinalityRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 10000)]
    public async Task SDiffCard_NoLimit_RoundTrips()
    {
        // IDatabase's default limit of 0 means "no limit", and is omitted rather than sent as LIMIT 0
        const string requestResp = "*4\r\n$9\r\nSDIFFCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Difference, ["s1", "s2"]), requestResp, ":2\r\n", log: log);
        Assert.Equal(2, result);
    }

    [Fact(Timeout = 10000)]
    public async Task SUnionCard_WithLimit_RoundTrips()
    {
        const string requestResp = "*6\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Union, ["s1", "s2"], 3), requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }

    [Fact(Timeout = 10000)]
    public async Task SUnionCard_ApproxWithLimit_RoundTrips()
    {
        // APPROX is written before LIMIT
        const string requestResp = "*7\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$6\r\nAPPROX\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Union, ["s1", "s2"], 3, approximate: true), requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }
}
