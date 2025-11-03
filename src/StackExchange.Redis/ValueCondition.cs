using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

/// <summary>
/// Represents a check for an existing value, for use in conditional operations such as <c>DELEX</c> or <c>SET ... IFEQ</c>.
/// </summary>
[Experimental(Experiments.Server_8_4, UrlFormat = Experiments.UrlFormat)]
public readonly struct ValueCondition
{
    internal enum ConditionKind : byte
    {
        Always, // default, importantly
        Exists,
        NotExists,
        ValueEquals,
        ValueNotEquals,
        DigestEquals,
        DigestNotEquals,
    }

    // Supported: equality and non-equality checks for values and digests. Values are stored a RedisValue;
    // digests are stored as a native (CPU-endian) Int64 (long) value, inside the same RedisValue (via the
    // RedisValue.DirectOverlappedBits64 feature). This native Int64 value is an implementation detail that
    // is not directly exposed to the consumer.
    //
    // The exchange format with Redis is hex of the bytes; for the purposes of interfacing this with our
    // raw integer value, this should be considered big-endian, based on the behaviour of XxHash3.
    internal const int DigestBytes = 8; // XXH3 is 64-bit

    private readonly ConditionKind _kind;
    private readonly RedisValue _value;

    internal ConditionKind Kind => _kind;

    /// <summary>
    /// Always perform the operation; equivalent to <see cref="When.Always"/>.
    /// </summary>
    public static ValueCondition Always { get; } = new(ConditionKind.Always, RedisValue.Null);

    /// <summary>
    /// Only perform the operation if the value exists; equivalent to <see cref="When.Exists"/>.
    /// </summary>
    public static ValueCondition Exists { get; } = new(ConditionKind.Exists, RedisValue.Null);

    /// <summary>
    /// Only perform the operation if the value does not exist; equivalent to <see cref="When.NotExists"/>.
    /// </summary>
    public static ValueCondition NotExists { get; } = new(ConditionKind.NotExists, RedisValue.Null);

    /// <inheritdoc/>
    public override string ToString()
    {
        switch (_kind)
        {
            case ConditionKind.Exists:
                return "XX";
            case ConditionKind.NotExists:
                return "NX";
            case ConditionKind.ValueEquals:
                return $"IFEQ {_value}";
            case ConditionKind.ValueNotEquals:
                return $"IFNE {_value}";
            case ConditionKind.DigestEquals:
                var written = WriteHex(_value.DirectOverlappedBits64, stackalloc char[2 * DigestBytes]);
                return $"IFDEQ {written.ToString()}";
            case ConditionKind.DigestNotEquals:
                written = WriteHex(_value.DirectOverlappedBits64, stackalloc char[2 * DigestBytes]);
                return $"IFDNE {written.ToString()}";
            case ConditionKind.Always:
                return "";
            default:
                return ThrowInvalidOperation().ToString();
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValueCondition other && _kind == other._kind && _value == other._value;

    /// <inheritdoc/>
    public override int GetHashCode() => _kind.GetHashCode() ^ _value.GetHashCode();

    /// <summary>
    /// Indicates whether this instance represents a value comparison test.
    /// </summary>
    internal bool IsValueTest => _kind is ConditionKind.ValueEquals or ConditionKind.ValueNotEquals;

    /// <summary>
    /// Indicates whether this instance represents a digest test.
    /// </summary>
    internal bool IsDigestTest => _kind is ConditionKind.DigestEquals or ConditionKind.DigestNotEquals;

    /// <summary>
    /// Indicates whether this instance represents an existence test.
    /// </summary>
    internal bool IsExistenceTest => _kind is ConditionKind.Exists or ConditionKind.NotExists;

    /// <summary>
    /// Indicates whether this instance represents a negative test (not-equals, not-exists, digest-not-equals).
    /// </summary>
    internal bool IsNegated => _kind is ConditionKind.ValueNotEquals or ConditionKind.DigestNotEquals or ConditionKind.NotExists;

    /// <summary>
    /// Gets the underlying value for this condition.
    /// </summary>
    public RedisValue Value => _value;

    private ValueCondition(ConditionKind kind, in RedisValue value)
    {
        if (value.IsNull)
        {
            kind = kind switch
            {
                // interpret === null as "does not exist"
                ConditionKind.DigestEquals or ConditionKind.ValueEquals => ConditionKind.NotExists,

                // interpret !== null as "exists"
                ConditionKind.DigestNotEquals or ConditionKind.ValueNotEquals => ConditionKind.Exists,

                // otherwise: leave alone
                _ => kind,
            };
        }
        _kind = kind;
        _value = value;
        // if it's a digest operation, the value must be an int64
        Debug.Assert(_kind is not (ConditionKind.DigestEquals or ConditionKind.DigestNotEquals) ||
                     value.Type == RedisValue.StorageType.Int64);
    }

    /// <summary>
    /// Create a value equality condition with the supplied value.
    /// </summary>
    public static ValueCondition Equal(in RedisValue value) => new(ConditionKind.ValueEquals, value);

    /// <summary>
    /// Create a value non-equality condition with the supplied value.
    /// </summary>
    public static ValueCondition NotEqual(in RedisValue value) => new(ConditionKind.ValueNotEquals, value);

    /// <summary>
    /// Create a digest equality condition, computing the digest of the supplied value.
    /// </summary>
    public static ValueCondition DigestEqual(in RedisValue value) => value.Digest();

    /// <summary>
    /// Create a digest non-equality condition, computing the digest of the supplied value.
    /// </summary>
    public static ValueCondition DigestNotEqual(in RedisValue value) => !value.Digest();

    /// <summary>
    /// Calculate the digest of a payload, as an equality test. For a non-equality test, use <see cref="NotEqual"/> on the result.
    /// </summary>
    public static ValueCondition CalculateDigest(ReadOnlySpan<byte> value)
    {
        // the internal impl of XxHash3 uses ulong (not Span<byte>), so: use
        // that to avoid extra steps, and store the CPU-endian value
        var digest = unchecked((long)XxHash3.HashToUInt64(value));
        return new ValueCondition(ConditionKind.DigestEquals, digest);
    }

    /// <summary>
    /// Creates an equality match based on the specified digest bytes.
    /// </summary>
    public static ValueCondition ParseDigest(ReadOnlySpan<char> digest)
    {
        if (digest.Length != 2 * DigestBytes) ThrowDigestLength();

        // we receive 16 hex characters, as bytes; parse that into a long, by
        // first dealing with the nibbles
        Span<byte> tmp = stackalloc byte[DigestBytes];
        int offset = 0;
        for (int i = 0; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(
                (ParseNibble(digest[offset++]) << 4) // hi
                | ParseNibble(digest[offset++])); // lo
        }
        // now interpret that as big-endian
        var digestInt64 = BinaryPrimitives.ReadInt64BigEndian(tmp);
        return new ValueCondition(ConditionKind.DigestEquals, digestInt64);
    }

    private static byte ParseNibble(int b)
    {
        if (b >= '0' & b <= '9') return (byte)(b - '0');
        if (b >= 'a' & b <= 'f') return (byte)(b - 'a' + 10);
        if (b >= 'A' & b <= 'F') return (byte)(b - 'A' + 10);
        return ThrowInvalidBytes();

        static byte ThrowInvalidBytes() => throw new ArgumentException("Invalid digest bytes");
    }

    private static void ThrowDigestLength() => throw new ArgumentException($"Invalid digest length; expected {2 * DigestBytes} bytes");

    /// <summary>
    /// Creates an equality match based on the specified digest bytes.
    /// </summary>
    public static ValueCondition ParseDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 2 * DigestBytes) ThrowDigestLength();

        // we receive 16 hex characters, as bytes; parse that into a long, by
        // first dealing with the nibbles
        Span<byte> tmp = stackalloc byte[DigestBytes];
        int offset = 0;
        for (int i = 0; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(
                (ToNibble(digest[offset++]) << 4) // hi
                | ToNibble(digest[offset++])); // lo
        }
        // now interpret that as big-endian
        var digestInt64 = BinaryPrimitives.ReadInt64BigEndian(tmp);
        return new ValueCondition(ConditionKind.DigestEquals, digestInt64);

        static byte ToNibble(int b)
        {
            if (b >= '0' & b <= '9') return (byte)(b - '0');
            if (b >= 'a' & b <= 'f') return (byte)(b - 'a' + 10);
            if (b >= 'A' & b <= 'F') return (byte)(b - 'A' + 10);
            return ThrowInvalidBytes();
        }

        static byte ThrowInvalidBytes() => throw new ArgumentException("Invalid digest bytes");
    }

    internal int TokenCount => _kind switch
    {
        ConditionKind.Exists or ConditionKind.NotExists => 1,
        ConditionKind.ValueEquals or ConditionKind.ValueNotEquals or ConditionKind.DigestEquals or ConditionKind.DigestNotEquals => 2,
        _ => 0,
    };

    internal void WriteTo(PhysicalConnection physical)
    {
        switch (_kind)
        {
            case ConditionKind.Exists:
                physical.WriteBulkString("XX"u8);
                break;
            case ConditionKind.NotExists:
                physical.WriteBulkString("NX"u8);
                break;
            case ConditionKind.ValueEquals:
                physical.WriteBulkString("IFEQ"u8);
                physical.WriteBulkString(_value);
                break;
            case ConditionKind.ValueNotEquals:
                physical.WriteBulkString("IFNE"u8);
                physical.WriteBulkString(_value);
                break;
            case ConditionKind.DigestEquals:
                physical.WriteBulkString("IFDEQ"u8);
                var written = WriteHex(_value.DirectOverlappedBits64, stackalloc byte[2 * DigestBytes]);
                physical.WriteBulkString(written);
                break;
            case ConditionKind.DigestNotEquals:
                physical.WriteBulkString("IFDNE"u8);
                written = WriteHex(_value.DirectOverlappedBits64, stackalloc byte[2 * DigestBytes]);
                physical.WriteBulkString(written);
                break;
        }
    }

    internal static Span<byte> WriteHex(long value, Span<byte> target)
    {
        Debug.Assert(target.Length >= 2 * DigestBytes);

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
        return target.Slice(0, 2 * DigestBytes);
    }

    internal static Span<char> WriteHex(long value, Span<char> target)
    {
        Debug.Assert(target.Length >= 2 * DigestBytes);

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
        return target.Slice(0, 2 * DigestBytes);
    }

    /// <summary>
    /// Negate this condition. The nature of the condition is preserved.
    /// </summary>
    public static ValueCondition operator !(in ValueCondition value) => value._kind switch
    {
        ConditionKind.ValueEquals => new(ConditionKind.ValueNotEquals, value._value),
        ConditionKind.ValueNotEquals => new(ConditionKind.ValueEquals, value._value),
        ConditionKind.DigestEquals => new(ConditionKind.DigestNotEquals, value._value),
        ConditionKind.DigestNotEquals => new(ConditionKind.DigestEquals, value._value),
        ConditionKind.Exists => new(ConditionKind.NotExists, value._value),
        ConditionKind.NotExists => new(ConditionKind.Exists, value._value),
        // ReSharper disable once ExplicitCallerInfoArgument
        _ => value.ThrowInvalidOperation("operator !"),
    };

    /// <summary>
    /// Convert a <see cref="When"/> to a <see cref="ValueCondition"/>.
    /// </summary>
    public static implicit operator ValueCondition(When when) => when switch
    {
        When.Always => Always,
        When.Exists => Exists,
        When.NotExists => NotExists,
        _ => throw new ArgumentOutOfRangeException(nameof(when)),
    };

    /// <summary>
    /// Convert a value condition to a digest condition.
    /// </summary>
    public ValueCondition AsDigest() => _kind switch
    {
        ConditionKind.ValueEquals => _value.Digest(),
        ConditionKind.ValueNotEquals => !_value.Digest(),
        _ => ThrowInvalidOperation(),
    };

    internal ValueCondition ThrowInvalidOperation([CallerMemberName] string? operation = null)
        => throw new InvalidOperationException($"{operation} cannot be used with a {_kind} condition.");

    internal When AsWhen() => _kind switch
    {
        ConditionKind.Always => When.Always,
        ConditionKind.Exists => When.Exists,
        ConditionKind.NotExists => When.NotExists,
        _ => ThrowInvalidOperation().AsWhen(),
    };
}
