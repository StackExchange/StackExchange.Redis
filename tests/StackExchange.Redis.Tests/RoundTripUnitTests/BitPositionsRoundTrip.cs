using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Exact wire bytes for <c>BITRANGEINTARRAY key start end [BYTE|BIT] [LIMIT count] [ZEROS]</c>, as built by
/// <see cref="RedisDatabase.GetStringBitPositionsMessage"/>, plus the reply parsing.
/// </summary>
public class BitPositionsRoundTrip(ITestOutputHelper log)
{
    private const string Cmd = "$16\r\nBITRANGEINTARRAY\r\n$1\r\nk\r\n";
    private const string SampleReply = "*4\r\n:1\r\n:7\r\n:9\r\n:30\r\n";
    private static readonly long[] Sample = [1, 7, 9, 30];

    private static Message Build(bool bit, long start, long end, StringIndexType indexType, long? limit)
        => new RedisDatabase(null!, 0, null).GetStringBitPositionsMessage("k", bit, start, end, indexType, limit, CommandFlags.None);

    [Fact(Timeout = 1000)]
    public async Task Defaults_SendStartAndEnd()
    {
        var msg = Build(true, 0, -1, StringIndexType.Byte, null);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*4\r\n" + Cmd + "$1\r\n0\r\n$2\r\n-1\r\n", SampleReply, log: log);
        Assert.Equal(Sample, result);
    }

    [Fact(Timeout = 1000)]
    public async Task BitIndexType_SendsToken()
    {
        var msg = Build(true, 5, 10, StringIndexType.Bit, null);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*5\r\n" + Cmd + "$1\r\n5\r\n$2\r\n10\r\n$3\r\nBIT\r\n", "*2\r\n:7\r\n:9\r\n", log: log);
        Assert.Equal([7, 9], result);
    }

    [Fact(Timeout = 1000)]
    public async Task Limit_SendsToken()
    {
        var msg = Build(true, 0, -1, StringIndexType.Byte, 1);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*6\r\n" + Cmd + "$1\r\n0\r\n$2\r\n-1\r\n$5\r\nLIMIT\r\n$1\r\n1\r\n", "*2\r\n:1\r\n:7\r\n", log: log);
        Assert.Equal([1, 7], result);
    }

    [Fact(Timeout = 1000)]
    public async Task ClearBits_SendsZeros()
    {
        var msg = Build(false, 24, 31, StringIndexType.Bit, null);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*6\r\n" + Cmd + "$2\r\n24\r\n$2\r\n31\r\n$3\r\nBIT\r\n$5\r\nZEROS\r\n", "*7\r\n:24\r\n:25\r\n:26\r\n:27\r\n:28\r\n:29\r\n:31\r\n", log: log);
        Assert.Equal([24, 25, 26, 27, 28, 29, 31], result);
    }

    [Fact(Timeout = 1000)]
    public async Task ClearBits_ByteDefault_SendsZerosOnly()
    {
        var msg = Build(false, 0, -1, StringIndexType.Byte, null);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*5\r\n" + Cmd + "$1\r\n0\r\n$2\r\n-1\r\n$5\r\nZEROS\r\n", "*1\r\n:0\r\n", log: log);
        Assert.Equal([0], result);
    }

    [Fact(Timeout = 1000)]
    public async Task LimitAndZeros_ByteDefault()
    {
        var msg = Build(false, 0, -1, StringIndexType.Byte, 2);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*7\r\n" + Cmd + "$1\r\n0\r\n$2\r\n-1\r\n$5\r\nLIMIT\r\n$1\r\n2\r\n$5\r\nZEROS\r\n", "*2\r\n:0\r\n:2\r\n", log: log);
        Assert.Equal([0, 2], result);
    }

    [Fact(Timeout = 1000)]
    public async Task BitAndLimit()
    {
        var msg = Build(true, 2, -1, StringIndexType.Bit, 2);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*7\r\n" + Cmd + "$1\r\n2\r\n$2\r\n-1\r\n$3\r\nBIT\r\n$5\r\nLIMIT\r\n$1\r\n2\r\n", "*2\r\n:7\r\n:9\r\n", log: log);
        Assert.Equal([7, 9], result);
    }

    [Fact(Timeout = 1000)]
    public async Task AllOptions_CanonicalOrder()
    {
        var msg = Build(false, 2, -1, StringIndexType.Bit, 2);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*8\r\n" + Cmd + "$1\r\n2\r\n$2\r\n-1\r\n$3\r\nBIT\r\n$5\r\nLIMIT\r\n$1\r\n2\r\n$5\r\nZEROS\r\n", "*2\r\n:2\r\n:3\r\n", log: log);
        Assert.Equal([2, 3], result);
    }

    [Fact(Timeout = 1000)]
    public async Task Limit_Zero_IsSent()
    {
        var msg = Build(true, 0, -1, StringIndexType.Byte, 0);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, "*6\r\n" + Cmd + "$1\r\n0\r\n$2\r\n-1\r\n$5\r\nLIMIT\r\n$1\r\n0\r\n", "*0\r\n", log: log);
        Assert.Empty(result);
    }

    [Fact]
    public void Limit_Negative_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Build(true, 0, -1, StringIndexType.Byte, -1));
        Assert.Equal("limit", ex.ParamName);
    }

    [Fact]
    public void IndexType_Undefined_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Build(true, 0, -1, (StringIndexType)42, null));
        Assert.Equal("indexType", ex.ParamName);
    }
}
