using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Represents a check for an existing value - this could be existence (NX/XX), equality (IFEQ/IFNE), or digest equality (IFDEQ/IFDNE).
/// </summary>
public readonly struct ValueCondition : Interpolated.IRespArgument
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
                var written = WriteHex(_value.OverlappedValueInt64, stackalloc char[2 * DigestBytes]);
                return $"IFDEQ {written.ToString()}";
            case ConditionKind.DigestNotEquals:
                written = WriteHex(_value.OverlappedValueInt64, stackalloc char[2 * DigestBytes]);
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
    /// The retry category implied by applying this condition to an otherwise unconditional write;
    /// <see cref="CommandFlags.None"/> means "no opinion", leaving the per-command default in place.
    /// </summary>
    /// <remarks>
    /// Note that a compare-and-set replay (IFEQ/IFDEQ) after an *ambiguous success* fails the comparison and
    /// reports "not set" even though the write did land; the keyspace still converges, which is what the
    /// category describes, but callers relying on the boolean should be aware of it.
    /// </remarks>
    internal CommandFlags RetryCategory => _kind switch
    {
        ConditionKind.Always => CommandFlags.None,
        _ => CommandFlags.CommandRetryWriteChecked,
    };

    /// <summary>
    /// Gets the underlying value for this condition.
    /// </summary>
    public RedisValue Value
    {
        get => _value;
    }

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

    [ThreadStatic]
    private static XxHash3? _xxh;

    /// <summary>
    /// Calculate the digest of a payload, as an equality test. For a non-equality test, use <see cref="NotEqual"/> on the result.
    /// </summary>
    public static ValueCondition CalculateDigest(in ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment)
        {
            return CalculateDigest(value.FirstSpan);
        }

        var xxh = _xxh;
        xxh ??= _xxh = new XxHash3();
        xxh.Reset();
        foreach (var memory in value)
        {
            xxh.Append(memory.Span);
        }
        var digest = unchecked((long)xxh.GetCurrentHashAsUInt64());
        return new ValueCondition(ConditionKind.DigestEquals, digest);
    }

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

    /// <summary>
    /// The already-framed RESP keyword for this condition - <c>NX</c>, <c>IFDEQ</c>, ... - or empty when
    /// the condition contributes no arguments.
    /// </summary>
    /// <remarks>
    /// Shared by both writers rather than restated in each; see the equivalent on <see cref="Expiration"/>.
    /// <see cref="IsValueTest"/> and <see cref="IsDigestTest"/> say what follows it, and in which encoding -
    /// those two stay <c>internal</c> because <c>DigestUnitTests</c> asserts on them; this does not.
    /// </remarks>
    private ReadOnlySpan<byte> KeywordResp => _kind switch
    {
        ConditionKind.Exists => "$2\r\nXX\r\n"u8,
        ConditionKind.NotExists => "$2\r\nNX\r\n"u8,
        ConditionKind.ValueEquals => "$4\r\nIFEQ\r\n"u8,
        ConditionKind.ValueNotEquals => "$4\r\nIFNE\r\n"u8,
        ConditionKind.DigestEquals => "$5\r\nIFDEQ\r\n"u8,
        ConditionKind.DigestNotEquals => "$5\r\nIFDNE\r\n"u8,
        _ => default,
    };

    /// <inheritdoc/>
    /// <remarks>See <see cref="Expiration"/> for why this is an explicit implementation.</remarks>
    void Interpolated.IRespArgument.WriteTo(scoped ref Interpolated.RespRequestBuilder handler)
    {
        var keyword = KeywordResp;
        if (keyword.IsEmpty) return; // ValueCondition.Always contributes no arguments

#pragma warning disable SER011 // pre-framed constants owned by this type; see Expiration for the reasoning
        handler.AppendFormatted(new Interpolated.RespFragment(keyword));
#pragma warning restore SER011
        if (IsValueTest)
        {
            handler.AppendFormatted(_value);
        }
        else if (IsDigestTest)
        {
            // the wire form is hex of the big-endian digest bytes, NOT the int64 the RedisValue holds;
            // AppendBulk takes the stack buffer directly, where a RedisValue would need a byte[]
            handler.AppendBulk(WriteHex(_value.OverlappedValueInt64, stackalloc byte[2 * DigestBytes]));
        }
    }

    internal void WriteTo(in MessageWriter writer)
    {
        var keyword = KeywordResp;
        if (keyword.IsEmpty) return;

        writer.WriteRaw(keyword);
        if (IsValueTest)
        {
            writer.WriteBulkString(_value);
        }
        else if (IsDigestTest)
        {
            writer.WriteBulkString(WriteHex(_value.OverlappedValueInt64, stackalloc byte[2 * DigestBytes]));
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

    /// <summary>
    /// Read a <c>DIGEST</c> reply as the condition a later write can be gated on; null for a key that
    /// does not exist.
    /// </summary>
    /// <param name="reader">The reader, positioned on the reply.</param>
    /// <param name="digest">The parsed digest, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// for the same reason <see cref="KeywordResp"/> is shared by both writers: one copy of the shape
    /// knowledge, so there is nothing to fall out of step.
    /// </remarks>
    internal static bool TryReadDigest(in RespReader reader, out ValueCondition? digest)
    {
        if (reader.IsNull) // for example, key doesn't exist
        {
            digest = null;
            return true;
        }

        if (reader.ScalarLengthIs(2 * DigestBytes))
        {
            var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[2 * DigestBytes]);
            digest = ParseDigest(span);
            return true;
        }

        digest = null;
        return false;
    }

    internal When AsWhen() => _kind switch
    {
        ConditionKind.Always => When.Always,
        ConditionKind.Exists => When.Exists,
        ConditionKind.NotExists => When.NotExists,
        _ => ThrowInvalidOperation().AsWhen(),
    };
}
