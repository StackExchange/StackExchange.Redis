using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;

namespace StackExchange.Redis;

/// <summary>
/// Represents a check for an existing value, for use in conditional operations such as <c>DELEX</c> or <c>SET ... IFEQ</c>.
/// </summary>
public readonly struct ValueCondition
{
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
    /// Indicates whether this instance represents a value test.
    /// </summary>
    public bool HasValue => _kind != MatchKind.None;

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

    private ValueCondition(MatchKind kind, RedisValue value)
    {
        _kind = kind;
        _value = value;
        // if it's a digest operation, the value must be an int64
        Debug.Assert(_kind is not (MatchKind.DigestEquals or MatchKind.DigestNotEquals) ||
                     value.Type == RedisValue.StorageType.Int64);
    }

    private enum MatchKind : byte
    {
        None,
        ValueEquals,
        ValueNotEquals,
        DigestEquals,
        DigestNotEquals,
    }

    /// <summary>
    /// Create an equality match based on this value. If the value is already an equality match, no change is made.
    /// The underlying nature of the test (digest vs value) is preserved.
    /// </summary>
    public ValueCondition Equal() => _kind switch
    {
        MatchKind.ValueEquals or MatchKind.DigestEquals => this, // no change needed
        MatchKind.ValueNotEquals => new ValueCondition(MatchKind.ValueEquals, _value),
        MatchKind.DigestNotEquals => new ValueCondition(MatchKind.DigestEquals, _value),
        _ => throw new InvalidOperationException($"Unexpected match kind: {_kind}"),
    };

    /// <summary>
    /// Create a non-equality match based on this value. If the value is already a non-equality match, no change is made.
    /// The underlying nature of the test (digest vs value) is preserved.
    /// </summary>
    public ValueCondition NotEqual() => _kind switch
    {
        MatchKind.ValueNotEquals or MatchKind.DigestNotEquals => this, // no change needed
        MatchKind.ValueEquals => new ValueCondition(MatchKind.ValueNotEquals, _value),
        MatchKind.DigestEquals => new ValueCondition(MatchKind.DigestNotEquals, _value),
        _ => throw new InvalidOperationException($"Unexpected match kind: {_kind}"),
    };

    /// <summary>
    /// Create a digest match based on this value. If the value is already a digest match, no change is made.
    /// The underlying equality/non-equality nature of the test is preserved.
    /// </summary>
    public ValueCondition Digest() => _kind switch
    {
        MatchKind.DigestEquals or MatchKind.DigestNotEquals => this, // no change needed
        MatchKind.ValueEquals => _value.Digest(),
        MatchKind.ValueNotEquals => _value.Digest().NotEqual(),
        _ => throw new InvalidOperationException($"Unexpected match kind: {_kind}"),
    };

    internal static readonly ValueCondition Null = default;

    /// <summary>
    /// Calculate the digest of a payload, as an equality test. For a non-equality test, use <see cref="NotEqual"/> on the result.
    /// </summary>
    public static ValueCondition Digest(ReadOnlySpan<byte> payload)
    {
        long digest = unchecked((long)XxHash3.HashToUInt64(payload));
        return new ValueCondition(MatchKind.DigestEquals, digest);
    }

    /// <summary>
    /// Creates an equality match based on the specified digest bytes.
    /// </summary>
    internal static ValueCondition RawDigest(ReadOnlySpan<byte> digest)
    {
        Debug.Assert(digest.Length == HashLength);
        // we receive 16 hex charactes, as bytes; parse that into a long, by
        // first dealing with the nibbles
        Span<byte> tmp = stackalloc byte[HashLength];
        int offset = 0;
        for (int i = 0; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(
                (ToNibble(digest[offset++]) << 4) // hi
                | ToNibble(digest[offset++])); // lo
        }
        // now interpret that as little-endian, so the first network bytes end
        // up in the low integer bytes (this makes writing it easier, and matches
        // basically all CPUs)
        return new ValueCondition(MatchKind.DigestEquals, BinaryPrimitives.ReadInt64LittleEndian(tmp));

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
        // note: see RawDigest for notes on endianness here; for our convenience,
        // we take the bytes in little-endian order, but as long as that
        // matches how we store them: we're fine - it is transparent to the caller.
        ReadOnlySpan<byte> hex = "0123456789abcdef"u8;
        for (int i = 0; i < 2 * HashLength;)
        {
            var b = (byte)value;
            target[i++] = hex[(b >> 4) & 0xF]; // hi nibble
            target[i++] = hex[b & 0xF]; // lo nibble
            value >>= 8;
        }
    }

    internal static void WriteHex(long value, Span<char> target)
    {
        Debug.Assert(target.Length == 2 * HashLength);
        // note: see RawDigest for notes on endianness here; for our convenience,
        // we take the bytes in little-endian order, but as long as that
        // matches how we store them: we're fine - it is transparent to the caller.
        const string hex = "0123456789abcdef";
        for (int i = 0; i < 2 * HashLength;)
        {
            var b = (byte)value;
            target[i++] = hex[(b >> 4) & 0xF]; // hi nibble
            target[i++] = hex[b & 0xF]; // lo nibble
            value >>= 8;
        }
    }
}
