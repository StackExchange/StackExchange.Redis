using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class BitFieldRoundTrip(ITestOutputHelper log)
{
    private static Message Message(RedisCommand command, params BitFieldOperation[] operations)
    {
        var payload = RedisDatabase.BuildBitFieldPayload(operations, out _, out _);
        return StackExchange.Redis.Message.Create(0, CommandFlags.None, command, (RedisKey)"k", payload);
    }

    [Fact(Timeout = 1000)]
    public async Task Increment_RoundTrips()
    {
        var msg = Message(RedisCommand.BITFIELD, BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1));
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$1\r\n1\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*1\r\n:1\r\n", log: log);
        Assert.Equal(new long?[] { 1 }, result!.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task AllGet_UsesReadOnlyCommand()
    {
        var msg = Message(
            RedisCommand.BITFIELD_RO,
            BitFieldOperation.Get(BitFieldEncoding.UInt8, 0),
            BitFieldOperation.Get(BitFieldEncoding.UInt63, BitFieldOffset.Index(2)));
        const string RequestResp = "*8\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n$1\r\n0\r\n$3\r\nGET\r\n$3\r\nu63\r\n$2\r\n#2\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*2\r\n:255\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 255, 0 }, result!.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task LeadingWrap_IsNotEmitted()
    {
        // WRAP is the server default, so there is nothing to say
        var msg = Message(RedisCommand.BITFIELD, BitFieldOperation.Set(BitFieldEncoding.Int64, 0, 5, BitFieldOverflow.Wrap));
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$3\r\nSET\r\n$3\r\ni64\r\n$1\r\n0\r\n$1\r\n5\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*1\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 0 }, result!.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_IsEmittedOnlyWhenItChanges()
    {
        var msg = Message(
            RedisCommand.BITFIELD,
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Saturate),
            BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, BitFieldOffset.Index(1), 2, BitFieldOverflow.Saturate),
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 3, BitFieldOverflow.Wrap));
        const string RequestResp = "*18\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$3\r\nSAT\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n1\r\n"
            + "$6\r\nINCRBY\r\n$2\r\nu8\r\n$2\r\n#1\r\n$1\r\n2\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nWRAP\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n3\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*3\r\n:0\r\n:2\r\n:1\r\n", log: log);
        Assert.Equal(new long?[] { 0, 2, 1 }, result!.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_SurvivesAnInterveningGet_AndFailReportsNull()
    {
        // the sticky OVERFLOW state is not reset or consumed by a GET, so the second INCRBY needs no token
        var msg = Message(
            RedisCommand.BITFIELD,
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail),
            BitFieldOperation.Get(BitFieldEncoding.Int8, 0),
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail));
        const string RequestResp = "*15\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nFAIL\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n"
            + "$3\r\nGET\r\n$2\r\ni8\r\n$1\r\n0\r\n"
            + "$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*3\r\n:100\r\n:100\r\n$-1\r\n", log: log);
        Assert.Equal(new long?[] { 100, 100, null }, result!.Span.ToArray());
    }

    [Fact]
    public void Classification_ReflectsTheOperations()
    {
        RedisDatabase.BuildBitFieldPayload([BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)], out var allGet, out var anyIncrement);
        Assert.True(allGet);
        Assert.False(anyIncrement);

        RedisDatabase.BuildBitFieldPayload(
            [BitFieldOperation.Get(BitFieldEncoding.UInt8, 0), BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1)],
            out allGet,
            out anyIncrement);
        Assert.False(allGet);
        Assert.False(anyIncrement); // SET only: eligible for last-wins retry

        RedisDatabase.BuildBitFieldPayload(
            [BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1), BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, 0, 1)],
            out allGet,
            out anyIncrement);
        Assert.False(allGet);
        Assert.True(anyIncrement);
    }

    [Fact]
    public void DefaultOperation_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => RedisDatabase.BuildBitFieldPayload(new BitFieldOperation[1], out _, out _));
        Assert.Equal("operations", ex.ParamName);
    }

    [Fact]
    public void ReadOnlyVariantIsNotPrimaryOnly()
    {
        // BITFIELD is a write server-side even when every sub-operation is a GET; BITFIELD_RO is not
        Assert.True(RedisCommand.BITFIELD.IsPrimaryOnly());
        Assert.False(RedisCommand.BITFIELD_RO.IsPrimaryOnly());
    }
}
