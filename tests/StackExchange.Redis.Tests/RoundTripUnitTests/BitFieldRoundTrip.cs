using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class BitFieldRoundTrip(ITestOutputHelper log)
{
    private static Message Batch(RedisCommand command, params BitFieldOperation[] operations) =>
        new RedisDatabase.BitFieldMessage(0, CommandFlags.None, command, (RedisKey)"k", operations);

    private static Message Single(RedisCommand command, BitFieldOperation operation) =>
        new RedisDatabase.BitFieldSingleMessage(0, CommandFlags.None, command, (RedisKey)"k", operation);

    [Fact(Timeout = 1000)]
    public async Task Increment_RoundTrips()
    {
        var msg = Batch(RedisCommand.BITFIELD, BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1));
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$1\r\n1\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*1\r\n:1\r\n", log: log);
        Assert.Equal(new long?[] { 1 }, result.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task SingleOperation_WritesTheSameAsAUnitBatch()
    {
        // the single-operation overload avoids the array, but must produce identical bytes
        var msg = Single(RedisCommand.BITFIELD, BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1));
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$1\r\n1\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.NullableInt64, RequestResp, "*1\r\n:1\r\n", log: log);
        Assert.Equal(1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task AllGet_UsesReadOnlyCommand()
    {
        var msg = Batch(
            RedisCommand.BITFIELD_RO,
            BitFieldOperation.Get(BitFieldEncoding.UInt8, 0),
            BitFieldOperation.Get(BitFieldEncoding.UInt63, BitFieldOffset.Element(2)));
        const string RequestResp = "*8\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n$1\r\n0\r\n$3\r\nGET\r\n$3\r\nu63\r\n$2\r\n#2\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*2\r\n:255\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 255, 0 }, result.Span.ToArray());
    }

    [Theory(Timeout = 1000)]
    [InlineData(1, "$2\r\ni1\r\n")]
    [InlineData(9, "$2\r\ni9\r\n")]
    [InlineData(10, "$3\r\ni10\r\n")]
    [InlineData(64, "$3\r\ni64\r\n")]
    public async Task EncodingWidths_AreFramedCorrectly(int width, string encodingResp)
    {
        // the width is 1-64, so the length prefix is always one digit and the whole bulk string is
        // written in one go
        var msg = Batch(RedisCommand.BITFIELD_RO, BitFieldOperation.Get(BitFieldEncoding.Signed(width), 0));
        var requestResp = "*5\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n" + encodingResp + "$1\r\n0\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, requestResp, "*1\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 0 }, result.Span.ToArray());
    }

    [Theory(Timeout = 1000)]
    [InlineData(0, "$2\r\n#0\r\n")]
    [InlineData(9, "$2\r\n#9\r\n")]
    [InlineData(1234567890123, "$14\r\n#1234567890123\r\n")]
    public async Task ElementOffsets_AreFramedCorrectly(long element, string offsetResp)
    {
        var msg = Batch(RedisCommand.BITFIELD_RO, BitFieldOperation.Get(BitFieldEncoding.UInt8, BitFieldOffset.Element(element)));
        var requestResp = "*5\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n" + offsetResp;

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, requestResp, "*1\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 0 }, result.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task LeadingWrap_IsNotEmitted()
    {
        // WRAP is the server default, so there is nothing to say
        var msg = Batch(RedisCommand.BITFIELD, BitFieldOperation.Set(BitFieldEncoding.Int64, 0, 5, BitFieldOverflow.Wrap));
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$3\r\nSET\r\n$3\r\ni64\r\n$1\r\n0\r\n$1\r\n5\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*1\r\n:0\r\n", log: log);
        Assert.Equal(new long?[] { 0 }, result.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_IsEmittedOnlyWhenItChanges()
    {
        var msg = Batch(
            RedisCommand.BITFIELD,
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Saturate),
            BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, BitFieldOffset.Element(1), 2, BitFieldOverflow.Saturate),
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 3, BitFieldOverflow.Wrap));
        const string RequestResp = "*18\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$3\r\nSAT\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n1\r\n"
            + "$6\r\nINCRBY\r\n$2\r\nu8\r\n$2\r\n#1\r\n$1\r\n2\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nWRAP\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n3\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*3\r\n:0\r\n:2\r\n:1\r\n", log: log);
        Assert.Equal(new long?[] { 0, 2, 1 }, result.Span.ToArray());
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_SurvivesAnInterveningGet_AndFailReportsNull()
    {
        // the sticky OVERFLOW state is not reset or consumed by a GET, so the second INCRBY needs no token
        var msg = Batch(
            RedisCommand.BITFIELD,
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail),
            BitFieldOperation.Get(BitFieldEncoding.Int8, 0),
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail));
        const string RequestResp = "*15\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nFAIL\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n"
            + "$3\r\nGET\r\n$2\r\ni8\r\n$1\r\n0\r\n"
            + "$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n";

        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.LeaseNullableInt64, RequestResp, "*3\r\n:100\r\n:100\r\n$-1\r\n", log: log);
        Assert.Equal(new long?[] { 100, 100, null }, result.Span.ToArray());
    }

    [Fact]
    public void ArgCountMatchesTheOperations()
    {
        // key + (SET, enc, off, value)
        Assert.Equal(5, Batch(RedisCommand.BITFIELD, BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1)).ArgCount);
        Assert.Equal(5, Single(RedisCommand.BITFIELD, BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1)).ArgCount);

        // key + (GET, enc, off)
        Assert.Equal(4, Batch(RedisCommand.BITFIELD_RO, BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)).ArgCount);

        // key + (OVERFLOW, mode) + 2x(INCRBY, enc, off, value)
        Assert.Equal(
            11,
            Batch(
                RedisCommand.BITFIELD,
                BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Fail),
                BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Fail)).ArgCount);
    }

    [Fact]
    public void DefaultOperation_IsRejectedBeforeTheMessageIsQueued()
    {
        var batch = Assert.Throws<ArgumentException>(() => Batch(RedisCommand.BITFIELD, default(BitFieldOperation)));
        Assert.Equal("operations", batch.ParamName);

        var single = Assert.Throws<ArgumentException>(() => Single(RedisCommand.BITFIELD, default(BitFieldOperation)));
        Assert.Equal("operation", single.ParamName);
    }

    [Fact]
    public void MutatingTheOperationsAfterIssue_FailsBeforeAnythingIsWritten()
    {
        // the batch form aliases the caller's memory rather than copying it, and the shape depends on
        // the contents, so a mutation has to be caught before the header goes out
        var operations = new[] { BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1) };
        var msg = new RedisDatabase.BitFieldMessage(0, CommandFlags.None, RedisCommand.BITFIELD, (RedisKey)"k", operations);

        operations[0] = default;

        using var conn = new TestConnection(startReading: false);
        var box = TaskResultBox<long?>.Create(out _, null);
        msg.SetSource(box, ResultProcessor.NullableInt64);

        Assert.Throws<InvalidOperationException>(() => conn.WriteOutbound(msg));
        Assert.Empty(conn.GetOutboundData().ToArray());
    }

    [Theory]
    // all-GET: no side effects to replay, whichever command we end up issuing
    [InlineData(true, false, true, "BITFIELD_RO", CommandFlags.CommandRetryReadOnly)]
    [InlineData(true, false, false, "BITFIELD", CommandFlags.CommandRetryReadOnly)]
    // SET only: a replay lands on the same value, because the offset is positional
    [InlineData(false, false, false, "BITFIELD", CommandFlags.CommandRetryWriteLastWins)]
    // anything with an INCRBY compounds, so it keeps BITFIELD's accumulating default
    [InlineData(false, true, false, "BITFIELD", CommandFlags.None)]
    public void CommandAndRetryCategoryFollowThePayload(bool allGet, bool anyIncrement, bool readOnlyAvailable, string expectedCommand, CommandFlags expectedCategory)
    {
        var flags = CommandFlags.None;
        Assert.Equal(expectedCommand, RedisDatabase.SelectBitFieldCommand(allGet, anyIncrement, readOnlyAvailable, ref flags).ToString());

        // CommandFlags.None here means "no opinion", leaving BITFIELD's own accumulating default in place
        Assert.Equal(expectedCategory, flags & Message.MaskRetryCategory);
    }

    [Fact]
    public void AnExplicitRetryCategoryIsNotOverridden()
    {
        var flags = CommandFlags.CommandRetryNever;
        Assert.Equal(RedisCommand.BITFIELD_RO, RedisDatabase.SelectBitFieldCommand(allGet: true, anyIncrement: false, readOnlyAvailable: true, ref flags));
        Assert.Equal(CommandFlags.CommandRetryNever, flags & Message.MaskRetryCategory);
    }

    [Fact]
    public void ReadOnlyVariantIsNotPrimaryOnly()
    {
        // BITFIELD is a write server-side even when every sub-operation is a GET; BITFIELD_RO is not
        Assert.True(RedisCommand.BITFIELD.IsPrimaryOnly());
        Assert.False(RedisCommand.BITFIELD_RO.IsPrimaryOnly());
    }
}
