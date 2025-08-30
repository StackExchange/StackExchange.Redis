using RESPite.Internal;

namespace RESPite.Connections.Internal;

internal sealed class SynchronizedConnection(in RespContext tail) : DecoratorConnection(tail)
{
    private readonly SemaphoreSlim _semaphore = new(1);

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            _semaphore.Dispose();
        }
        base.OnDispose(disposing);
    }

    protected override ValueTask OnDisposeAsync()
    {
        _semaphore.Dispose();
        return base.OnDisposeAsync();
    }

    internal override bool IsHealthy => _semaphore.CurrentCount > 0 & base.IsHealthy;
    public override void Send(in RespOperation message)
    {
        try
        {
            _semaphore.Wait(message.CancellationToken);
            Tail.Send(message);
        }
        catch (Exception ex)
        {
            message.Message.TrySetException(message.Token, ex);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    internal override void Send(ReadOnlySpan<RespOperation> messages)
    {
        switch (messages.Length)
        {
            case 0: return;
            case 1:
                Send(messages[0]);
                return;
        }

        try
        {
            _semaphore.Wait(messages[0].CancellationToken);
            Tail.Send(messages);
        }
        catch (Exception ex)
        {
            MarkFaulted(messages, ex);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override Task SendAsync(in RespOperation message)
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

            var pending = Tail.SendAsync(message);
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
            message.Message.TrySetException(message.Token, ex);
            throw;
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async Task FullAsync(SynchronizedConnection @this, RespOperation message)
        {
            try
            {
                await @this._semaphore.WaitAsync(message.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                message.Message.TrySetException(message.Token, ex);
                throw;
            }

            try
            {
                await @this.Tail.SendAsync(message).ConfigureAwait(false);
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

    internal override Task SendAsync(ReadOnlyMemory<RespOperation> messages)
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

            var pending = Tail.SendAsync(messages);
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
            MarkFaulted(messages.Span, ex);
            throw;
        }
        finally
        {
            if (haveLock) _semaphore.Release();
        }

        static async Task FullAsync(SynchronizedConnection @this, ReadOnlyMemory<RespOperation> messages)
        {
            bool haveLock = false; // we don't have the lock initially
            try
            {
                await @this._semaphore.WaitAsync(messages.Span[0].CancellationToken).ConfigureAwait(false);
                haveLock = true;
                await @this.Tail.SendAsync(messages).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MarkFaulted(messages.Span, ex);
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
