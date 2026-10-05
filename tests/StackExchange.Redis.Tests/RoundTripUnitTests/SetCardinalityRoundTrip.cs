using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class SetCardinalityRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 1000)]
    public async Task SDiffCard_NoLimit_RoundTrips()
    {
        // the old core omitted LIMIT when the limit was 0 ("*4\r\n...s2\r\n"); the new core passes IDatabase's
        // default of 0 through as an explicit "LIMIT 0". That is still valid - LIMIT 0 means "no limit", as it
        // does for SINTERCARD - but it is two extra arguments, and a negative limit is no longer swallowed
        const string requestResp = "*6\r\n$9\r\nSDIFFCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$5\r\nLIMIT\r\n$1\r\n0\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Difference, ["s1", "s2"]), requestResp, ":2\r\n", log: log);
        Assert.Equal(2, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SUnionCard_WithLimit_RoundTrips()
    {
        const string requestResp = "*6\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Union, ["s1", "s2"], 3), requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SUnionCard_ApproxWithLimit_RoundTrips()
    {
        // APPROX is written before LIMIT
        const string requestResp = "*7\r\n$10\r\nSUNIONCARD\r\n$1\r\n2\r\n$2\r\ns1\r\n$2\r\ns2\r\n$6\r\nAPPROX\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.SetCombineLengthAsync(SetOperation.Union, ["s1", "s2"], 3, approximate: true), requestResp, ":3\r\n", log: log);
        Assert.Equal(3, result);
    }
}
