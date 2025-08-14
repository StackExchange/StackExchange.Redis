using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

/// <summary>
/// This type is intended to provide fast hashing functions for small strings, for example well-known
/// RESP literals that are usually identifiable by their length and initial bytes; it is not intended
/// for general purpose hashing. All matches must also perform a sequence equality check.
/// </summary>
/// <remarks>See HastHashGenerator.md for more information and intended usage.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("DEBUG")] // evaporate in release
internal sealed class FastHashAttribute(string token = "") : Attribute
{
    public string Token => token;
}

internal static class FastHash
{
    // Perform case-insensitive hash by masking (X and x differ by only 1 bit); this halves
    // our entropy, but is still useful when case doesn't matter.
    private const long CaseMask = ~0x2020202020202020;

    public static long Hash64CI(this ReadOnlySequence<byte> value)
        => value.Hash64() & CaseMask;

    public static long Hash64(this ReadOnlySequence<byte> value)
    {
#if NETCOREAPP3_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        var first = value.FirstSpan;
#else
        var first = value.First.Span;
#endif
        return first.Length >= sizeof(long) || value.IsSingleSegment
            ? first.Hash64() : SlowHash64(value);

        static long SlowHash64(ReadOnlySequence<byte> value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            if (value.Length < sizeof(long))
            {
                value.CopyTo(buffer);
                buffer.Slice((int)value.Length).Clear();
            }
            else
            {
                value.Slice(0, sizeof(long)).CopyTo(buffer);
            }
            return BitConverter.IsLittleEndian
                ? Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(buffer))
                : BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }
    }

    public static long Hash64CI(this scoped ReadOnlySpan<byte> value)
        => value.Hash64() & CaseMask;

    public static long Hash64(this scoped ReadOnlySpan<byte> value)
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
