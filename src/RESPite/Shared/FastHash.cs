using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite;

/// <summary>
/// This type is intended to provide fast hashing functions for small strings, for example well-known
/// RESP literals that are usually identifiable by their length and initial bytes; it is not intended
/// for general purpose hashing. All matches must also perform a sequence equality check.
/// </summary>
/// <remarks>See HastHashGenerator.md for more information and intended usage.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("DEBUG")] // evaporate in release
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public sealed class FastHashAttribute(string token = "") : Attribute
{
    public string Token => token;
}

[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public readonly struct FastHash
{
    private readonly long _hashCI;
    private readonly long _hashCS;
    private readonly ReadOnlyMemory<byte> _value;

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

    public static long HashCS(ReadOnlySequence<byte> value)
    {
#if NETCOREAPP3_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        var first = value.FirstSpan;
#else
        var first = value.First.Span;
#endif
        return first.Length >= MaxBytesHashed || value.IsSingleSegment
            ? HashCS(first) : SlowHashCS(value);

        static long SlowHashCS(ReadOnlySequence<byte> value)
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
                const int CS_MASK = ~0x20;
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

    public static long HashCI(scoped ReadOnlySpan<byte> value)
        => HashCS(value) & CaseMask;

    public static long HashCS(scoped ReadOnlySpan<byte> value)
    {
        if (BitConverter.IsLittleEndian)
        {
            ref byte data = ref MemoryMarshal.GetReference(value);
            return value.Length switch
            {
                0 => 0,
                1 => data, // 0000000A
                2 => Unsafe.ReadUnaligned<ushort>(ref data), // 000000BA
                3 => Unsafe.ReadUnaligned<ushort>(ref data) | // 000000BA
                     (Unsafe.Add(ref data, 2) << 16), // 00000C00
                4 => Unsafe.ReadUnaligned<uint>(ref data), // 0000DCBA
                5 => Unsafe.ReadUnaligned<uint>(ref data) | // 0000DCBA
                     ((long)Unsafe.Add(ref data, 4) << 32), // 000E0000
                6 => Unsafe.ReadUnaligned<uint>(ref data) | // 0000DCBA
                     ((long)Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref data, 4)) << 32), // 00FE0000
                7 => Unsafe.ReadUnaligned<uint>(ref data) | // 0000DCBA
                     ((long)Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref data, 4)) << 32) | // 00FE0000
                     ((long)Unsafe.Add(ref data, 6) << 48), // 0G000000
                _ => Unsafe.ReadUnaligned<long>(ref data), // HGFEDCBA
            };
        }

#pragma warning disable CS0618 // Type or member is obsolete
        return Hash64Fallback(value);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    [Obsolete("Only exists for benchmarks (to show that we don't need to use it) and unit tests (for correctness)")]
    internal static unsafe long Hash64Unsafe(scoped ReadOnlySpan<byte> value)
    {
        if (BitConverter.IsLittleEndian)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(value))
            {
                return value.Length switch
                {
                    0 => 0,
                    1 => *ptr, // 0000000A
                    2 => *(ushort*)ptr, // 000000BA
                    3 => *(ushort*)ptr | // 000000BA
                         (ptr[2] << 16), // 00000C00
                    4 => *(int*)ptr, // 0000DCBA
                    5 => (long)*(int*)ptr | // 0000DCBA
                         ((long)ptr[4] << 32), // 000E0000
                    6 => (long)*(int*)ptr | // 0000DCBA
                         ((long)*(ushort*)(ptr + 4) << 32), // 00FE0000
                    7 => (long)*(int*)ptr | // 0000DCBA
                         ((long)*(ushort*)(ptr + 4) << 32) | // 00FE0000
                         ((long)ptr[6] << 48), // 0G000000
                    _ => *(long*)ptr, // HGFEDCBA
                };
            }
        }

        return Hash64Fallback(value);
    }

    [Obsolete("Only exists for unit tests and fallback")]
    internal static long Hash64Fallback(scoped ReadOnlySpan<byte> value)
    {
        if (value.Length < sizeof(long))
        {
            Span<byte> tmp = stackalloc byte[sizeof(long)];
            value.CopyTo(tmp); // ABC*****
            tmp.Slice(value.Length).Clear(); // ABC00000
            return BinaryPrimitives.ReadInt64LittleEndian(tmp); // 00000CBA
        }

        return BinaryPrimitives.ReadInt64LittleEndian(value); // HGFEDCBA
    }
}
