using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class BitPositionRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 1000)]
    public async Task ExplicitEnd_IsSent()
    {
        const string RequestResp = "*5\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n$2\r\n-1\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.StringBitPositionAsync("k", false, 0, -1, StringIndexType.Byte), RequestResp, ":-1\r\n", log: log);
        Assert.Equal(-1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task ExplicitEnd_WithBitIndexType_SendsToken()
    {
        const string RequestResp = "*6\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n$1\r\n7\r\n$3\r\nBIT\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.StringBitPositionAsync("k", false, 0, 7, StringIndexType.Bit), RequestResp, ":-1\r\n", log: log);
        Assert.Equal(-1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task UnboundedEnd_OmitsEnd()
    {
        const string RequestResp = "*4\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.StringBitPositionAsync("k", false, 0, StringIndex.Unbounded, StringIndexType.Byte), RequestResp, ":8\r\n", log: log);
        Assert.Equal(8, result);
    }

    [Fact(Timeout = 1000)]
    public async Task UnboundedEnd_KeepsStart()
    {
        const string RequestResp = "*4\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n1\r\n$1\r\n2\r\n";

        var result = await RoundTrip.ExecuteAsync(db => db.StringBitPositionAsync("k", true, 2, StringIndex.Unbounded, StringIndexType.Byte), RequestResp, ":16\r\n", log: log);
        Assert.Equal(16, result);
    }

    [Fact]
    public void UnboundedEnd_RejectsBitIndexType()
    {
        var executor = new RoundTripExecutor(":0\r\n");
        var db = RoundTrip.Database(executor);
        var ex = Assert.Throws<ArgumentException>(() => db.StringBitPosition("k", false, 0, StringIndex.Unbounded, StringIndexType.Bit));
        Assert.Equal("indexType", ex.ParamName);
        Assert.Empty(executor.Frames);
    }
}
