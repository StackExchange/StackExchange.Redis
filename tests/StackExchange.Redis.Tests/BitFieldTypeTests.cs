using System;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Unit tests for the BITFIELD value types; no server required.
/// </summary>
public class BitFieldTypeTests
{
    [Theory]
    [InlineData(1, "i1")]
    [InlineData(8, "i8")]
    [InlineData(53, "i53")]
    [InlineData(64, "i64")]
    public void SignedWidths(int width, string expected)
    {
        var encoding = BitFieldEncoding.Signed(width);
        Assert.Equal(expected, encoding.ToString());
        Assert.Equal(width, encoding.Width);
        Assert.True(encoding.IsSigned);
    }

    [Theory]
    [InlineData(1, "u1")]
    [InlineData(8, "u8")]
    [InlineData(63, "u63")]
    public void UnsignedWidths(int width, string expected)
    {
        var encoding = BitFieldEncoding.Unsigned(width);
        Assert.Equal(expected, encoding.ToString());
        Assert.Equal(width, encoding.Width);
        Assert.False(encoding.IsSigned);
    }

    [Fact]
    public void NamedEncodingsMatchTheirFactories()
    {
        Assert.Equal(BitFieldEncoding.Signed(8), BitFieldEncoding.Int8);
        Assert.Equal(BitFieldEncoding.Signed(16), BitFieldEncoding.Int16);
        Assert.Equal(BitFieldEncoding.Signed(32), BitFieldEncoding.Int32);
        Assert.Equal(BitFieldEncoding.Signed(64), BitFieldEncoding.Int64);
        Assert.Equal(BitFieldEncoding.Unsigned(8), BitFieldEncoding.UInt8);
        Assert.Equal(BitFieldEncoding.Unsigned(16), BitFieldEncoding.UInt16);
        Assert.Equal(BitFieldEncoding.Unsigned(32), BitFieldEncoding.UInt32);
        Assert.Equal(BitFieldEncoding.Unsigned(63), BitFieldEncoding.UInt63);
        Assert.NotEqual(BitFieldEncoding.Int8, BitFieldEncoding.UInt8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65)]
    public void SignedRejectsIllegalWidths(int width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldEncoding.Signed(width));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(64)]
    public void UnsignedRejectsIllegalWidths(int width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldEncoding.Unsigned(width));

    [Fact]
    public void UnsignedSixtyFourExplainsItself()
    {
        // the obvious thing to reach for; the message should say why it cannot work, and what to use
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldEncoding.Unsigned(64));
        Assert.Contains("UInt63", ex.Message);
        Assert.Contains("Int64", ex.Message);
    }

    [Fact]
    public void OffsetForms()
    {
        Assert.Equal("100", BitFieldOffset.Bit(100).ToString());
        Assert.Equal("#100", BitFieldOffset.Element(100).ToString());
        Assert.Equal(BitFieldOffset.Bit(3), (BitFieldOffset)3L); // implicit long is a bit offset
        Assert.NotEqual(BitFieldOffset.Bit(3), BitFieldOffset.Element(3));
    }

    [Fact]
    public void OffsetsCannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldOffset.Bit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldOffset.Element(-1));
    }

    [Fact]
    public void DefaultEncodingIsRejectedAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(() => BitFieldOperation.Get(default, 0));
        Assert.Equal("encoding", ex.ParamName);
    }

    [Fact]
    public void UnknownOverflowIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1, (BitFieldOverflow)42));

    [Fact]
    public void OperationToString()
    {
        Assert.Equal("GET u8 #1", BitFieldOperation.Get(BitFieldEncoding.UInt8, BitFieldOffset.Element(1)).ToString());
        Assert.Equal("SET i16 32 -5 (Saturate)", BitFieldOperation.Set(BitFieldEncoding.Int16, 32, -5, BitFieldOverflow.Saturate).ToString());
        Assert.Equal("INCRBY i8 0 1 (Wrap)", BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 1).ToString());
    }
}
