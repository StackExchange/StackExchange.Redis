using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Buffers;
using RESPite.Internal;
using RESPite.Messages;
using static RESPite.Internal.Constants;

namespace RESPite.Resp;

/// <summary>
/// Provides common RESP reader implementations.
/// </summary>
public static class RespReaders
{
    private static readonly Impl common = new();

    /// <summary>
    /// Reads <see cref="String"/> payloads.
    /// </summary>
    public static IReader<Empty, string?> String => common;

    /// <summary>
    /// Reads <see cref="Int32"/> payloads.
    /// </summary>
    public static IReader<Empty, int> Int32 => common;

    /// <summary>
    /// Reads 'OK' acknowledgements.
    /// </summary>
    public static IReader<Empty, Empty> OK => common;

    /// <summary>
    /// Reads arrays of opaque payloads.
    /// </summary>
    public static IReader<Empty, RefCountedBuffers<byte>> Strings => common;

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    public static IReader<string, string> Pong { get; } = new PongReader();

    internal static void ThrowMissingExpected(in ReadOnlySequence<byte> content, string expected, [CallerMemberName] string caller = "")
    {
#if DEBUG
        throw new InvalidOperationException($"Did not receive expected response: '{expected}'; got '{UTF8.GetString(content)}' from {caller}");
#else
        throw new InvalidOperationException($"Did not receive expected response: '{expected}'");
#endif
    }

    internal sealed class Impl :
        IReader<Empty, Empty>,
        IReader<Empty, string?>,
        IReader<Empty, int>,
        IReader<Empty, RefCountedBuffers<byte>>
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
                if (span.Length != 5 || !(UnsafeCpuUInt32(span) == OK_HiNibble & UnsafeCpuByte(span, 4) == (byte)'\n')) ThrowMissingExpected(content, "+OK");
            }
            else
            {
                Slower(content);
            }
            return default;

            static Empty Slower(scoped in ReadOnlySequence<byte> content)
            {
                var reader = new RespReader(content);
                if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.IsOK())) ThrowMissingExpected(content, "+OK");
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
                switch (span.Length)
                {
                    case 4: // :N\r\n
                        if ((UnsafeCpuUInt32(span) & SingleCharScalarMask) == SingleDigitInteger)
                        {
                            return Digit(UnsafeCpuByte(span, 1));
                        }
                        break;
                    case 5: // :NN\r\n
                        if ((UnsafeCpuUInt32(span) & DoubleCharScalarMask) == DoubleDigitInteger
                            & UnsafeCpuByte(span, 4) == (byte)'\n')
                        {
                            return (10 * Digit(UnsafeCpuByte(span, 1)))
                                + Digit(UnsafeCpuByte(span, 2));
                        }
                        break;
                    case 7: // $1\r\nN\r\n
                        if (UnsafeCpuUInt32(span) == BulkSingleDigitPrefix
                            && UnsafeCpuUInt16(span, 5) == CrLfUInt16)
                        {
                            return Digit(UnsafeCpuByte(span, 4));
                        }
                        break;
                    case 8: // $2\r\nNN\r\n
                        if (UnsafeCpuUInt32(span) == BulkDoubleDigitPrefix
                            && UnsafeCpuUInt16(span, 6) == CrLfUInt16)
                        {
                            return (10 * Digit(UnsafeCpuByte(span, 4)))
                                + Digit(UnsafeCpuByte(span, 5));
                        }
                        break;
                }
                static int Digit(byte value)
                {
                    var i = value - '0';
                    if (i < 0 | i > 9) ThrowFormat();
                    return i;
                }
            }
            var reader = new RespReader(in content);
            if (!(reader.TryReadNext() && reader.IsScalar)) ThrowFormat();
            return reader.ReadInt32();
        }

        RefCountedBuffers<byte> IReader<Empty, RefCountedBuffers<byte>>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(in content);
            if (!(reader.TryReadNext() && !reader.IsAggregate)) ThrowFormat();
            return ReadAggregateAsRefCountedBuffers(ref reader);
        }

        private static readonly uint
                SingleCharScalarMask = CpuUInt32(0xFF00FFFF),
                DoubleCharScalarMask = CpuUInt32(0xFF0000FF),
                SingleDigitInteger = UnsafeCpuUInt32(":\0\r\n"u8),
                DoubleDigitInteger = UnsafeCpuUInt32(":\0\0\r"u8),
                BulkSingleDigitPrefix = UnsafeCpuUInt32("$1\r\n"u8),
                BulkDoubleDigitPrefix = UnsafeCpuUInt32("$2\r\n"u8);
    }

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    private sealed class PongReader : IReader<Empty, Empty>, IReader<string, string>
    {
        internal PongReader() { }

        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.Is("PONG"u8))) ThrowMissingExpected(in content, "PONG");
            return default;
        }
        string IReader<string, string>.Read(in string request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            if (!reader.TryReadNext(RespPrefix.BulkString)) ThrowMissingExpected(in content, request);
            return reader.ReadString()!;
        }
    }

    internal static RefCountedBuffers<byte> ReadAggregateAsRefCountedBuffers(ref RespReader reader)
    {
        Debug.Assert(reader.IsAggregate, "should have already checked for aggregate");
        if (reader.IsNull) return RefCountedBuffers<byte>.Null;

        var count = reader.ChildCount;
        if (count == 0) return RefCountedBuffers<byte>.Empty;

        var builder = new RefCountedBuffers<byte>.Builder(count, clear: false);
        var buffer = new BufferCore<byte>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (!reader.TryReadNext(RespPrefix.BulkString)) ThrowFormat();

                builder.SetLength(i, reader.CopyTo(ref buffer));
            }
            return builder.Create(buffer.Detach());
        }
        finally
        {
            // can't use "using" because that creates a snapshot (BufferCore is mutable, and used "ref")
            buffer.Dispose();
        }
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFormat() => throw new FormatException();

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUnableToLease() => throw new InvalidOperationException("Unable to lease memory for " + nameof(Strings));
}
