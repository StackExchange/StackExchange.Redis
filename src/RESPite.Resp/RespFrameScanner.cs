using RESPite.Transports;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using RESPite.Internal;
using static RESPite.Internal.Constants;
namespace RESPite.Resp;

/// <summary>
/// Scans RESP frames
/// </summary>
public sealed class RespFrameScanner : IFrameScanner<RespFrameScanner.RespFrameState>
{
    /// <summary>
    /// Gets a frame scanner for RESP2 request/response connections, or RESP3 connections
    /// </summary>
    public static RespFrameScanner Default { get; } = new(false);
    /// <summary>
    /// Gets a frame scanner that identifies RESP2 pub/sub messages
    /// </summary>
    public static RespFrameScanner Subscription { get; } = new(true);
    private RespFrameScanner(bool pubsub) => _pubsub = pubsub;
    private readonly bool _pubsub;

    void IFrameScanner<RespFrameState>.OnBeforeFrame(ref RespFrameState state, ref FrameScanInfo info)
    {
        state.Remaining = 1;
        info.ReadHint = 3; // minimum legal RESP frame is: _\r\n
    }

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
    private static OperationStatus TryFastRead(ReadOnlySpan<byte> data, ref FrameScanInfo info)
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
            info.BytesRead = 3;
            return OperationStatus.Done;
        }

        var masked = hi & SingleCharScalarMask;
        if (masked == SingleDigitInteger || masked == EitherBoolean)
        {
            info.BytesRead = 4;
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
            info.BytesRead = 5;
            return OperationStatus.Done;
        }
        if ((u64 & FirstSeven) == PONG)
        {
            info.BytesRead = 7;
            return OperationStatus.Done;
        }
        return UseReader;
    }
    OperationStatus IFrameScanner<RespFrameState>.TryRead(ref RespFrameState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info)
    {
        if (info.BytesRead == 0 & data.IsSingleSegment)
        {
#if NETCOREAPP3_1_OR_GREATER
            var status = TryFastRead(data.FirstSpan, ref info);
#else
            var status = TryFastRead(data.First.Span, ref info);
#endif
            if (status != UseReader) return status;
        }

        return TryReadViaReader(ref state, in data, ref info, _pubsub);

        static OperationStatus TryReadViaReader(ref RespFrameState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info, bool pubsub)
        {
            var reader = new RespReader(in data);
            int remaining = state.Remaining;
            while (remaining != 0 && reader.TryReadNext()) // TODO: implement info.ReadHint
            {
                remaining = remaining - 1 + reader.ChildCount;
                if (info.BytesRead == 0) // message root, indicates the kind
                {
                    // "push" messages are out-of-band by definition; on a pub/sub connection, all "array" messages except "pong" can be considered "push"
                    if (pubsub & reader.Prefix == RespPrefix.Array & reader.ChildCount > 0) // fine to test all; we expect most pub/sub frames to be OOB
                    {
                        if (!reader.TryReadNext()) return OperationStatus.NeedMoreData; // need to redo from start when we can see more (no BytesRead delta)
                        remaining = remaining - 1 + reader.ChildCount;
                        info.IsOutOfBand = !(reader.Prefix == RespPrefix.BulkString && reader.Is("pong"u8));
                    }
                    else
                    {
                        info.IsOutOfBand = reader.Prefix == RespPrefix.Push;
                    }
                }
            }
            Debug.Assert(remaining >= 0, "remaining count should not go negative");
            state.Remaining = remaining;
            info.BytesRead += reader.BytesConsumed;
            return remaining == 0 ? OperationStatus.Done : OperationStatus.NeedMoreData;
        }
    }

    void IFrameScanner<RespFrameState>.Trim(ref RespFrameState state, ref ReadOnlySequence<byte> data, ref FrameScanInfo info) { }

    /// <summary>
    /// Internal state required by the frame parser
    /// </summary>
    public struct RespFrameState
    {
        internal RespFrameState(int outstanding) => Remaining = outstanding;
        internal int Remaining;
    }
}
