using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

/// <summary>
/// Represents a check for an existing value, for use in conditional operations such as <c>DELEX</c> or <c>SET ... IFEQ</c>.
/// </summary>
public readonly struct ValueCondition
{
    // Supported: equality and non-equality checks for values and digests. Values are stored a RedisValue;
    // digests are stored as a native (CPU-endian) Int64 (long) value, inside the same RedisValue (via the
    // RedisValue.DirectOverlappedBits64 feature). This native Int64 value is an implementation detail that
    // is not directly exposed to the consumer.
    //
    // The exchange format with Redis is hex of the bytes; for the purposes of interfacing this with our
    // raw integer value, this should be considered big-endian, based on the behaviour of XxHash3.
    private const int HashLength = 8; // XXH3 is 64-bit

    private readonly MatchKind _kind;
    private readonly RedisValue _value;

    /// <inheritdoc/>
    public override string ToString()
    {
        switch (_kind)
        {
            case MatchKind.ValueEquals:
                return $"IFEQ {_value}";
            case MatchKind.ValueNotEquals:
                return $"IFNE {_value}";
            case MatchKind.DigestEquals:
                Span<char> buffer = stackalloc char[2 * HashLength];
                WriteHex(_value.DirectOverlappedBits64, buffer);
                return $"IFDEQ {buffer.ToString()}";
            case MatchKind.DigestNotEquals:
                WriteHex(_value.DirectOverlappedBits64, buffer = stackalloc char[2 * HashLength]);
                return $"IFDNE {buffer.ToString()}";
            case MatchKind.None:
                return "";
            default:
                return _kind.ToString();
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValueCondition other && _kind == other._kind && _value == other._value;

    /// <inheritdoc/>
    public override int GetHashCode() => _kind.GetHashCode() ^ _value.GetHashCode();

    /// <summary>
    /// Indicates whether this instance represents a valid test.
    /// </summary>
    public bool HasValue => _kind is not MatchKind.None;

    /// <summary>
    /// Indicates whether this instance represents a digest test.
    /// </summary>
    public bool IsDigest => _kind is MatchKind.DigestEquals or MatchKind.DigestNotEquals;

    /// <summary>
    /// Indicates whether this instance represents an equality test.
    /// </summary>
    public bool IsEqual => _kind is MatchKind.ValueEquals or MatchKind.DigestEquals;

    /// <summary>
    /// Gets the underlying value for this condition.
    /// </summary>
    public RedisValue Value => _value;

    private ValueCondition(MatchKind kind, in RedisValue value)
    {
        _kind = kind;
        _value = value;
        // if it's a digest operation, the value must be an int64
        Debug.Assert(_kind is not (MatchKind.DigestEquals or MatchKind.DigestNotEquals) ||
                     value.Type == RedisValue.StorageType.Int64);
    }

    /// <summary>
    /// Create a value equality condition with the supplied value.
    /// </summary>
    public static ValueCondition Equal(in RedisValue value) => new(MatchKind.ValueEquals, value);

    /// <summary>
    /// Create a value non-equality condition with the supplied value.
    /// </summary>
    public static ValueCondition NotEqual(in RedisValue value) => new(MatchKind.ValueNotEquals, value);

    /// <summary>
    /// Create a digest equality condition, computing the digest of the supplied value.
    /// </summary>
    public static ValueCondition DigestEqual(in RedisValue value) => value.Digest();

    /// <summary>
    /// Create a digest non-equality condition, computing the digest of the supplied value.
    /// </summary>
    public static ValueCondition DigestNotEqual(in RedisValue value) => !value.Digest();

    private enum MatchKind : byte
    {
        None,
        ValueEquals,
        ValueNotEquals,
        DigestEquals,
        DigestNotEquals,
    }

    /// <summary>
    /// Calculate the digest of a payload, as an equality test. For a non-equality test, use <see cref="NotEqual"/> on the result.
    /// </summary>
    public static ValueCondition CalculateDigest(ReadOnlySpan<byte> value)
    {
        // the internal impl of XxHash3 uses ulong (not Span<byte>), so: use
        // that to avoid extra steps, and store the CPU-endian value
        var digest = XxHash3.HashToUInt64(value);
        return new ValueCondition(MatchKind.DigestEquals, digest);
    }

    /// <summary>
    /// Creates an equality match based on the specified digest bytes.
    /// </summary>
    public static ValueCondition ParseDigest(ReadOnlySpan<char> digest)
    {
        if (digest.Length != 2 * HashLength) ThrowDigestLength();

        // we receive 16 hex characters, as bytes; parse that into a long, by
        // first dealing with the nibbles
        Span<byte> tmp = stackalloc byte[HashLength];
        int offset = 0;
        for (int i = 0; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(
                (ParseNibble(digest[offset++]) << 4) // hi
                | ParseNibble(digest[offset++])); // lo
        }
        // now interpret that as big-endian
        var digestInt64 = BinaryPrimitives.ReadInt64BigEndian(tmp);
        return new ValueCondition(MatchKind.DigestEquals, digestInt64);
    }

    private static byte ParseNibble(int b)
    {
        if (b >= '0' & b <= '9') return (byte)(b - '0');
        if (b >= 'a' & b <= 'f') return (byte)(b - 'a' + 10);
        if (b >= 'A' & b <= 'F') return (byte)(b - 'A' + 10);
        return ThrowInvalidBytes();

        static byte ThrowInvalidBytes() => throw new ArgumentException("Invalid digest bytes");
    }

    private static void ThrowDigestLength() => throw new ArgumentException($"Invalid digest length; expected {2 * HashLength} bytes");

    /// <summary>
    /// Creates an equality match based on the specified digest bytes.
    /// </summary>
    public static ValueCondition ParseDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 2 * HashLength) ThrowDigestLength();

        // we receive 16 hex characters, as bytes; parse that into a long, by
        // first dealing with the nibbles
        Span<byte> tmp = stackalloc byte[HashLength];
        int offset = 0;
        for (int i = 0; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(
                (ToNibble(digest[offset++]) << 4) // hi
                | ToNibble(digest[offset++])); // lo
        }
        // now interpret that as big-endian
        var digestInt64 = BinaryPrimitives.ReadInt64BigEndian(tmp);
        return new ValueCondition(MatchKind.DigestEquals, digestInt64);

        static byte ToNibble(int b)
        {
            if (b >= '0' & b <= '9') return (byte)(b - '0');
            if (b >= 'a' & b <= 'f') return (byte)(b - 'a' + 10);
            if (b >= 'A' & b <= 'F') return (byte)(b - 'A' + 10);
            return ThrowInvalidBytes();
        }

        static byte ThrowInvalidBytes() => throw new ArgumentException("Invalid digest bytes");
    }

    internal int TokenCount => _kind == MatchKind.None ? 0 : 2;

    internal void WriteTo(PhysicalConnection physical)
    {
        switch (_kind)
        {
            case MatchKind.ValueEquals:
                physical.WriteBulkString("IFEQ"u8);
                physical.WriteBulkString(_value);
                break;
            case MatchKind.ValueNotEquals:
                physical.WriteBulkString("IFNE"u8);
                physical.WriteBulkString(_value);
                break;
            case MatchKind.DigestEquals:
                physical.WriteBulkString("IFDEQ"u8);
                Span<byte> buffer = stackalloc byte[16];
                WriteHex(_value.DirectOverlappedBits64, buffer);
                physical.WriteBulkString(buffer);
                break;
            case MatchKind.DigestNotEquals:
                physical.WriteBulkString("IFDNE"u8);
                WriteHex(_value.DirectOverlappedBits64, buffer = stackalloc byte[16]);
                physical.WriteBulkString(buffer);
                break;
        }
    }

    internal static void WriteHex(long value, Span<byte> target)
    {
        Debug.Assert(target.Length == 2 * HashLength);

        // iterate over the bytes in big-endian order, writing the hi/lo nibbles,
        // using pointer-like behaviour (rather than complex shifts and masks)
        if (BitConverter.IsLittleEndian)
        {
            value = BinaryPrimitives.ReverseEndianness(value);
        }
        ref byte ptr = ref Unsafe.As<long, byte>(ref value);
        int targetOffset = 0;
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        for (int sourceOffset = 0; sourceOffset < sizeof(long); sourceOffset++)
        {
            byte b = Unsafe.Add(ref ptr, sourceOffset);
            target[targetOffset++] = hex[(b >> 4) & 0xF]; // hi nibble
            target[targetOffset++] = hex[b & 0xF]; // lo
        }
    }

    internal static void WriteHex(long value, Span<char> target)
    {
        Debug.Assert(target.Length == 2 * HashLength);

        // iterate over the bytes in big-endian order, writing the hi/lo nibbles,
        // using pointer-like behaviour (rather than complex shifts and masks)
        if (BitConverter.IsLittleEndian)
        {
            value = BinaryPrimitives.ReverseEndianness(value);
        }
        ref byte ptr = ref Unsafe.As<long, byte>(ref value);
        int targetOffset = 0;
        const string hex = "0123456789abcdef";
        for (int sourceOffset = 0; sourceOffset < sizeof(long); sourceOffset++)
        {
            byte b = Unsafe.Add(ref ptr, sourceOffset);
            target[targetOffset++] = hex[(b >> 4) & 0xF]; // hi nibble
            target[targetOffset++] = hex[b & 0xF]; // lo
        }
    }

    /// <summary>
    /// Negate this condition. The digest/value aspect of the condition is preserved.
    /// </summary>
    public static ValueCondition operator !(in ValueCondition value) => value._kind switch
    {
        MatchKind.ValueEquals => new(MatchKind.ValueNotEquals, value._value),
        MatchKind.ValueNotEquals => new(MatchKind.ValueEquals, value._value),
        MatchKind.DigestEquals => new(MatchKind.DigestNotEquals, value._value),
        MatchKind.DigestNotEquals => new(MatchKind.DigestEquals, value._value),
        _ => value, // GIGO
    };

    /// <summary>
    /// Convert this condition to a digest condition. If this condition is not a value-based condition, it is returned as-is.
    /// The equality or non-equality aspect of the condition is preserved.
    /// </summary>
    public ValueCondition Digest() => _kind switch
    {
        MatchKind.ValueEquals => _value.Digest(),
        MatchKind.ValueNotEquals => !_value.Digest(),
        _ => this,
    };
}
