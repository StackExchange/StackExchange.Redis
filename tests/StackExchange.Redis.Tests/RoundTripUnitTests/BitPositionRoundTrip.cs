using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class BitPositionRoundTrip(ITestOutputHelper log)
{
    [Fact(Timeout = 1000)]
    public async Task ExplicitEnd_IsSent()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetStringBitPositionMessage("k", false, 0, -1, StringIndexType.Byte, CommandFlags.None);
        const string RequestResp = "*5\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n$2\r\n-1\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, RequestResp, ":-1\r\n", log: log);
        Assert.Equal(-1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task ExplicitEnd_WithBitIndexType_SendsToken()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetStringBitPositionMessage("k", false, 0, 7, StringIndexType.Bit, CommandFlags.None);
        const string RequestResp = "*6\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n$1\r\n7\r\n$3\r\nBIT\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, RequestResp, ":-1\r\n", log: log);
        Assert.Equal(-1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task UnboundedEnd_OmitsEnd()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetStringBitPositionMessage("k", false, 0, StringIndex.Unbounded, StringIndexType.Byte, CommandFlags.None);
        const string RequestResp = "*4\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n0\r\n$1\r\n0\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, RequestResp, ":8\r\n", log: log);
        Assert.Equal(8, result);
    }

    [Fact(Timeout = 1000)]
    public async Task UnboundedEnd_KeepsStart()
    {
        var db = new RedisDatabase(null!, 0, null);
        var msg = db.GetStringBitPositionMessage("k", true, 2, StringIndex.Unbounded, StringIndexType.Byte, CommandFlags.None);
        const string RequestResp = "*4\r\n$6\r\nBITPOS\r\n$1\r\nk\r\n$1\r\n1\r\n$1\r\n2\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64, RequestResp, ":16\r\n", log: log);
        Assert.Equal(16, result);
    }

    [Fact]
    public void UnboundedEnd_RejectsBitIndexType()
    {
        var db = new RedisDatabase(null!, 0, null);
        var ex = Assert.Throws<ArgumentException>(() => db.GetStringBitPositionMessage("k", false, 0, StringIndex.Unbounded, StringIndexType.Bit, CommandFlags.None));
        Assert.Equal("indexType", ex.ParamName);
    }
}
