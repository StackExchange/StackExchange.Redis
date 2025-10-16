using System.Buffers;
using RESPite.Messages;
using static RESPite.Internal.RespConstants;
namespace RESPite.Internal;

/// <summary>
/// Scans RESP frames.
/// </summary>.
public sealed class RespFrameScanner // : IFrameSacanner<ScanState>, IFrameValidator
{
    /// <summary>
    /// Gets a frame scanner for RESP2 request/response connections, or RESP3 connections.
    /// </summary>
    public static RespFrameScanner Default { get; } = new(false);

    /// <summary>
    /// Gets a frame scanner that identifies RESP2 pub/sub messages.
    /// </summary>
    public static RespFrameScanner Subscription { get; } = new(true);
    private RespFrameScanner(bool pubsub) => _pubsub = pubsub;
    private readonly bool _pubsub;

    private static readonly uint FastNull = UnsafeCpuUInt32("_\r\n\0"u8),
        SingleCharScalarMask = CpuUInt32(0xFF00FFFF),
        SingleDigitInteger = UnsafeCpuUInt32(":\0\r\n"u8),
        EitherBoolean = UnsafeCpuUInt32("#\0\r\n"u8),
        FirstThree = CpuUInt32(0xFFFFFF00);
    private static readonly ulong OK = UnsafeCpuUInt64("+OK\r\n\0\0\0"u8),
        PONG = UnsafeCpuUInt64("+PONG\r\n\0"u8),
        DoubleCharScalarMask = CpuUInt64(0xFF0000FFFF000000),
        DoubleDigitInteger = UnsafeCpuUInt64(":\0\0\r\n"u8),
        FirstFive = CpuUInt64(0xFFFFFFFFFF000000),
        FirstSeven = CpuUInt64(0xFFFFFFFFFFFFFF00);

    private const OperationStatus UseReader = (OperationStatus)(-1);
    private static OperationStatus TryFastRead(ReadOnlySpan<byte> data, ref RespScanState info)
    {
        // use silly math to detect the most common short patterns without needing
        // to access a reader, or use indexof etc; handles:
        // +OK\r\n
        // +PONG\r\n
        // :N\r\n for any single-digit N (integer)
        // :NN\r\n for any double-digit N (integer)
        // #N\r\n for any single-digit N (boolean)
        // _\r\n (null)
        uint hi, lo;
        switch (data.Length)
        {
            case 0:
            case 1:
            case 2:
                return OperationStatus.NeedMoreData;
            case 3:
                hi = (((uint)UnsafeCpuUInt16(data)) << 16) | (((uint)UnsafeCpuByte(data, 2)) << 8);
                break;
            default:
                hi = UnsafeCpuUInt32(data);
                break;
        }
        if ((hi & FirstThree) == FastNull)
        {
            info.SetComplete(3, RespPrefix.Null);
            return OperationStatus.Done;
        }

        var masked = hi & SingleCharScalarMask;
        if (masked == SingleDigitInteger)
        {
            info.SetComplete(4, RespPrefix.Integer);
            return OperationStatus.Done;
        }
        else if (masked == EitherBoolean)
        {
            info.SetComplete(4, RespPrefix.Boolean);
            return OperationStatus.Done;
        }

        switch (data.Length)
        {
            case 3:
                return OperationStatus.NeedMoreData;
            case 4:
                return UseReader;
            case 5:
                lo = ((uint)data[4]) << 24;
                break;
            case 6:
                lo = ((uint)UnsafeCpuUInt16(data, 4)) << 16;
                break;
            case 7:
                lo = ((uint)UnsafeCpuUInt16(data, 4)) << 16 | ((uint)UnsafeCpuByte(data, 6)) << 8;
                break;
            default:
                lo = UnsafeCpuUInt32(data, 4);
                break;
        }
        var u64 = BitConverter.IsLittleEndian ? ((((ulong)lo) << 32) | hi) : ((((ulong)hi) << 32) | lo);
        if (((u64 & FirstFive) == OK) | ((u64 & DoubleCharScalarMask) == DoubleDigitInteger))
        {
            info.SetComplete(5, RespPrefix.SimpleString);
            return OperationStatus.Done;
        }
        if ((u64 & FirstSeven) == PONG)
        {
            info.SetComplete(7, RespPrefix.SimpleString);
            return OperationStatus.Done;
        }
        return UseReader;
    }

    /// <summary>
    /// Attempt to read more data as part of the current frame.
    /// </summary>
    public OperationStatus TryRead(ref RespScanState state, in ReadOnlySequence<byte> data)
    {
        if (!_pubsub & state.TotalBytes == 0 & data.IsSingleSegment)
        {
#if NETCOREAPP3_1_OR_GREATER
            var status = TryFastRead(data.FirstSpan, ref state);
#else
            var status = TryFastRead(data.First.Span, ref state);
#endif
            if (status != UseReader) return status;
        }

        return TryReadViaReader(ref state, in data);

        static OperationStatus TryReadViaReader(ref RespScanState state, in ReadOnlySequence<byte> data)
        {
            var reader = new RespReader(in data);
            var complete = state.TryRead(ref reader, out var consumed);
            if (complete)
            {
                return OperationStatus.Done;
            }
            return OperationStatus.NeedMoreData;
        }
    }

    /// <summary>
    /// Attempt to read more data as part of the current frame.
    /// </summary>
    public OperationStatus TryRead(ref RespScanState state, ReadOnlySpan<byte> data)
    {
        if (!_pubsub & state.TotalBytes == 0)
        {
#if NETCOREAPP3_1_OR_GREATER
            var status = TryFastRead(data, ref state);
#else
            var status = TryFastRead(data, ref state);
#endif
            if (status != UseReader) return status;
        }

        return TryReadViaReader(ref state, data);

        static OperationStatus TryReadViaReader(ref RespScanState state, ReadOnlySpan<byte> data)
        {
            var reader = new RespReader(data);
            var complete = state.TryRead(ref reader, out var consumed);
            if (complete)
            {
                return OperationStatus.Done;
            }
            return OperationStatus.NeedMoreData;
        }
    }

    /// <summary>
    /// Validate that the supplied message is a valid RESP request, specifically: that it contains a single
    /// top-level array payload with bulk-string elements, the first of which is non-empty (the command).
    /// </summary>
    public void ValidateRequest(in ReadOnlySequence<byte> message)
    {
        if (message.IsEmpty) Throw("Empty RESP frame");
        RespReader reader = new(in message);
        reader.MoveNext(RespPrefix.Array);
        reader.DemandNotNull();
        if (reader.IsStreaming) Throw("Streaming is not supported in this context");
        var count = reader.AggregateLength();
        for (int i = 0; i < count; i++)
        {
            reader.MoveNext(RespPrefix.BulkString);
            reader.DemandNotNull();
            if (reader.IsStreaming) Throw("Streaming is not supported in this context");

            if (i == 0 && reader.ScalarIsEmpty()) Throw("command must be non-empty");
        }
        reader.DemandEnd();

        static void Throw(string message) => throw new InvalidOperationException(message);
    }
}
