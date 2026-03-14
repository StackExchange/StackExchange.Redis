using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Enum,
    AllowMultiple = false,
    Inherited = false)]
[Conditional("DEBUG")] // evaporate in release
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public sealed partial class AsciiHashAttribute(string token = "") : Attribute
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

// note: instance members are in AsciiHash.Instance.cs.
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public readonly partial struct AsciiHash
{
    /// <summary>
    /// In-place ASCII upper-case conversion.
    /// </summary>
    public static void ToUpper(Span<byte> span)
    {
        foreach (ref var b in span)
        {
            if (b >= 'a' && b <= 'z')
                b = (byte)(b & ~0x20);
        }
    }

    /// <summary>
    /// In-place ASCII lower-case conversion.
    /// </summary>
    public static void ToLower(Span<byte> span)
    {
        foreach (ref var b in span)
        {
            if (b >= 'a' && b <= 'z')
                b |= (byte)(b & ~0x20);
        }
    }

    internal const int MaxBytesHashed = sizeof(long);

    public static bool EqualsCS(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality
        return len <= MaxBytesHashed ? HashCS(first) == HashCS(second) : first.SequenceEqual(second);
    }

    public static bool SequenceEqualsCS(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        => first.SequenceEqual(second);

    public static bool EqualsCI(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the UC hash performs CI equality
        return len <= MaxBytesHashed ? HashUC(first) == HashUC(second) : SequenceEqualsCI(first, second);
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
        return len <= MaxBytesHashed ? HashCS(first) == HashCS(second) : first.SequenceEqual(second);
    }

    public static bool SequenceEqualsCS(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        => first.SequenceEqual(second);

    public static bool EqualsCI(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var len = first.Length;
        if (len != second.Length) return false;
        // for very short values, the CS hash performs CS equality; check that first
        return len <= MaxBytesHashed ? HashUC(first) == HashUC(second) : SequenceEqualsCI(first, second);
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

    public static void Hash(scoped ReadOnlySpan<byte> value, out long cs, out long uc)
    {
        cs = HashCS(value);
        uc = ToUC(cs);
    }

    public static void Hash(scoped ReadOnlySpan<char> value, out long cs, out long uc)
    {
        cs = HashCS(value);
        uc = ToUC(cs);
    }

    public static long HashUC(scoped ReadOnlySpan<byte> value) => ToUC(HashCS(value));

    public static long HashUC(scoped ReadOnlySpan<char> value) => ToUC(HashCS(value));

    internal static long ToUC(long hashCS)
    {
        const long LC_MASK = 0x2020_2020_2020_2020;
        // check whether there are any possible lower-case letters;
        // this would be anything with the 0x20 bit set
        if ((hashCS & LC_MASK) == 0) return hashCS;

        // Something looks possibly lower-case; we can't just mask it off,
        // because there are other non-alpha characters in that range.
#if NET
        ToUpper(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref hashCS), sizeof(long)));
        return hashCS;
#else
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, hashCS);
        ToUpper(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
#endif
    }

    public static long HashCS(scoped ReadOnlySpan<byte> value)
    {
        // at least 8? we can blit
        if ((value.Length >> 3) != 0) return BinaryPrimitives.ReadInt64LittleEndian(value);

        // small (<7); manual loop
        // note: profiling with unsafe code to pick out elements: much slower
        // note: profiling with overstamping a local: 3x slower
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
        if ((value.Length >> 3) != 0)
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

    public static void HashCS(scoped ReadOnlySpan<byte> value, out long cs0, out long cs1)
    {
        cs0 = HashCS(value);
        cs1 = value.Length > MaxBytesHashed ? HashCS(value.Slice(start: MaxBytesHashed)) : 0;
    }

    public static void HashCS(scoped ReadOnlySpan<char> value, out long cs0, out long cs1)
    {
        cs0 = HashCS(value);
        cs1 = value.Length > MaxBytesHashed ? HashCS(value.Slice(start: MaxBytesHashed)) : 0;
    }

    public static void HashUC(scoped ReadOnlySpan<byte> value, out long cs0, out long cs1)
    {
        cs0 = HashUC(value);
        cs1 = value.Length > MaxBytesHashed ? HashUC(value.Slice(start: MaxBytesHashed)) : 0;
    }

    public static void HashUC(scoped ReadOnlySpan<char> value, out long cs0, out long cs1)
    {
        cs0 = HashUC(value);
        cs1 = value.Length > MaxBytesHashed ? HashUC(value.Slice(start: MaxBytesHashed)) : 0;
    }

    public static void Hash(scoped ReadOnlySpan<byte> value, out long cs0, out long uc0, out long cs1, out long uc1)
    {
        Hash(value, out cs0, out uc0);
        if (value.Length > MaxBytesHashed)
        {
            Hash(value.Slice(start: MaxBytesHashed), out cs1, out uc1);
        }
        else
        {
            cs1 = uc1 = 0;
        }
    }

    public static void Hash(scoped ReadOnlySpan<char> value, out long cs0, out long uc0, out long cs1, out long uc1)
    {
        Hash(value, out cs0, out uc0);
        if (value.Length > MaxBytesHashed)
        {
            Hash(value.Slice(start: MaxBytesHashed), out cs1, out uc1);
        }
        else
        {
            cs1 = uc1 = 0;
        }
    }
}
