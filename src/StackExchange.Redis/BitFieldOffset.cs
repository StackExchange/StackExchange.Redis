using System;
using System.Globalization;

namespace StackExchange.Redis;

/// <summary>
/// Where a bitfield sits inside a string: either an absolute bit offset, or - via
/// <see cref="Index(long)"/> - a zero-based index into an array of consecutive fields of the same
/// width, which the server multiplies out for us (the <c>#</c> form).
/// </summary>
/// <remarks><seealso href="https://redis.io/commands/bitfield"/></remarks>
public readonly struct BitFieldOffset : IEquatable<BitFieldOffset>
{
    private readonly long _value;
    private readonly bool _isIndex;

    private BitFieldOffset(long value, bool isIndex)
    {
        _value = value;
        _isIndex = isIndex;
    }

    /// <summary>
    /// An absolute offset, in bits, from the start of the string.
    /// </summary>
    /// <param name="offset">The offset, in bits.</param>
    public static BitFieldOffset Bits(long offset) => offset < 0
        ? throw new ArgumentOutOfRangeException(nameof(offset), "A bitfield offset cannot be negative.")
        : new(offset, false);

    /// <summary>
    /// A zero-based index into an array of consecutive fields of the encoding's own width; the
    /// server multiplies the index by the width, so <c>Index(2)</c> of a <c>u8</c> is bit 16.
    /// </summary>
    /// <param name="index">The index of the field.</param>
    public static BitFieldOffset Index(long index) => index < 0
        ? throw new ArgumentOutOfRangeException(nameof(index), "A bitfield index cannot be negative.")
        : new(index, true);

    /// <summary>
    /// Creates an absolute bit offset; equivalent to <see cref="Bits(long)"/>.
    /// </summary>
    /// <param name="offset">The offset, in bits.</param>
    public static implicit operator BitFieldOffset(long offset) => Bits(offset);

    internal RedisValue ToLiteral() => _isIndex
        ? (RedisValue)("#" + _value.ToString(CultureInfo.InvariantCulture))
        : (RedisValue)_value;

    /// <inheritdoc/>
    public override string ToString() => _isIndex
        ? "#" + _value.ToString(CultureInfo.InvariantCulture)
        : _value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public bool Equals(BitFieldOffset other) => _value == other._value && _isIndex == other._isIndex;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BitFieldOffset other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _isIndex ? ~_value.GetHashCode() : _value.GetHashCode();

    /// <summary>Compares two values for equality.</summary>
    public static bool operator ==(BitFieldOffset x, BitFieldOffset y) => x.Equals(y);

    /// <summary>Compares two values for non-equality.</summary>
    public static bool operator !=(BitFieldOffset x, BitFieldOffset y) => !x.Equals(y);
}
