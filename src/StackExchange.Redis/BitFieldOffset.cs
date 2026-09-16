using System;
using System.Globalization;

namespace StackExchange.Redis;

/// <summary>
/// Where a bitfield sits inside a string: either a bit position, or - via
/// <see cref="Element(long)"/> - the position of one element in an array of consecutive fields of
/// the same width, which the server multiplies out for us (the <c>#</c> form).
/// </summary>
/// <remarks><seealso href="https://redis.io/commands/bitfield"/></remarks>
public readonly struct BitFieldOffset : IEquatable<BitFieldOffset>
{
    private readonly long _value;
    private readonly bool _isElement;

    private BitFieldOffset(long value, bool isIndex)
    {
        _value = value;
        _isElement = isIndex;
    }

    /// <summary>
    /// The zero-based bit position, counted from the start of the string; <c>Bit(16)</c> is the
    /// seventeenth bit.
    /// </summary>
    /// <param name="bit">The bit position.</param>
    public static BitFieldOffset Bit(long bit) => bit < 0
        ? throw new ArgumentOutOfRangeException(nameof(bit), "A bitfield bit position cannot be negative.")
        : new(bit, false);

    /// <summary>
    /// The zero-based position of one element in an array of consecutive fields of the encoding's
    /// own width; the server multiplies it by the width, so <c>Element(2)</c> of a <c>u8</c> is
    /// bit 16.
    /// </summary>
    /// <param name="element">The element position.</param>
    public static BitFieldOffset Element(long element) => element < 0
        ? throw new ArgumentOutOfRangeException(nameof(element), "A bitfield element position cannot be negative.")
        : new(element, true);

    /// <summary>
    /// Creates a bit position; equivalent to <see cref="Bit(long)"/>.
    /// </summary>
    /// <param name="bit">The bit position.</param>
    public static implicit operator BitFieldOffset(long bit) => Bit(bit);

    internal void Write(in MessageWriter writer, Span<byte> scratch)
    {
        if (!_isElement)
        {
            writer.WriteBulkString(_value);
            return;
        }

        scratch[0] = (byte)'#';
        var len = Format.FormatInt64(_value, scratch.Slice(1)) + 1;
        writer.WriteBulkString(scratch.Slice(0, len));
    }

    /// <inheritdoc/>
    public override string ToString() => _isElement
        ? "#" + _value.ToString(CultureInfo.InvariantCulture)
        : _value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public bool Equals(BitFieldOffset other) => _value == other._value && _isElement == other._isElement;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BitFieldOffset other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _isElement ? ~_value.GetHashCode() : _value.GetHashCode();

    /// <summary>Compares two values for equality.</summary>
    public static bool operator ==(BitFieldOffset x, BitFieldOffset y) => x.Equals(y);

    /// <summary>Compares two values for non-equality.</summary>
    public static bool operator !=(BitFieldOffset x, BitFieldOffset y) => !x.Equals(y);
}
