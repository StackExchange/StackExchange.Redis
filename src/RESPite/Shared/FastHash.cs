using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace RESPite;

/// <summary>
/// This type is intended to provide fast hashing functions for small ASCII strings, for example well-known
/// RESP literals that are usually identifiable by their length and initial bytes; it is not intended
/// for general purpose hashing, and the behavior is undefined for non-ASCII literals.
/// All matches must also perform a sequence equality check.
/// </summary>
/// <remarks>See HastHashGenerator.md for more information and intended usage.</remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
[Conditional("DEBUG")] // evaporate in release
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public sealed class FastHashAttribute(string token = "") : Attribute
{
    /// <summary>
    /// The token expected when parsing data, if different from the implied value. The implied
    /// value is the name, replacing underscores for hyphens, so: 'a_b' becomes 'a-b'.
    /// </summary>
    public string Token => token;

    /// <summary>
    /// Indicates whether a parse operation is case-sensitive. Not used in other contexts.
    /// </summary>
    public bool CaseSensitive { get; set; } = true;
}

[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public readonly struct FastHash
{
    private readonly long _hashCI;
    private readonly long _hashCS;
    private readonly ReadOnlyMemory<byte> _value;
    public int Length => _value.Length;

    public FastHash(ReadOnlySpan<byte> value) : this((ReadOnlyMemory<byte>)value.ToArray()) { }

    public FastHash(ReadOnlyMemory<byte> value)
    {
        _value = value;
        var span = value.Span;
        _hashCI = HashCI(span);
        _hashCS = HashCS(span);
    }

    private const long CaseMask = ~0x2020202020202020;

    public bool IsCS(ReadOnlySpan<byte> value) => IsCS(HashCS(value), value);

    public bool IsCS(long hash, ReadOnlySpan<byte> value)
    {
        var len = _value.Length;
        if (hash != _hashCS | (value.Length != len)) return false;
        return len <= MaxBytesHashIsEqualityCS || EqualsCS(_value.Span, value);
    }

    public bool IsCI(ReadOnlySpan<byte> value) => IsCI(HashCI(value), value);

    public bool IsCI(long hash, ReadOnlySpan<byte> value)
    {
        var len = _value.Length;
        if (hash != _hashCI | (value.Length != len)) return false;
        if (len <= MaxBytesHashIsEqualityCS && HashCS(value) == _hashCS) return true;
        return EqualsCI(_value.Span, value);
    }

    public static long HashCS(in ReadOnlySequence<byte> value)
    {
#if NETCOREAPP3_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        var first = value.FirstSpan;
#else
        var first = value.First.Span;
#endif
        return first.Length >= MaxBytesHashed | value.IsSingleSegment
            ? HashCS(first) : SlowHashCS(value);

        static long SlowHashCS(in ReadOnlySequence<byte> value)
        {
            Span<byte> buffer = stackalloc byte[MaxBytesHashed];
            var len = value.Length;
            if (len <= MaxBytesHashed)
            {
                value.CopyTo(buffer);
                buffer = buffer.Slice(0, (int)len);
            }
            else
            {
                value.Slice(0, MaxBytesHashed).CopyTo(buffer);
            }

            return HashCS(buffer);
        }
    }

    internal const int MaxBytesHashIsEqualityCS = sizeof(long), MaxBytesHashed = sizeof(long);

    public static bool EqualsCS(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality
        return len <= MaxBytesHashIsEqualityCS ? HashCS(first) == HashCS(second) : first.SequenceEqual(second);
    }

    public static bool SequenceEqualsCS(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        => first.SequenceEqual(second);

    public static unsafe bool EqualsCI(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality; check that first
        if (len <= MaxBytesHashIsEqualityCS && HashCS(first) == HashCS(second)) return true;

        // OK, don't be clever (SIMD, etc); the purpose of FashHash is to compare RESP key tokens, which are
        // typically relatively short, think 3-20 bytes. That wouldn't even touch a SIMD vector, so:
        // just loop (the exact thing we'd need to do *anyway* in a SIMD implementation, to mop up the non-SIMD
        // trailing bytes).
        fixed (byte* firstPtr = &MemoryMarshal.GetReference(first))
        {
            fixed (byte* secondPtr = &MemoryMarshal.GetReference(second))
            {
                const int CS_MASK = 0b0101_1111;
                for (int i = 0; i < len; i++)
                {
                    byte x = firstPtr[i];
                    var xCI = x & CS_MASK;
                    if (xCI >= 'A' & xCI <= 'Z')
                    {
                        // alpha mismatch
                        if (xCI != (secondPtr[i] & CS_MASK)) return false;
                    }
                    else if (x != secondPtr[i])
                    {
                        // non-alpha mismatch
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public static unsafe bool SequenceEqualsCI(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;

        // OK, don't be clever (SIMD, etc); the purpose of FashHash is to compare RESP key tokens, which are
        // typically relatively short, think 3-20 bytes. That wouldn't even touch a SIMD vector, so:
        // just loop (the exact thing we'd need to do *anyway* in a SIMD implementation, to mop up the non-SIMD
        // trailing bytes).
        fixed (byte* firstPtr = &MemoryMarshal.GetReference(first))
        {
            fixed (byte* secondPtr = &MemoryMarshal.GetReference(second))
            {
                const int CS_MASK = 0b0101_1111;
                for (int i = 0; i < len; i++)
                {
                    byte x = firstPtr[i];
                    var xCI = x & CS_MASK;
                    if (xCI >= 'A' & xCI <= 'Z')
                    {
                        // alpha mismatch
                        if (xCI != (secondPtr[i] & CS_MASK)) return false;
                    }
                    else if (x != secondPtr[i])
                    {
                        // non-alpha mismatch
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public static bool EqualsCS(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality
        return len <= MaxBytesHashIsEqualityCS ? HashCS(first) == HashCS(second) : first.SequenceEqual(second);
    }

    public static bool SequenceEqualsCS(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        => first.SequenceEqual(second);

    public static unsafe bool EqualsCI(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality; check that first
        if (len <= MaxBytesHashIsEqualityCS && HashCS(first) == HashCS(second)) return true;

        // OK, don't be clever (SIMD, etc); the purpose of FashHash is to compare RESP key tokens, which are
        // typically relatively short, think 3-20 bytes. That wouldn't even touch a SIMD vector, so:
        // just loop (the exact thing we'd need to do *anyway* in a SIMD implementation, to mop up the non-SIMD
        // trailing bytes).
        fixed (char* firstPtr = &MemoryMarshal.GetReference(first))
        {
            fixed (char* secondPtr = &MemoryMarshal.GetReference(second))
            {
                const int CS_MASK = 0b0101_1111;
                for (int i = 0; i < len; i++)
                {
                    int x = (byte)firstPtr[i];
                    var xCI = x & CS_MASK;
                    if (xCI >= 'A' & xCI <= 'Z')
                    {
                        // alpha mismatch
                        if (xCI != (secondPtr[i] & CS_MASK)) return false;
                    }
                    else if (x != (byte)secondPtr[i])
                    {
                        // non-alpha mismatch
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public static unsafe bool SequenceEqualsCI(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;

        // OK, don't be clever (SIMD, etc); the purpose of FashHash is to compare RESP key tokens, which are
        // typically relatively short, think 3-20 bytes. That wouldn't even touch a SIMD vector, so:
        // just loop (the exact thing we'd need to do *anyway* in a SIMD implementation, to mop up the non-SIMD
        // trailing bytes).
        fixed (char* firstPtr = &MemoryMarshal.GetReference(first))
        {
            fixed (char* secondPtr = &MemoryMarshal.GetReference(second))
            {
                const int CS_MASK = 0b0101_1111;
                for (int i = 0; i < len; i++)
                {
                    int x = (byte)firstPtr[i];
                    var xCI = x & CS_MASK;
                    if (xCI >= 'A' & xCI <= 'Z')
                    {
                        // alpha mismatch
                        if (xCI != (secondPtr[i] & CS_MASK)) return false;
                    }
                    else if (x != (byte)secondPtr[i])
                    {
                        // non-alpha mismatch
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public static long HashCI(scoped ReadOnlySpan<byte> value)
        => HashCS(value) & CaseMask;

    public static long HashCS(scoped ReadOnlySpan<byte> value)
    {
        // at least 8? we can blit
        if ((value.Length >> 3) != 0)
        {
            if (BitConverter.IsLittleEndian) return MemoryMarshal.Read<long>(value);
            return BinaryPrimitives.ReadInt64LittleEndian(value);
        }

        // small (<7); manual loop
        // note: profiling with unsafe code to pick out elements: slower
        ulong tally = 0;
        for (int i = 0; i < value.Length; i++)
        {
            tally |= ((ulong)value[i]) << (i << 3);
        }
        return (long)tally;
    }

    public static long HashCS(scoped ReadOnlySpan<char> value)
    {
        // note: BDN profiling with Vector64.Narrow showed no benefit
        if (value.Length > 8)
        {
            // slice if necessary, so we can use bounds-elided foreach
            if (value.Length != 8) value = value.Slice(0, 8);
        }
        ulong tally = 0;
        for (int i = 0; i < value.Length; i++)
        {
            tally |= ((ulong)value[i]) << (i << 3);
        }
        return (long)tally;
    }

    public static long HashCI(scoped ReadOnlySpan<char> value) => HashCS(value) & CaseMask;
}
