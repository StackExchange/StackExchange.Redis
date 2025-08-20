using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal class PipelinedConnection : IRespConnection
{
    private readonly IRespConnection _tail;
    private readonly SemaphoreSlim _semaphore = new(1);
    public PipelinedConnection(IRespConnection tail) => this._tail = tail;

    public void Dispose()
    {
        _semaphore.Dispose();
        _tail.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        return _tail.DisposeAsync();
    }

    public bool CanWrite => _semaphore.CurrentCount > 0 && _tail.CanWrite;
    public int Outstanding => _tail.Outstanding;

    public RespPayload Send(RespPayload payload)
    {
        _semaphore.Wait();
        try
        {
            return _tail.Send(payload);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default)
    {
        bool haveLock = false;
        try
        {
            haveLock = _semaphore.Wait(0);
            if (!haveLock)
            {
                return FullAsync(this, payload, cancellationToken);
            }

            var pending = _tail.SendAsync(payload, cancellationToken);
            if (!pending.IsCompleted)
            {
                haveLock = false; // transferring
                return AwaitedWithLock(this, pending);
            }
            return new(pending.GetAwaiter().GetResult());
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async ValueTask<RespPayload> AwaitedWithLock(PipelinedConnection @this, ValueTask<RespPayload> pending)
        {
            try
            {
                return await pending.ConfigureAwait(false);
            }
            finally
            {
                @this._semaphore.Release();
            }
        }
        static async ValueTask<RespPayload> FullAsync(PipelinedConnection @this, RespPayload payload, CancellationToken cancellationToken)
        {
            await @this._semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await @this._tail.SendAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                @this._semaphore.Release();
            }
        }
    }
}
