using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite.Internal;
using RESPite.Messages;
using static RESPite.Internal.Constants;

namespace RESPite.Resp.Readers;

/// <summary>
/// Provides common RESP reader implementations.
/// </summary>
internal static class RespReaders
{
    internal static readonly Impl Common = new();

    /// <summary>
    /// Reads <see cref="String"/> payloads.
    /// </summary>
    public static IRespReader<Empty, string?> String => Common;

    /// <summary>
    /// Reads <see cref="Int32"/> payloads.
    /// </summary>
    public static IRespReader<Empty, int> Int32 => Common;

    /// <summary>
    /// Reads <see cref="Nullable{Int32}"/> payloads.
    /// </summary>
    public static IRespReader<Empty, int?> NullableInt32 => Common;

    /// <summary>
    /// Reads <see cref="Int64"/> payloads.
    /// </summary>
    public static IRespReader<Empty, long> Int64 => Common;

    /// <summary>
    /// Reads <see cref="Nullable{Int64}"/> payloads.
    /// </summary>
    public static IRespReader<Empty, long?> NullableInt64 => Common;

    /// <summary>
    /// Reads 'OK' acknowledgements.
    /// </summary>
    public static IRespReader<Empty, Empty> OK => Common;

    /// <summary>
    /// Reads <see cref="LeasedString" /> payloads.
    /// </summary>
    public static IRespReader<Empty, LeasedString> LeasedString => Common;

    /// <summary>
    /// Reads arrays of opaque payloads.
    /// </summary>
    public static IRespReader<Empty, LeasedStrings> LeasedStrings => Common;

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    public static IRespReader<Empty, Empty> Pong => PongReader.Instance;

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    public static IRespReader<string, Empty> Echo => PongReader.Instance;

    internal static void ThrowMissingExpected(string expected, [CallerMemberName] string caller = "")
        => throw new InvalidOperationException($"Did not receive expected response: '{expected}'");

    internal sealed class Impl :
        IRespReader<Empty, Empty>,
        IRespReader<Empty, string?>,
        IRespReader<Empty, int>,
        IRespReader<Empty, int?>,
        IRespReader<Empty, long>,
        IRespReader<Empty, long?>,
        IRespReader<Empty, LeasedStrings>,
        IRespReader<Empty, LeasedString>
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
                if (span.Length != 5 || !(UnsafeCpuUInt32(span) == OK_HiNibble & UnsafeCpuByte(span, 4) == (byte)'\n')) ThrowMissingExpected("OK");
            }
            else
            {
                Slower(content);
            }
            return default;

            static Empty Slower(scoped in ReadOnlySequence<byte> content)
            {
                var reader = new RespReader(content, throwOnErrorResponse: true);
                if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.IsOK())) ThrowMissingExpected("OK");
                return default;
            }
        }

        Empty IRespReader<Empty, Empty>.Read(in Empty request, ref RespReader reader)
        {
            if (!(reader.TryReadNext(RespPrefix.SimpleString) && reader.IsOK())) ThrowMissingExpected("OK");
            return default;
        }

        string? IRespReader<Empty, string?>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            return reader.ReadString();
        }

        string? IReader<Empty, string?>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            return reader.ReadString();
        }

        long IReader<Empty, long>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment && content.Length <= 12) // 9 chars for pre-billion integers, plus 3 protocol chars
            {
                return ((IReader<Empty, int>)this).Read(request, content);
            }
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadInt64();
        }

        long? IReader<Empty, long?>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment && content.Length <= 12) // 9 chars for pre-billion integers, plus 3 protocol chars
            {
                return ((IReader<Empty, int?>)this).Read(request, content);
            }
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadInt64();
        }

        long IRespReader<Empty, long>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadInt64();
        }

        long? IRespReader<Empty, long?>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadInt64();
        }

        int IRespReader<Empty, int>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadInt32();
        }

        int? IRespReader<Empty, int?>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadInt32();
        }

        LeasedString IReader<Empty, LeasedString>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(in content);
            reader.ReadNextScalar();
            return reader.ReadLeasedString();
        }

        LeasedString IRespReader<Empty, LeasedString>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            return reader.ReadLeasedString();
        }

        private static bool TryReadFastInt32(ReadOnlySpan<byte> span, out int value)
        {
            switch (span.Length)
            {
                case 4: // :N\r\n
                    if ((UnsafeCpuUInt32(span) & SingleCharScalarMask) == SingleDigitInteger)
                    {
                        value = Digit(UnsafeCpuByte(span, 1));
                        return true;
                    }
                    break;
                case 5: // :NN\r\n
                    if ((UnsafeCpuUInt32(span) & DoubleCharScalarMask) == DoubleDigitInteger
                        & UnsafeCpuByte(span, 4) == (byte)'\n')
                    {
                        value = (10 * Digit(UnsafeCpuByte(span, 1)))
                            + Digit(UnsafeCpuByte(span, 2));
                        return true;
                    }
                    break;
                case 7: // $1\r\nN\r\n
                    if (UnsafeCpuUInt32(span) == BulkSingleDigitPrefix
                        && UnsafeCpuUInt16(span, 5) == CrLfUInt16)
                    {
                        value = Digit(UnsafeCpuByte(span, 4));
                        return true;
                    }
                    break;
                case 8: // $2\r\nNN\r\n
                    if (UnsafeCpuUInt32(span) == BulkDoubleDigitPrefix
                        && UnsafeCpuUInt16(span, 6) == CrLfUInt16)
                    {
                        value = (10 * Digit(UnsafeCpuByte(span, 4)))
                            + Digit(UnsafeCpuByte(span, 5));
                        return true;
                    }
                    break;
            }
            value = default;
            return false;

            static int Digit(byte value)
            {
                var i = value - '0';
                if (i < 0 | i > 9) ThrowFormat();
                return i;
            }
        }

        int IReader<Empty, int>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment)
            {
#if NETCOREAPP3_1_OR_GREATER
                var span = content.FirstSpan;
#else
                var span = content.First.Span;
#endif
                if (TryReadFastInt32(span, out int i)) return i;
            }
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadInt32();
        }

        int? IReader<Empty, int?>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            if (content.IsSingleSegment)
            {
#if NETCOREAPP3_1_OR_GREATER
                var span = content.FirstSpan;
#else
                var span = content.First.Span;
#endif
                if (TryReadFastInt32(span, out int i)) return i;
            }
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadInt32();
        }

        LeasedStrings IReader<Empty, LeasedStrings>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(in content, throwOnErrorResponse: true);
            reader.ReadNextAggregate();
            return reader.ReadLeasedStrings();
        }

        LeasedStrings IRespReader<Empty, LeasedStrings>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextAggregate();
            return reader.ReadLeasedStrings();
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
    /// Reads values as an enum of type <typeparamref name="T"/>.
    /// </summary>
    public sealed class EnumReader<T> : IRespReader<Empty, T>, IRespReader<Empty, T?> where T : struct, Enum
    {
        /// <summary>
        /// Gets the reader instance.
        /// </summary>
        public static EnumReader<T> Instance { get; } = new();

        private EnumReader() { }

        T IReader<Empty, T>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            RespReader reader = new(content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadEnum<T>(default);
        }

        T? IReader<Empty, T?>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            RespReader reader = new(content, throwOnErrorResponse: true);
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadEnum<T>(default);
        }

        T IRespReader<Empty, T>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            reader.DemandNotNull();
            return reader.ReadEnum<T>(default);
        }

        T? IRespReader<Empty, T?>.Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            return reader.IsNull ? null : reader.ReadEnum<T>(default);
        }
    }

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    private sealed class PongReader : IRespReader<Empty, Empty>, IRespReader<string, Empty>
    {
        internal static readonly PongReader Instance = new();
        private PongReader() { }

        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content, throwOnErrorResponse: true);
            return Read(in request, ref reader);
        }

        Empty IReader<string, Empty>.Read(in string request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content, throwOnErrorResponse: true);
            return Read(in request, ref reader);
        }

        public Empty Read(in Empty request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            reader.Demand(RespPrefix.SimpleString);
            if (!reader.Is("PONG"u8)) ThrowMissingExpected("PONG");
            return default;
        }

        public Empty Read(in string request, ref RespReader reader)
        {
            reader.ReadNextScalar();
            reader.Demand(RespPrefix.BulkString);
            var value = reader.ReadString()!;
            if (value != request) ThrowMissingExpected(request);
            return Empty.Value;
        }
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFormat() => throw new FormatException();
}
