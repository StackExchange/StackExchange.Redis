using RESPite.Buffers;
using RESPite.Gateways.Internal;
using RESPite.Internal;
using RESPite.Messages;
using RESPite.Transports.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Transports;

/// <summary>
/// Utility methods for working with gateways
/// </summary>
public static class TransportExtensions
{
    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations
    /// </summary>
    public static IRequestResponseTransport RequestResponse<TState>(this IByteTransport gateway, IFrameScanner<TState> frameScanner)
        => new RequestResponseTransport<TState>(gateway, frameScanner);

    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations
    /// </summary>
    public static IAsyncRequestResponseTransport RequestResponse<TState>(this IAsyncByteTransport gateway, IFrameScanner<TState> frameScanner)
        => new AsyncRequestResponseTransport<TState>(gateway, frameScanner);

    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations
    /// </summary>
    public static ISyncRequestResponseTransport RequestResponse<TState>(this ISyncByteTransport gateway, IFrameScanner<TState> frameScanner)
        => new SyncRequestResponseTransport<TState>(gateway, frameScanner);

    internal static RefCountedBuffer<byte> Serialize<TRequest>(this IWriter<TRequest> writer, in TRequest request)
    {
        BufferCore<byte> buffer = new();
        writer.Write(in request, ref buffer);
        return buffer.Detach();
    }

    private static void ThrowEmptyFrame() => throw new InvalidOperationException("Frames must have positive length");
    private static void ThrowInvalidData() => throw new InvalidOperationException("Invalid data while processing frame");
    private static void ThrowEOF() => throw new EndOfStreamException();
    private static void ThrowInvalidOperationStatus(OperationStatus status) => throw new InvalidOperationException("Invalid operation status: " + status);

    internal static IEnumerable<RefCountedBuffer<byte>> ReadAll<TState>(this ISyncByteTransport transport,
        IFrameScanner<TState> scanner, Action<ReadOnlySequence<byte>>? outOfBandData)
    {
        TState? scanState;
        {
            if (transport is IFrameScannerLifetime<TState> lifetime)
            {
                lifetime.OnInitialize(out scanState);
            }
            else
            {
                scanState = default;
            }
        }

        try
        {
            while (true) // successive frames
            {
                FrameScanInfo scanInfo = default;
                scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                while (true) // incremental read of single frame
                {
                    // we can pass partial fragments to an incremental scanner, but we need the entire fragment
                    // for deframe; as such, "skip" is our progress into the current frame for an incremental scanner
                    var entireBuffer = transport.GetBuffer();
                    var workingBuffer = scanInfo.BytesRead == 0 ? entireBuffer : entireBuffer.Slice(scanInfo.BytesRead);
                    var status = workingBuffer.IsEmpty ? OperationStatus.NeedMoreData : scanner.TryRead(ref scanState, in workingBuffer, ref scanInfo);
                    switch (status)
                    {
                        case OperationStatus.InvalidData:
                            // we always call advance as a courtesy for backends that need per-read advance
                            transport.Advance(0);
                            ThrowInvalidData();
                            break;
                        case OperationStatus.NeedMoreData:
                            transport.Advance(0);
                            if (!transport.TryRead(Math.Max(scanInfo.ReadHint, 1)))
                            {
                                if (transport.GetBuffer().IsEmpty) yield break; // clean exit
                                ThrowEOF(); // partial frame
                            }
                            continue;
                        case OperationStatus.Done when scanInfo.BytesRead <= 0:
                            // if we're not making progress, we'd loop forever
                            transport.Advance(0);
                            ThrowEmptyFrame();
                            break;
                        case OperationStatus.Done:
                            long bytesRead = scanInfo.BytesRead; // snapshot for our final advance
                            workingBuffer = entireBuffer.Slice(0, bytesRead); // includes head and trail data
                            scanner.Trim(ref scanState, ref workingBuffer, ref scanInfo); // contains just the payload
                            if (scanInfo.IsOutOfBand)
                            {
                                outOfBandData?.Invoke(workingBuffer);
                                transport.Advance(bytesRead);
                                // prepare for next frame
                                scanInfo = default;
                                scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                                continue;
                            }
                            var retained = workingBuffer.Retain();
                            transport.Advance(bytesRead);
                            yield return retained;
                            // prepare for next frame
                            scanInfo = default;
                            scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                            continue;
                        default:
                            transport.Advance(0);
                            ThrowInvalidOperationStatus(status);
                            break;
                    }
                }
            }

        }
        finally
        {
            if (transport is IFrameScannerLifetime<TState> lifetime)
            {
                lifetime?.OnComplete(ref scanState);
            }
        }

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RefCountedBuffer<byte> ReadOne<TState>(this ISyncByteTransport transport,
        IFrameScanner<TState> scanner, Action<ReadOnlySequence<byte>>? outOfBandData, bool unsafeForceLifetime)
    {
        if (unsafeForceLifetime)
        {
            return ReadOneWithLifetime(transport, Unsafe.As<IFrameScannerLifetime<TState>>(scanner), outOfBandData);
        }
        TState? scanState = default;
        return ReadOneCore(transport, scanner, outOfBandData, ref scanState);

        static RefCountedBuffer<byte> ReadOneWithLifetime(ISyncByteTransport transport,
        IFrameScannerLifetime<TState> scanner, Action<ReadOnlySequence<byte>>? outOfBandData)
        {
            scanner.OnInitialize(out var scanState);
            try
            {
                return ReadOneCore(transport, scanner, outOfBandData, ref scanState);
            }
            finally
            {
                scanner.OnComplete(ref scanState);
            }
        }

        static RefCountedBuffer<byte> ReadOneCore(ISyncByteTransport transport,
            IFrameScanner<TState> scanner, Action<ReadOnlySequence<byte>>? outOfBandData, ref TState? scanState)
        {
            FrameScanInfo scanInfo = default;
            scanner.OnBeforeFrame(ref scanState, ref scanInfo);
            while (true)
            {
                // we can pass partial fragments to an incremental scanner, but we need the entire fragment
                // for deframe; as such, "skip" is our progress into the current frame for an incremental scanner
                var entireBuffer = transport.GetBuffer();
                var workingBuffer = scanInfo.BytesRead == 0 ? entireBuffer : entireBuffer.Slice(scanInfo.BytesRead);
                var status = workingBuffer.IsEmpty ? OperationStatus.NeedMoreData : scanner.TryRead(ref scanState, in workingBuffer, ref scanInfo);
                switch (status)
                {
                    case OperationStatus.InvalidData:
                        // we always call advance as a courtesy for backends that need per-read advance
                        transport.Advance(0);
                        ThrowInvalidData();
                        break;
                    case OperationStatus.NeedMoreData:
                        transport.Advance(0);
                        if (!transport.TryRead(Math.Max(scanInfo.ReadHint, 1))) ThrowEOF();
                        continue;
                    case OperationStatus.Done when scanInfo.BytesRead <= 0:
                        // if we're not making progress, we'd loop forever
                        transport.Advance(0);
                        ThrowEmptyFrame();
                        break;
                    case OperationStatus.Done:
                        long bytesRead = scanInfo.BytesRead; // snapshot for our final advance
                        workingBuffer = entireBuffer.Slice(0, bytesRead); // includes head and trail data
                        scanner.Trim(ref scanState, ref workingBuffer, ref scanInfo); // contains just the payload
                        if (scanInfo.IsOutOfBand)
                        {
                            outOfBandData?.Invoke(workingBuffer);
                            transport.Advance(bytesRead);
                            // prepare for next frame
                            scanInfo = default;
                            scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                            continue;
                        }
                        var retained = workingBuffer.Retain();
                        transport.Advance(bytesRead);
                        return retained;
                    default:
                        transport.Advance(0);
                        ThrowInvalidOperationStatus(status);
                        break;
                }
            }
        }
    }

    internal static ValueTask<RefCountedBuffer<byte>> ReadOneAsync<TState>(this IAsyncByteTransport transport,
        IFrameScanner<TState> scanner, Action<ReadOnlySequence<byte>>? outOfBandData, CancellationToken token)
    {
        throw new NotImplementedException();
    }


    /// <summary>
    /// Builds a connection intended for pipelined operation, with a backlog
    /// of work
    /// </summary>
    public static IPipelinedTransport Pipeline(this IByteTransport gateway)
        => throw new NotImplementedException();

    /// <summary>
    /// Create a RESP transport over a stream
    /// </summary>
    public static IByteTransport CreateTransport(this Stream duplex, bool closeStream = true)
    {
        if (duplex is null) throw new ArgumentNullException(nameof(duplex));
        if (!(duplex.CanRead && duplex.CanWrite)) throw new ArgumentException("Stream must allow read and write", nameof(duplex));

        return new StreamTransport(duplex, closeStream);
    }
}
