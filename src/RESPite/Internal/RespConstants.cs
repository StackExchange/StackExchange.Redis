using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
// ReSharper disable InconsistentNaming
namespace RESPite.Internal;

internal static class RespConstants
{
    public static readonly UTF8Encoding UTF8 = new(false);

    public static ReadOnlySpan<byte> CrlfBytes => "\r\n"u8;

    public static readonly ushort CrLfUInt16 = UnsafeCpuUInt16(CrlfBytes);

    public static ReadOnlySpan<byte> OKBytes_LC => "ok"u8;
    public static ReadOnlySpan<byte> OKBytes => "OK"u8;
    public static readonly ushort OKUInt16 = UnsafeCpuUInt16(OKBytes);
    public static readonly ushort OKUInt16_LC = UnsafeCpuUInt16(OKBytes_LC);

    public static readonly uint BulkStringStreaming = UnsafeCpuUInt32("$?\r\n"u8);
    public static readonly uint BulkStringNull = UnsafeCpuUInt32("$-1\r"u8);

    public static readonly uint ArrayStreaming = UnsafeCpuUInt32("*?\r\n"u8);
    public static readonly uint ArrayNull = UnsafeCpuUInt32("*-1\r"u8);

    public static ushort UnsafeCpuUInt16(ReadOnlySpan<byte> bytes)
        => Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(bytes));
    public static ushort UnsafeCpuUInt16(ReadOnlySpan<byte> bytes, int offset)
        => Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref MemoryMarshal.GetReference(bytes), offset));
    public static byte UnsafeCpuByte(ReadOnlySpan<byte> bytes, int offset)
        => Unsafe.Add(ref MemoryMarshal.GetReference(bytes), offset);
    public static uint UnsafeCpuUInt32(ReadOnlySpan<byte> bytes)
        => Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(bytes));
    public static uint UnsafeCpuUInt32(ReadOnlySpan<byte> bytes, int offset)
        => Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(bytes), offset));
    public static ulong UnsafeCpuUInt64(ReadOnlySpan<byte> bytes)
        => Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(bytes));
    public static ushort CpuUInt16(ushort bigEndian)
        => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(bigEndian) : bigEndian;
    public static uint CpuUInt32(uint bigEndian)
        => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(bigEndian) : bigEndian;
    public static ulong CpuUInt64(ulong bigEndian)
        => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(bigEndian) : bigEndian;

    public const int MaxRawBytesInt32 = 11, // "-2147483648"
        MaxRawBytesInt64 = 20, // "-9223372036854775808",
        MaxProtocolBytesIntegerInt32 = MaxRawBytesInt32 + 3, // ?X10X\r\n where ? could be $, *, etc - usually a length prefix
        MaxProtocolBytesBulkStringIntegerInt32 = MaxRawBytesInt32 + 7, // $NN\r\nX11X\r\n for NN (length) 1-11
        MaxProtocolBytesBulkStringIntegerInt64 = MaxRawBytesInt64 + 7, // $NN\r\nX20X\r\n for NN (length) 1-20
        // The stack buffer for parsing a number out of a bulk string - doubles and decimals. 20 was too
        // small for the case it named: a G17 double is up to 24 characters on its own
        // ("-1.2345678901234567E-308"), and the server is worse - INCRBYFLOAT does LONG double arithmetic
        // and replies with the full expansion, so "12.134 - 14561.0000002" comes back as
        // "-14548.86600019999999933", which is 24 bytes and was rejected outright as "invalid format".
        // 64 covers realistic replies with room to spare and still costs nothing: it is a stackalloc, and
        // the length guard stays so that a pathological reply throws rather than parsing a truncation.
        MaxRawBytesNumber = 64,
        MaxProtocolBytesBytesNumber = MaxRawBytesNumber + 7; // $NN\r\nX...X\r\n for NN (length)
}
