using RESPite.Messages;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Transports.Internal;

internal sealed class SemaphoreSlimTransportDecorator: MessageTransportDecorator, ISynchronizedRequestResponseTransport
{
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1);
    private readonly TimeSpan _timeout;
    public SemaphoreSlimTransportDecorator(IRequestResponseBase transport, TimeSpan timeout) : base(transport)
    {
        _timeout = timeout;
    }
    public SemaphoreSlimTransportDecorator(IRequestResponseBase transport) : this(transport, Timeout.InfiniteTimeSpan) { }

    
    public override TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
    {
        if (!_lock.Wait(_timeout)) ThrowTimeout();
        try
        {
            return base.Send(request, writer, reader);
        }
        finally
        {
            _lock.Release();
        }
    }

    public override TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        if (!_lock.Wait(_timeout)) ThrowTimeout();
        try
        {
            return base.Send(request, writer, reader);
        }
        finally
        {
            _lock.Release();
        }
    }

    public override ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default)
        => SendAsyncImpl(request, writer, reader, token);

    private async ValueTask<TResponse> SendAsyncImpl<TRequest, TResponse>(TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token)
    {
        if (!await _lock.WaitAsync(_timeout, token)) ThrowTimeout();
        try
        {
            return base.Send(in request, writer, reader);
        }
        finally
        {
            _lock.Release();
        }
    }

    public override ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default)
        => SendAsyncImpl(request, writer, reader, token);

    private async ValueTask<TResponse> SendAsyncImpl<TRequest, TResponse>(TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token)
    {
        if (!await _lock.WaitAsync(_timeout, token)) ThrowTimeout();
        try
        {
            return base.Send(request, writer, reader);
        }
        finally
        {
            _lock.Release();
        }
    }

}
