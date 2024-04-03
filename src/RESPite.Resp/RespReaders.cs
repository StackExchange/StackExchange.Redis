using RESPite.Messages;
using System;
using System.Buffers;
using static RESPite.Internal.Constants;

namespace RESPite.Resp;

/// <summary>
/// Provides common RESP reader implementations
/// </summary>
public static class RespReaders
{
    private static readonly Impl common = new();
    ///// <summary>
    ///// Reads <see cref="String"/> payloads
    ///// </summary>
    //public static IRespReader<Empty, string?> String => common;
    /// <summary>
    /// Reads <see cref="Int32"/> payloads
    /// </summary>
    public static IReader<Empty, int> Int32 => common;
    /// <summary>
    /// Reads 'OK' acknowledgements
    /// </summary>
    public static IReader<Empty, Empty> OK => common;

    /// <summary>
    /// Reads PONG responses
    /// </summary>
    public static PongReader Pong { get; } = new();
    internal sealed class Impl : IReader<Empty, Empty>, IReader<Empty, string?>, IReader<Empty, int>
    {
        private static readonly uint OK_HiNibble = UnsafeCpuUInt32("+OK\r"u8);
        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment)
            {
#if NETCOREAPP3_1_OR_GREATER
                var span = content.FirstSpan;
#else
                var span = content.First.Span;
#endif
                if (span.Length != 5 || !(UnsafeCpuUInt32(span) == OK_HiNibble & UnsafeCpuByte(span, 4) == (byte)'\n')) Throw();
            }
            else
            {
                Slower(content);
            }
            return default;

            static void Throw() => throw new InvalidOperationException("Did not receive expected response: '+OK'");
            static Empty Slower(scoped in ReadOnlySequence<byte> content)
            {
                var reader = new RespReader(content);
                if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.IsOK())) Throw();
                return default;
            }
        }

        string? IReader<Empty, string?>.Read(in Empty request, in ReadOnlySequence<byte> content)
            => new RespReader(in content).ReadString();

        int IReader<Empty, int>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment)
            {
#if NETCOREAPP3_1_OR_GREATER
                var span = content.FirstSpan;
#else
                var span = content.First.Span;
#endif
                if (span.Length == 4)
                {
                    if ((UnsafeCpuUInt32(span) & SingleCharScalarMask) == SingleDigitInteger)
                    {
                        var value = UnsafeCpuByte(span, 1) - '0';
                        if (value < 0 | value > 9) Throw();
                        return value;
                    }
                }
                
            }
            var reader = new RespReader(in content);
            if (!(reader.TryReadNext() && reader.IsScalar)) Throw();
            return reader.ReadInt32();

            static void Throw() => throw new FormatException();
        }
        private static readonly uint SingleCharScalarMask = CpuUInt32(0xFF00FFFF),
                SingleDigitInteger = UnsafeCpuUInt32(":\0\r\n"u8);
    }
    /// <summary>
    /// Reads PONG responses
    /// </summary>
    public sealed class PongReader : IReader<Empty, Empty>, IReader<string, string>
    {
        internal PongReader() { }
        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.Is("PONG"u8))) Throw();
            return default;
        }
        private static void Throw() => throw new InvalidOperationException("Did not receive expected response: 'PONG'");
        string IReader<string, string>.Read(in string request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            if (!reader.TryReadNext(RespPrefix.BulkString)) Throw();
            return reader.ReadString()!;
        }
        //string IReader<Empty, string>.Read(in Empty request, in ReadOnlySequence<byte> content)
        //{
        //    var reader = new RespReader(content);
        //    if (!reader.TryReadNext(RespPrefix.BulkString)) Throw();
        //    return reader.ReadString()!;
        //}
    }
}
