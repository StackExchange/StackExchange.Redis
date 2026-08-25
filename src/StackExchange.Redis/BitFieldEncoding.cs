using System;

namespace StackExchange.Redis;

/// <summary>
/// The width and signedness of a single bitfield, as used by the <c>BITFIELD</c> operations
/// on <see cref="IDatabase"/>; for example <c>u8</c> or <c>i53</c>.
/// </summary>
/// <remarks>
/// Signed encodings may be 1-64 bits wide; unsigned encodings are limited to 63 bits, because the
/// server reports every value as a signed 64-bit integer. Every legal encoding therefore fits
/// losslessly in <see cref="long"/>.
/// </remarks>
public readonly struct BitFieldEncoding : IEquatable<BitFieldEncoding>
{
    // negative: signed; positive: unsigned; zero: default (not a legal encoding)
    private readonly sbyte _value;

    private BitFieldEncoding(sbyte value) => _value = value;

    /// <summary>A signed bitfield <paramref name="width"/> bits wide; 1-64.</summary>
    /// <param name="width">The width of the field, in bits.</param>
    public static BitFieldEncoding Signed(int width)
    {
        if (width is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Signed bitfield encodings must be 1-64 bits wide.");
        }

        return new((sbyte)-width);
    }

    /// <summary>An unsigned bitfield <paramref name="width"/> bits wide; 1-63.</summary>
    /// <param name="width">The width of the field, in bits.</param>
    public static BitFieldEncoding Unsigned(int width)
    {
        if (width is < 1 or > 63)
        {
            // the ceiling is 63 rather than 64 because every value comes back as a signed 64-bit
            // integer, so u64 has nowhere to go; the server rejects it for the same reason
            var message = width >= 64
                ? "Unsigned bitfield encodings are limited to 63 bits, because the reply is a signed 64-bit integer; use UInt63 or Int64."
                : "Unsigned bitfield encodings must be 1-63 bits wide.";
            throw new ArgumentOutOfRangeException(nameof(width), message);
        }

        return new((sbyte)width);
    }

    /// <summary>A signed 8-bit field, <c>i8</c>.</summary>
    public static BitFieldEncoding Int8 => new(-8);

    /// <summary>A signed 16-bit field, <c>i16</c>.</summary>
    public static BitFieldEncoding Int16 => new(-16);

    /// <summary>A signed 32-bit field, <c>i32</c>.</summary>
    public static BitFieldEncoding Int32 => new(-32);

    /// <summary>A signed 64-bit field, <c>i64</c>; the widest field the server supports.</summary>
    public static BitFieldEncoding Int64 => new(-64);

    /// <summary>An unsigned 8-bit field, <c>u8</c>.</summary>
    public static BitFieldEncoding UInt8 => new(8);

    /// <summary>An unsigned 16-bit field, <c>u16</c>.</summary>
    public static BitFieldEncoding UInt16 => new(16);

    /// <summary>An unsigned 32-bit field, <c>u32</c>.</summary>
    public static BitFieldEncoding UInt32 => new(32);

    /// <summary>
    /// An unsigned 63-bit field, <c>u63</c>; the widest unsigned field the server supports. There is
    /// deliberately no <c>UInt64</c>: the reply is a signed 64-bit integer, so <c>u64</c> could not be
    /// represented, and the server rejects it.
    /// </summary>
    public static BitFieldEncoding UInt63 => new(63);

    /// <summary>The width of the field, in bits.</summary>
    public int Width => _value < 0 ? -_value : _value;

    /// <summary>Whether the field is signed.</summary>
    public bool IsSigned => _value < 0;

    internal bool IsDefault => _value == 0;

    /// <summary>
    /// Writes this encoding as a complete bulk string - <c>$2\r\ni8\r\n</c> or <c>$3\r\ni64\r\n</c> - since
    /// the width is 1-64 and so the length prefix is always a single digit.
    /// </summary>
    internal void Write(in MessageWriter writer, Span<byte> scratch)
    {
        if (_value == 0) ThrowDefault();

        int width = Width, len;
        scratch[0] = (byte)'$';
        scratch[2] = (byte)'\r';
        scratch[3] = (byte)'\n';
        scratch[4] = _value < 0 ? (byte)'i' : (byte)'u';
        if (width >= 10)
        {
            scratch[1] = (byte)'3';
            scratch[5] = (byte)('0' + (width / 10));
            scratch[6] = (byte)('0' + (width % 10));
            len = 7;
        }
        else
        {
            scratch[1] = (byte)'2';
            scratch[5] = (byte)('0' + width);
            len = 6;
        }

        scratch[len++] = (byte)'\r';
        scratch[len++] = (byte)'\n';
        writer.WriteRaw(scratch.Slice(0, len));

        static void ThrowDefault() => throw new ArgumentException(
            $"A {nameof(BitFieldEncoding)} must be created via {nameof(Signed)}, {nameof(Unsigned)}, or one of the named encodings.",
            nameof(BitFieldEncoding));
    }

    /// <inheritdoc/>
    public override string ToString() => _value == 0
        ? "(default)"
        : (IsSigned ? "i" : "u") + Width.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public bool Equals(BitFieldEncoding other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BitFieldEncoding other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    /// <summary>Compares two values for equality.</summary>
    public static bool operator ==(BitFieldEncoding x, BitFieldEncoding y) => x._value == y._value;

    /// <summary>Compares two values for non-equality.</summary>
    public static bool operator !=(BitFieldEncoding x, BitFieldEncoding y) => x._value != y._value;
}
