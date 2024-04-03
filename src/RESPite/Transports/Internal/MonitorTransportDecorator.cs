using RESPite.Messages;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace RESPite.Transports.Internal;

internal sealed class MonitorTransportDecorator(ISyncRequestResponseTransport transport, TimeSpan timeout) : ISyncSynchronizedRequestResponseTransport
{
    public MonitorTransportDecorator(ISyncRequestResponseTransport transport) : this(transport, Timeout.InfiniteTimeSpan) { }

    private readonly object syncLock = new();
    public event Action<ReadOnlySequence<byte>> OutOfBandData
    {
        add => transport.OutOfBandData += value;
        remove => transport.OutOfBandData -= value;
    }

    public void Dispose() => transport.Dispose();

    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        bool lockTaken = false;
        try
        {
            Monitor.TryEnter(syncLock, timeout, ref lockTaken);
            if (!lockTaken) ThrowTimeout();
            return transport.Send(request, writer, reader);
        }
        finally
        {
            if (lockTaken) Monitor.Exit(syncLock);
        }
    }

    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
    {
        bool lockTaken = false;
        try
        {
            Monitor.TryEnter(syncLock, timeout, ref lockTaken);
            if (!lockTaken) ThrowTimeout();
            return transport.Send(request, writer, reader);
        }
        finally
        {
            if (lockTaken) Monitor.Exit(syncLock);
        }
    }

    [DoesNotReturn]
    private static void ThrowTimeout() => throw new TimeoutException();

}
