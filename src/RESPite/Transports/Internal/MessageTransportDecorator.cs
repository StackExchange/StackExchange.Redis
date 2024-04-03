using RESPite.Messages;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Transports.Internal;

internal abstract class MessageTransportDecorator : IMessageTransportBase
{
    protected IMessageTransportBase Transport => _transport;
    private readonly IMessageTransportBase _transport;
    private readonly int _support;
    private const int SUPPORT_SYNC = 1 << 0, SUPPORT_ASYNC = 1 << 1;
    protected MessageTransportDecorator(IMessageTransportBase transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (transport is ISyncMessageTransport) _support |= SUPPORT_SYNC;
        if (transport is IAsyncMessageTransport) _support |= SUPPORT_ASYNC;
    }

    public virtual event Action<ReadOnlySequence<byte>> OutOfBandData
    {
        add => _transport.OutOfBandData += value;
        remove => _transport.OutOfBandData -= value;
    }

    public virtual void Dispose() => (_transport as IDisposable)?.Dispose();

    public virtual ValueTask DisposeAsync() => _transport is IAsyncDisposable d ? d.DisposeAsync() : default;

    [DoesNotReturn]
    private void ThrowNotSupported([CallerMemberName] string caller = "") => throw new NotSupportedException(caller);
    protected IAsyncMessageTransport AsAsync([CallerMemberName] string caller = "")
    {
        if ((_support & SUPPORT_ASYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<IAsyncMessageTransport>(_transport); // type-tested in .ctor
    }
    protected ISyncMessageTransport AsSync([CallerMemberName] string caller = "")
    {
        if ((_support & SUPPORT_SYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<ISyncMessageTransport>(_transport); // type-tested in .ctor
    }

    public virtual TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
        => AsSync().Send(in request, writer, reader);
    public virtual TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
        => AsSync().Send(in request, writer, reader);
    public virtual ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default)
        => AsAsync().SendAsync(in request, writer, reader);
    public virtual ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default)
        => AsAsync().SendAsync(in request, writer, reader);

    [DoesNotReturn]
    protected static void ThrowTimeout() => throw new TimeoutException();
}
