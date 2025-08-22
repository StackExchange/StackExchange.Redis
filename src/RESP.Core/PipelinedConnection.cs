using System;
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
    public RespConfiguration Configuration => _tail.Configuration;
    public bool CanWrite => _semaphore.CurrentCount > 0 && _tail.CanWrite;
    public int Outstanding => _tail.Outstanding;

    public void Send(IRespMessage message)
    {
        _semaphore.Wait();
        try
        {
            _tail.Send(message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task SendAsync(IRespMessage message, CancellationToken cancellationToken = default)
    {
        bool haveLock = false;
        try
        {
            haveLock = _semaphore.Wait(0);
            if (!haveLock)
            {
                return FullAsync(this, message, cancellationToken);
            }

            var pending = _tail.SendAsync(message, cancellationToken);
            if (!pending.IsCompleted)
            {
                haveLock = false; // transferring
                return AwaitedWithLock(this, pending);
            }

            pending.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async Task AwaitedWithLock(PipelinedConnection @this, Task pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            finally
            {
                @this._semaphore.Release();
            }
        }
        static async Task FullAsync(PipelinedConnection @this, IRespMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await @this._semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                message.TrySetCanceled(oce.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                message.TrySetException(ex);
                throw;
            }

            try
            {
                await @this._tail.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                @this._semaphore.Release();
            }
        }
    }
}
