using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Internal;

internal static partial class Constants
{
    public static ReadOnlySpan<byte> CrlfBytes => "\r\n"u8;

    public static readonly ushort CrLfUInt16 = UnsafeCpuUInt16(CrlfBytes);

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

    public const int MaxRawBytesInt32 = 11, // includes -
        MaxProtocolBytesIntegerInt32 = MaxRawBytesInt32 + 3, // ?X10X\r\n where ? could be $, *, etc - usually a length prefix
        MaxProtocolBytesBulkStringIntegerInt32 = MaxRawBytesInt32 + 7; // $NN\r\nX10X\r\n for NN (length) 1-10
        /*
                    MaxBytesInt64 = 26, // $19\r\nX19X\r\n
                    MaxBytesSingle = 27; // $NN\r\nX...X\r\n - note G17 format, allow 20 for payload
    */
}
