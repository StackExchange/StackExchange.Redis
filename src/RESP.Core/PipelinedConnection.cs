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
        _semaphore.Wait(message.CancellationToken);
        try
        {
            _tail.Send(message);
        }
        catch (Exception ex)
        {
            message.TrySetException(ex);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Send(ReadOnlySpan<IRespMessage> messages)
    {
        switch (messages.Length)
        {
            case 0: return;
            case 1:
                Send(messages[0]);
                return;
        }
        _semaphore.Wait(messages[0].CancellationToken);
        try
        {
            _tail.Send(messages);
        }
        catch (Exception ex)
        {
            TrySetException(messages, ex);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task SendAsync(IRespMessage message)
    {
        bool haveLock = false;
        try
        {
            haveLock = _semaphore.Wait(0);
            if (!haveLock)
            {
                DebugCounters.OnPipelineFullAsync();
                return FullAsync(this, message);
            }

            var pending = _tail.SendAsync(message);
            if (!pending.IsCompleted)
            {
                DebugCounters.OnPipelineSendAsync();
                haveLock = false; // transferring
                return AwaitAndReleaseLock(pending);
            }

            DebugCounters.OnPipelineFullSync();
            pending.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            message.TrySetException(ex);
            throw;
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async Task FullAsync(PipelinedConnection @this, IRespMessage message)
        {
            try
            {
                await @this._semaphore.WaitAsync(message.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                message.TrySetException(ex);
                throw;
            }

            try
            {
                await @this._tail.SendAsync(message).ConfigureAwait(false);
            }
            finally
            {
                @this._semaphore.Release();
            }
        }
    }

    private async Task AwaitAndReleaseLock(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static void TrySetException(ReadOnlySpan<IRespMessage> messages, Exception ex)
    {
        foreach (var message in messages)
        {
            message.TrySetException(ex);
        }
    }

    public Task SendAsync(ReadOnlyMemory<IRespMessage> messages)
    {
        switch (messages.Length)
        {
            case 0: return Task.CompletedTask;
            case 1: return SendAsync(messages.Span[0]);
        }
        bool haveLock = false;
        try
        {
            haveLock = _semaphore.Wait(0);
            if (!haveLock)
            {
                DebugCounters.OnPipelineFullAsync();
                return FullAsync(this, messages);
            }

            var pending = _tail.SendAsync(messages);
            if (!pending.IsCompleted)
            {
                DebugCounters.OnPipelineSendAsync();
                haveLock = false; // transferring
                return AwaitAndReleaseLock(pending);
            }

            DebugCounters.OnPipelineFullSync();
            pending.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            TrySetException(messages.Span, ex);
            throw;
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async Task FullAsync(PipelinedConnection @this, ReadOnlyMemory<IRespMessage> messages)
        {
            bool haveLock = false; // we don't have the lock initially
            try
            {
                await @this._semaphore.WaitAsync(messages.Span[0].CancellationToken).ConfigureAwait(false);
                haveLock = true;
                await @this._tail.SendAsync(messages).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TrySetException(messages.Span, ex);
                throw;
            }
            finally
            {
                if (haveLock)
                {
                    @this._semaphore.Release();
                }
            }
        }
    }
}
