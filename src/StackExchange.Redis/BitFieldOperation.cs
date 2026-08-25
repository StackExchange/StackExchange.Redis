using System;

namespace StackExchange.Redis;

/// <summary>
/// A single <c>BITFIELD</c> sub-operation; see <see cref="Get"/>, <see cref="Set"/> and
/// <see cref="IncrementBy"/>.
/// </summary>
/// <remarks><seealso href="https://redis.io/commands/bitfield"/></remarks>
public readonly struct BitFieldOperation : IEquatable<BitFieldOperation>
{
    internal enum OperationKind : byte
    {
        None = 0,
        Get,
        Set,
        IncrementBy,
    }

    private readonly BitFieldOffset _offset;
    private readonly BitFieldEncoding _encoding;
    private readonly long _value;
    private readonly BitFieldOverflow _overflow;
    private readonly OperationKind _kind;

    private BitFieldOperation(OperationKind kind, BitFieldEncoding encoding, BitFieldOffset offset, long value, BitFieldOverflow overflow)
    {
        if (encoding.IsDefault)
        {
            throw new ArgumentException(
                $"A {nameof(BitFieldEncoding)} must be created via {nameof(BitFieldEncoding)}.{nameof(BitFieldEncoding.Signed)}, {nameof(BitFieldEncoding.Unsigned)}, or one of the named encodings.",
                nameof(encoding));
        }
        if (overflow is < BitFieldOverflow.Wrap or > BitFieldOverflow.Fail)
        {
            throw new ArgumentOutOfRangeException(nameof(overflow));
        }

        _kind = kind;
        _encoding = encoding;
        _offset = offset;
        _value = value;
        _overflow = overflow;
    }

    /// <summary>
    /// Reads the field at <paramref name="offset"/>. A field that lies beyond the end of the string
    /// reads as zero, and no <see cref="BitFieldOverflow"/> applies.
    /// </summary>
    /// <param name="encoding">The width and signedness of the field.</param>
    /// <param name="offset">The location of the field.</param>
    public static BitFieldOperation Get(BitFieldEncoding encoding, BitFieldOffset offset) =>
        new(OperationKind.Get, encoding, offset, 0, BitFieldOverflow.Wrap);

    /// <summary>
    /// Writes <paramref name="value"/> to the field at <paramref name="offset"/>, returning the
    /// previous value; the string is zero-extended as needed.
    /// </summary>
    /// <param name="encoding">The width and signedness of the field.</param>
    /// <param name="offset">The location of the field.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="overflow">How to handle a value that does not fit the encoding.</param>
    public static BitFieldOperation Set(BitFieldEncoding encoding, BitFieldOffset offset, long value, BitFieldOverflow overflow = BitFieldOverflow.Wrap) =>
        new(OperationKind.Set, encoding, offset, value, overflow);

    /// <summary>
    /// Increments the field at <paramref name="offset"/> by <paramref name="value"/> (which may be
    /// negative), returning the new value; the string is zero-extended as needed.
    /// </summary>
    /// <param name="encoding">The width and signedness of the field.</param>
    /// <param name="offset">The location of the field.</param>
    /// <param name="value">The amount to increment by.</param>
    /// <param name="overflow">How to handle an increment that does not fit the encoding.</param>
    public static BitFieldOperation IncrementBy(BitFieldEncoding encoding, BitFieldOffset offset, long value, BitFieldOverflow overflow = BitFieldOverflow.Wrap) =>
        new(OperationKind.IncrementBy, encoding, offset, value, overflow);

    internal OperationKind Kind => _kind;

    internal BitFieldEncoding Encoding => _encoding;

    internal BitFieldOffset Offset => _offset;

    internal long Value => _value;

    /// <summary>
    /// The overflow behavior of this operation; <see cref="BitFieldOverflow.Wrap"/> for a
    /// <see cref="Get"/>, which cannot overflow.
    /// </summary>
    internal BitFieldOverflow Overflow => _overflow;

    /// <inheritdoc/>
    public override string ToString() => _kind switch
    {
        OperationKind.Get => $"GET {_encoding} {_offset}",
        OperationKind.Set => $"SET {_encoding} {_offset} {_value} ({_overflow})",
        OperationKind.IncrementBy => $"INCRBY {_encoding} {_offset} {_value} ({_overflow})",
        _ => "(default)",
    };

    /// <inheritdoc/>
    public bool Equals(BitFieldOperation other) =>
        _kind == other._kind && _encoding == other._encoding && _offset == other._offset
        && _value == other._value && _overflow == other._overflow;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BitFieldOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = ((int)_kind * 397) ^ ((int)_overflow * 31);
        hash = (hash * 397) ^ _encoding.GetHashCode();
        hash = (hash * 397) ^ _offset.GetHashCode();
        return (hash * 397) ^ _value.GetHashCode();
    }

    /// <summary>Compares two values for equality.</summary>
    public static bool operator ==(BitFieldOperation x, BitFieldOperation y) => x.Equals(y);

    /// <summary>Compares two values for non-equality.</summary>
    public static bool operator !=(BitFieldOperation x, BitFieldOperation y) => !x.Equals(y);
}
