using System;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The buffer arithmetic, asserted directly rather than by writing and hoping something notices.
/// </summary>
/// <remarks>
/// Writing a payload and checking the result cannot prove this: the handler rents from
/// <c>ArrayPool&lt;byte&gt;.Shared</c>, which rounds up to a power of two, so an under-reservation lands in
/// slack and the test passes anyway. That is exactly how the original defect hid - it only surfaces above
/// 2^30, where the pool starts handing back arrays of exactly the requested length. So assert the invariant
/// itself, at every digit-count boundary, where it is cheap and cannot be masked.
/// </remarks>
public class InterpolatedWriterCapacityTests
{
    /// <summary>The bytes a <c>$len\r\n{payload}\r\n</c> bulk string actually occupies.</summary>
    private static long ActualBulkLength(int payloadLength)
        => 1 + payloadLength.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 2 + (long)payloadLength + 2;

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(999_999_999)]          // 9 digits - the old reservation's unstated assumption
    [InlineData(1_000_000_000)]        // 10 digits - one byte short before the fix, masked by pool slack
    [InlineData(1_073_741_825)]        // just above 2^30, where the pool stops rounding up and it bites
    [InlineData(int.MaxValue - 64)]
    public void ReservationCoversTheBytesActuallyWritten(int payloadLength)
    {
        long reserved = RespRequestBuilder.BulkReservation(payloadLength);
        Assert.True(
            reserved >= ActualBulkLength(payloadLength),
            $"reserved {reserved} for a payload of {payloadLength}, which needs {ActualBulkLength(payloadLength)}");
    }

    /// <summary>The reservation must not overflow into a negative for a plausible large payload.</summary>
    [Fact]
    public void ReservationDoesNotOverflowForLargePayloads()
        => Assert.True(RespRequestBuilder.BulkReservation(int.MaxValue - 64) > 0);
}
