using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class BitFieldRoundTrip(ITestOutputHelper log)
{
    /// <summary>A server new enough for <c>BITFIELD_RO</c>; without one, an all-GET payload sends <c>BITFIELD</c>.</summary>
    private static readonly RedisFeatures Modern = new(new Version(7, 0, 0));

    private static async Task<long?[]> Batch(string requestResp, string responseResp, ITestOutputHelper log, params BitFieldOperation[] operations)
    {
        using var lease = await RoundTrip.ExecuteAsync(
            db => db.StringBitFieldAsync("k", operations),
            requestResp,
            responseResp,
            log: log,
            features: Modern);
        return lease.Span.ToArray();
    }

    [Fact(Timeout = 1000)]
    public async Task Increment_RoundTrips()
    {
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$1\r\n1\r\n";

        var result = await Batch(RequestResp, "*1\r\n:1\r\n", log, BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1));
        Assert.Equal(new long?[] { 1 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task SingleOperation_WritesTheSameAsAUnitBatch()
    {
        // the single-operation overload avoids the array, but must produce identical bytes
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$1\r\n1\r\n";

        var result = await RoundTrip.ExecuteAsync(
            db => db.StringBitFieldAsync("k", BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1)),
            RequestResp,
            "*1\r\n:1\r\n",
            log: log);
        Assert.Equal(1, result);
    }

    [Fact(Timeout = 1000)]
    public async Task AllGet_UsesReadOnlyCommand()
    {
        const string RequestResp = "*8\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n$1\r\n0\r\n$3\r\nGET\r\n$3\r\nu63\r\n$2\r\n#2\r\n";

        var result = await Batch(
            RequestResp,
            "*2\r\n:255\r\n:0\r\n",
            log,
            BitFieldOperation.Get(BitFieldEncoding.UInt8, 0),
            BitFieldOperation.Get(BitFieldEncoding.UInt63, BitFieldOffset.Element(2)));
        Assert.Equal(new long?[] { 255, 0 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task AllGet_WithoutKnownFeatures_UsesTheWritableCommand()
    {
        // nothing known about the server: BITFIELD works everywhere, BITFIELD_RO only from 6.0
        var result = await RoundTrip.ExecuteAsync(
            db => db.StringBitFieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)),
            "*5\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n$1\r\n0\r\n",
            "*1\r\n:7\r\n",
            log: log);
        Assert.Equal(7, result);
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
        var requestResp = "*5\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n" + encodingResp + "$1\r\n0\r\n";

        var result = await Batch(requestResp, "*1\r\n:0\r\n", log, BitFieldOperation.Get(BitFieldEncoding.Signed(width), 0));
        Assert.Equal(new long?[] { 0 }, result);
    }

    [Theory(Timeout = 1000)]
    [InlineData(0, "$2\r\n#0\r\n")]
    [InlineData(9, "$2\r\n#9\r\n")]
    [InlineData(1234567890123, "$14\r\n#1234567890123\r\n")]
    public async Task ElementOffsets_AreFramedCorrectly(long element, string offsetResp)
    {
        var requestResp = "*5\r\n$11\r\nBITFIELD_RO\r\n$1\r\nk\r\n$3\r\nGET\r\n$2\r\nu8\r\n" + offsetResp;

        var result = await Batch(requestResp, "*1\r\n:0\r\n", log, BitFieldOperation.Get(BitFieldEncoding.UInt8, BitFieldOffset.Element(element)));
        Assert.Equal(new long?[] { 0 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task LeadingWrap_IsNotEmitted()
    {
        // WRAP is the server default, so there is nothing to say
        const string RequestResp = "*6\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n$3\r\nSET\r\n$3\r\ni64\r\n$1\r\n0\r\n$1\r\n5\r\n";

        var result = await Batch(RequestResp, "*1\r\n:0\r\n", log, BitFieldOperation.Set(BitFieldEncoding.Int64, 0, 5, BitFieldOverflow.Wrap));
        Assert.Equal(new long?[] { 0 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_IsEmittedOnlyWhenItChanges()
    {
        const string RequestResp = "*18\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$3\r\nSAT\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n1\r\n"
            + "$6\r\nINCRBY\r\n$2\r\nu8\r\n$2\r\n#1\r\n$1\r\n2\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nWRAP\r\n$3\r\nSET\r\n$2\r\nu8\r\n$1\r\n0\r\n$1\r\n3\r\n";

        var result = await Batch(
            RequestResp,
            "*3\r\n:0\r\n:2\r\n:1\r\n",
            log,
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Saturate),
            BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, BitFieldOffset.Element(1), 2, BitFieldOverflow.Saturate),
            BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 3, BitFieldOverflow.Wrap));
        Assert.Equal(new long?[] { 0, 2, 1 }, result);
    }

    [Fact(Timeout = 1000)]
    public async Task Overflow_SurvivesAnInterveningGet_AndFailReportsNull()
    {
        // the sticky OVERFLOW state is not reset or consumed by a GET, so the second INCRBY needs no token
        const string RequestResp = "*15\r\n$8\r\nBITFIELD\r\n$1\r\nk\r\n"
            + "$8\r\nOVERFLOW\r\n$4\r\nFAIL\r\n$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n"
            + "$3\r\nGET\r\n$2\r\ni8\r\n$1\r\n0\r\n"
            + "$6\r\nINCRBY\r\n$2\r\ni8\r\n$1\r\n0\r\n$3\r\n100\r\n";

        var result = await Batch(
            RequestResp,
            "*3\r\n:100\r\n:100\r\n$-1\r\n",
            log,
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail),
            BitFieldOperation.Get(BitFieldEncoding.Int8, 0),
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail));
        Assert.Equal(new long?[] { 100, 100, null }, result);
    }

    [Fact]
    public void ArgCountMatchesTheOperations()
    {
        // key + (SET, enc, off, value)
        Assert.Equal(5, BitFieldOperation.CountArgs([BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1)], "operations"));
        Assert.Equal(5, BitFieldOperation.CountArgs(BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1), "operation"));

        // key + (GET, enc, off)
        Assert.Equal(4, BitFieldOperation.CountArgs([BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)], "operations"));

        // key + (OVERFLOW, mode) + 2x(INCRBY, enc, off, value)
        Assert.Equal(
            11,
            BitFieldOperation.CountArgs(
                [
                    BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Fail),
                    BitFieldOperation.IncrementBy(BitFieldEncoding.UInt8, 0, 1, BitFieldOverflow.Fail),
                ],
                "operations"));
    }

    [Fact]
    public void DefaultOperation_IsRejectedBeforeAnythingIsSent()
    {
        var executor = new RoundTripExecutor("*1\r\n:0\r\n");
        var db = RoundTrip.Database(executor);

        var batch = Assert.Throws<ArgumentException>(() => db.StringBitField("k", new[] { default(BitFieldOperation) }));
        Assert.Equal("operations", batch.ParamName);

        var single = Assert.Throws<ArgumentException>(() => db.StringBitField("k", default(BitFieldOperation)));
        Assert.Equal("operation", single.ParamName);

        Assert.Empty(executor.Frames);
    }

    // MutatingTheOperationsAfterIssue_FailsBeforeAnythingIsWritten is gone: the old BitFieldMessage held the
    // caller's array until the writer reached it, so a mutation in between could corrupt the frame. The new
    // surface renders the frame synchronously inside the call, so there is no window to mutate in.

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
        Assert.Equal(expectedCommand, Bitmaps.SelectBitFieldCommand(allGet, anyIncrement, readOnlyAvailable, ref flags).ToString());

        // CommandFlags.None here means "no opinion", leaving BITFIELD's own accumulating default in place
        Assert.Equal(expectedCategory, flags & CommandFlagsInternal.MaskRetryCategory);
    }

    [Fact]
    public void AnExplicitRetryCategoryIsNotOverridden()
    {
        var flags = CommandFlags.CommandRetryNever;
        Assert.Equal(RedisCommand.BITFIELD_RO, Bitmaps.SelectBitFieldCommand(allGet: true, anyIncrement: false, readOnlyAvailable: true, ref flags));
        Assert.Equal(CommandFlags.CommandRetryNever, flags & CommandFlagsInternal.MaskRetryCategory);
    }

    [Fact]
    public void ReadOnlyVariantIsNotPrimaryOnly()
    {
        // BITFIELD is a write server-side even when every sub-operation is a GET; BITFIELD_RO is not
        Assert.True(RedisCommand.BITFIELD.IsPrimaryOnly());
        Assert.False(RedisCommand.BITFIELD_RO.IsPrimaryOnly());
    }
}
