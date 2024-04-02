using RESPite.Transports;
using System;
using System.Buffers;
using System.Diagnostics;

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

    OperationStatus IFrameScanner<RespFrameState>.TryRead(ref RespFrameState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info)
    {
        var reader = new RespReader(in data);
        int remaining = state.Remaining;
        while (remaining != 0 && reader.TryReadNext()) // TODO: implement info.ReadHint
        {
            remaining = remaining - 1 + reader.ChildCount;
            if (info.BytesRead == 0) // message root, indicates the kind
            {
                // "push" messages are out-of-band by definition; on a pub/sub connection, all "array" messages except "pong" can be considered "push"
                if (_pubsub & reader.Prefix == RespPrefix.Array & reader.ChildCount > 0) // fine to test all; we expect most pub/sub frames to be OOB
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
