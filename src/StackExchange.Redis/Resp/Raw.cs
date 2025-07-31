using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace StackExchange.Redis.Resp;

/// <summary>
/// Pre-computed payload fragments, for high-volume scenarios / common values.
/// </summary>
/// <remarks>
/// CPU-endianness applies here; we can't just use "const" - however, modern JITs treat "static readonly" *almost* the same as "const", so: meh.
/// </remarks>
internal static class Raw
{
    public static ulong Create64(ReadOnlySpan<byte> bytes, int length)
    {
        if (length != bytes.Length)
        {
            throw new ArgumentException($"Length check failed: {length} vs {bytes.Length}, value: {RespConstants.UTF8.GetString(bytes)}", nameof(length));
        }
        if (length < 0 || length > sizeof(ulong))
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Invalid length {length} - must be 0-{sizeof(ulong)}");
        }

        // this *will* be aligned; this approach intentionally chosen for parity with write
        Span<byte> scratch = stackalloc byte[sizeof(ulong)];
        if (length != sizeof(ulong)) scratch.Slice(length).Clear();
        bytes.CopyTo(scratch);
        return Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(scratch));
    }

    public static uint Create32(ReadOnlySpan<byte> bytes, int length)
    {
        if (length != bytes.Length)
        {
            throw new ArgumentException($"Length check failed: {length} vs {bytes.Length}, value: {RespConstants.UTF8.GetString(bytes)}", nameof(length));
        }
        if (length < 0 || length > sizeof(uint))
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Invalid length {length} - must be 0-{sizeof(uint)}");
        }

        // this *will* be aligned; this approach intentionally chosen for parity with write
        Span<byte> scratch = stackalloc byte[sizeof(uint)];
        if (length != sizeof(uint)) scratch.Slice(length).Clear();
        bytes.CopyTo(scratch);
        return Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(scratch));
    }

    public static ulong BulkStringEmpty_6 = Create64("$0\r\n\r\n"u8, 6);

    public static ulong BulkStringInt32_M1_8 = Create64("$2\r\n-1\r\n"u8, 8);
    public static ulong BulkStringInt32_0_7 = Create64("$1\r\n0\r\n"u8, 7);
    public static ulong BulkStringInt32_1_7 = Create64("$1\r\n1\r\n"u8, 7);
    public static ulong BulkStringInt32_2_7 = Create64("$1\r\n2\r\n"u8, 7);
    public static ulong BulkStringInt32_3_7 = Create64("$1\r\n3\r\n"u8, 7);
    public static ulong BulkStringInt32_4_7 = Create64("$1\r\n4\r\n"u8, 7);
    public static ulong BulkStringInt32_5_7 = Create64("$1\r\n5\r\n"u8, 7);
    public static ulong BulkStringInt32_6_7 = Create64("$1\r\n6\r\n"u8, 7);
    public static ulong BulkStringInt32_7_7 = Create64("$1\r\n7\r\n"u8, 7);
    public static ulong BulkStringInt32_8_7 = Create64("$1\r\n8\r\n"u8, 7);
    public static ulong BulkStringInt32_9_7 = Create64("$1\r\n9\r\n"u8, 7);
    public static ulong BulkStringInt32_10_8 = Create64("$2\r\n10\r\n"u8, 8);

    public static ulong BulkStringPrefix_M1_5 = Create64("$-1\r\n"u8, 5);
    public static uint BulkStringPrefix_0_4 = Create32("$0\r\n"u8, 4);
    public static uint BulkStringPrefix_1_4 = Create32("$1\r\n"u8, 4);
    public static uint BulkStringPrefix_2_4 = Create32("$2\r\n"u8, 4);
    public static uint BulkStringPrefix_3_4 = Create32("$3\r\n"u8, 4);
    public static uint BulkStringPrefix_4_4 = Create32("$4\r\n"u8, 4);
    public static uint BulkStringPrefix_5_4 = Create32("$5\r\n"u8, 4);
    public static uint BulkStringPrefix_6_4 = Create32("$6\r\n"u8, 4);
    public static uint BulkStringPrefix_7_4 = Create32("$7\r\n"u8, 4);
    public static uint BulkStringPrefix_8_4 = Create32("$8\r\n"u8, 4);
    public static uint BulkStringPrefix_9_4 = Create32("$9\r\n"u8, 4);
    public static ulong BulkStringPrefix_10_5 = Create64("$10\r\n"u8, 5);

    public static ulong ArrayPrefix_M1_5 = Create64("*-1\r\n"u8, 5);
    public static uint ArrayPrefix_0_4 = Create32("*0\r\n"u8, 4);
    public static uint ArrayPrefix_1_4 = Create32("*1\r\n"u8, 4);
    public static uint ArrayPrefix_2_4 = Create32("*2\r\n"u8, 4);
    public static uint ArrayPrefix_3_4 = Create32("*3\r\n"u8, 4);
    public static uint ArrayPrefix_4_4 = Create32("*4\r\n"u8, 4);
    public static uint ArrayPrefix_5_4 = Create32("*5\r\n"u8, 4);
    public static uint ArrayPrefix_6_4 = Create32("*6\r\n"u8, 4);
    public static uint ArrayPrefix_7_4 = Create32("*7\r\n"u8, 4);
    public static uint ArrayPrefix_8_4 = Create32("*8\r\n"u8, 4);
    public static uint ArrayPrefix_9_4 = Create32("*9\r\n"u8, 4);
    public static ulong ArrayPrefix_10_5 = Create64("*10\r\n"u8, 5);

#if NETCOREAPP3_0_OR_GREATER
    private static uint FirstAndLast(char first, char last)
    {
        Debug.Assert(first < 128 && last < 128, "ASCII please");
        Span<byte> scratch = [(byte)first, 0, 0, (byte)last];
        // this *will* be aligned; this approach intentionally chosen for how we read
        return Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(scratch));
    }

    public const int CommonRespIndex_Success = 0;
    public const int CommonRespIndex_SingleDigitInteger = 1;
    public const int CommonRespIndex_DoubleDigitInteger = 2;
    public const int CommonRespIndex_SingleDigitString = 3;
    public const int CommonRespIndex_DoubleDigitString = 4;
    public const int CommonRespIndex_SingleDigitArray = 5;
    public const int CommonRespIndex_DoubleDigitArray = 6;
    public const int CommonRespIndex_Error = 7;

    public static readonly Vector256<uint> CommonRespPrefixes = Vector256.Create(
        FirstAndLast('+', '\r'), // success                 +OK\r\n
        FirstAndLast(':', '\n'), // single-digit integer    :4\r\n
        FirstAndLast(':', '\r'), // double-digit integer    :42\r\n
        FirstAndLast('$', '\n'), // 0-9 char string         $0\r\n\r\n
        FirstAndLast('$', '\r'), // null/10-99 char string  $-1\r\n or $10\r\nABCDEFGHIJ\r\n
        FirstAndLast('*', '\n'), // 0-9 length array        *0\r\n
        FirstAndLast('*', '\r'), // null/10-99 length array *-1\r\n or *10\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n
        FirstAndLast('-', 'R')); // common errors            -ERR something bad happened

    public static readonly Vector256<uint> FirstLastMask = CreateUInt32(0xFF0000FF);

    private static Vector256<uint> CreateUInt32(uint value)
    {
#if NET7_0_OR_GREATER
        return Vector256.Create<uint>(value);
#else
        return Vector256.Create(value, value, value, value, value, value, value, value);
#endif
    }

#endif
}
