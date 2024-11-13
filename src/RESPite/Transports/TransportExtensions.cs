using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Gateways.Internal;
using RESPite.Internal.Buffers;
using RESPite.Messages;
using RESPite.Transports.Internal;

namespace RESPite.Transports;

/// <summary>
/// Utility methods for working with gateways.
/// </summary>
public static class TransportExtensions
{
    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations.
    /// </summary>
    public static IRequestResponseTransport RequestResponse<TState>(this IByteTransport gateway, IFrameScanner<TState> frameScanner, FrameValidation validateOutbound = FrameValidation.Debug)
        => new RequestResponseTransport<TState>(gateway, frameScanner, validateOutbound);

    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations.
    /// </summary>
    public static IAsyncRequestResponseTransport RequestResponse<TState>(this IAsyncByteTransport gateway, IFrameScanner<TState> frameScanner, FrameValidation validateOutbound = FrameValidation.Debug)
        => new AsyncRequestResponseTransport<TState>(gateway, frameScanner, validateOutbound);

    /// <summary>
    /// Builds a connection intended for simple request/response operation, without
    /// any concurrency or backlog of pending operations.
    /// </summary>
    public static ISyncRequestResponseTransport RequestResponse<TState>(this ISyncByteTransport gateway, IFrameScanner<TState> frameScanner, FrameValidation validateOutbound = FrameValidation.Debug)
        => new SyncRequestResponseTransport<TState>(gateway, frameScanner, validateOutbound);

    /// <summary>
    /// Uses <see cref="Monitor"/> to synchronize access to the underlying transport.
    /// </summary>
    public static ISyncRequestResponseTransport WithMonitorSynchronization(this ISyncRequestResponseTransport transport)
        => new MonitorTransportDecorator(transport);

    /// <summary>
    /// Uses <see cref="Monitor"/> to synchronize access to the underlying transport.
    /// </summary>
    public static ISyncRequestResponseTransport WithMonitorSynchronization(this ISyncRequestResponseTransport transport, TimeSpan timeout)
        => new MonitorTransportDecorator(transport, timeout);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static IRequestResponseTransport WithSemaphoreSlimSynchronization(this IRequestResponseTransport transport)
        => new SemaphoreSlimTransportDecorator(transport);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static IRequestResponseTransport WithSemaphoreSlimSynchronization(this IRequestResponseTransport transport, TimeSpan timeout)
        => new SemaphoreSlimTransportDecorator(transport, timeout);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static ISyncRequestResponseTransport WithSemaphoreSlimSynchronization(this ISyncRequestResponseTransport transport)
        => new SemaphoreSlimTransportDecorator(transport);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static ISyncRequestResponseTransport WithSemaphoreSlimSynchronization(this ISyncRequestResponseTransport transport, TimeSpan timeout)
        => new SemaphoreSlimTransportDecorator(transport, timeout);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static IAsyncRequestResponseTransport WithSemaphoreSlimSynchronization(this IAsyncRequestResponseTransport transport)
        => new SemaphoreSlimTransportDecorator(transport);

    /// <summary>
    /// Uses <see cref="SemaphoreSlim"/> to synchronize access to the underlying transport.
    /// </summary>
    public static IAsyncRequestResponseTransport WithSemaphoreSlimSynchronization(this IAsyncRequestResponseTransport transport, TimeSpan timeout)
        => new SemaphoreSlimTransportDecorator(transport, timeout);

    private class Scratch : IBufferWriter<byte>
    {
        [ThreadStatic]
        private static Scratch? perThread;
        public static Scratch Create()
        {
            var obj = perThread ?? new();
            perThread = null;
            obj.buffer = new(SlabManager<byte>.Ambient);
            return obj;
        }
        private Scratch() { }
        private BufferCore<byte> buffer;
        public void Advance(int count) => buffer.Commit(count);
        public Memory<byte> GetMemory(int sizeHint = 0) => buffer.GetWritableTail();
        public Span<byte> GetSpan(int sizeHint = 0) => buffer.GetWritableTail().Span;
        public RefCountedBuffer<byte> DetachAndRecycle()
        {
            var result = buffer.Detach();
            buffer = default;
            perThread = this;
            return result;
        }
    }
    internal static RefCountedBuffer<byte> Serialize<TRequest>(this IWriter<TRequest> writer, in TRequest request)
    {
        var buffer = Scratch.Create();
        writer.Write(in request, buffer);
        return buffer.DetachAndRecycle();
    }

    private static void ThrowEmptyFrame() => throw new InvalidOperationException("Frames must have positive length");
    private static void ThrowInvalidData() => throw new InvalidOperationException("Invalid data while processing frame");
    private static void ThrowEOF() => throw new EndOfStreamException();
    private static void ThrowInvalidOperationStatus(OperationStatus status) => throw new InvalidOperationException("Invalid operation status: " + status);

    internal static IEnumerable<RefCountedBuffer<byte>> ReadAll<TState>(
        this ISyncByteTransport transport,
        IFrameScanner<TState> scanner,
        MessageCallback? outOfBandData)
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
    internal static RefCountedBuffer<byte> ReadOne<TState>(
        this ISyncByteTransport transport,
        IFrameScanner<TState> scanner,
        MessageCallback? outOfBandData,
        bool unsafeForceLifetime)
    {
        if (unsafeForceLifetime)
        {
            return ReadOneWithLifetime(transport, Unsafe.As<IFrameScannerLifetime<TState>>(scanner), outOfBandData);
        }
        TState? scanState = default;
        return ReadOneCore(transport, scanner, outOfBandData, ref scanState);

        static RefCountedBuffer<byte> ReadOneWithLifetime(
            ISyncByteTransport transport,
            IFrameScannerLifetime<TState> scanner,
            MessageCallback? outOfBandData)
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

        static RefCountedBuffer<byte> ReadOneCore(
            ISyncByteTransport transport,
            IFrameScanner<TState> scanner,
            MessageCallback? outOfBandData,
            ref TState? scanState)
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

    internal static async ValueTask<RefCountedBuffer<byte>> ReadOneAsync<TState>(
        this IAsyncByteTransport transport,
        IFrameScanner<TState> scanner,
        MessageCallback? outOfBandData,
        CancellationToken token)
    {
        TState? scanState;
        var lifetime = scanner as IFrameScannerLifetime<TState>;
        if (lifetime is null)
        {
            scanState = default;
        }
        else
        {
            lifetime.OnInitialize(out scanState);
        }
        try
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
                        if (!await transport.TryReadAsync(Math.Max(scanInfo.ReadHint, 1)).ConfigureAwait(false)) ThrowEOF();
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
        finally
        {
            lifetime?.OnComplete(ref scanState);
        }
    }

    /// <summary>
    /// Builds a connection intended for pipelined operation, with a backlog
    /// of work.
    /// </summary>
    public static IPipelinedTransport Pipeline(this IByteTransport gateway)
        => throw new NotImplementedException();

    /// <summary>
    /// Create a RESP transport over a duplex stream.
    /// </summary>
    public static IByteTransport CreateTransport(this Stream duplex, bool closeStream = true)
        => new StreamTransport(duplex, closeStream);

    /// <summary>
    /// Create a RESP transport over a pair of streams.
    /// </summary>
    public static IByteTransport CreateTransport(this Stream source, Stream target, bool closeStreams = true)
        => new StreamTransport(source, target, closeStreams);

    /// <summary>
    /// Create a RESP transport over a socket.
    /// </summary>
    public static IByteTransport CreateTransport(this Socket socket, bool closeStreams = true)
        => new StreamTransport(new NetworkStream(socket), closeStreams);

    /// <summary>
    /// Create a RESP transport over a socket.
    /// </summary>
    public static IByteTransport CreateTransport(this EndPoint remoteEndpoint, bool closeStreams = true)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        socket.Connect(remoteEndpoint);
        return new NetworkStream(socket, closeStreams).CreateTransport(closeStreams);
    }
}
