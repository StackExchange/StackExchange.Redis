using System.Diagnostics;
using System.Runtime.CompilerServices;
using RESPite.Connections.Internal;

namespace RESPite;

public abstract class RespConnection : IDisposable, IAsyncDisposable
{
    private bool _isDisposed;
    internal bool IsDisposed => _isDisposed;

    private readonly RespContext _context;
    public ref readonly RespContext Context => ref _context;
    public RespConfiguration Configuration { get; }

    internal virtual bool IsHealthy => !_isDisposed;

    internal virtual int OutstandingOperations { get; }

    // this is the usual usage, since we want context to be preserved
    private protected RespConnection(in RespContext tail, RespConfiguration? configuration = null)
    {
        var conn = tail.Connection;
        if (conn is not { IsHealthy: true })
        {
            ThrowUnhealthy();
        }

        Configuration = configuration ?? conn.Configuration;
        _context = tail.WithConnection(this);

        static void ThrowUnhealthy() =>
            throw new ArgumentException("A healthy tail connection is required.", nameof(tail));
    }

    // this is atypical - only for use when creating null connections
    private protected RespConnection(RespConfiguration? configuration = null)
    {
        Configuration = configuration ?? RespConfiguration.Default;
        _context = default;
        _context = _context.WithConnection(this);
        Debug.Assert(this is NullConnection);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowIfDisposed()
    {
        if (_isDisposed) ThrowDisposed();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowDisposed() => throw CreateObjectDisposedException();

    internal Exception CreateObjectDisposedException() => new ObjectDisposedException(GetType().Name);

    public void Dispose()
    {
        _isDisposed = this is not NullConnection;
        OnDispose(true);
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    public ValueTask DisposeAsync()
    {
        _isDisposed = this is not NullConnection;
        return OnDisposeAsync();
    }

    protected virtual ValueTask OnDisposeAsync()
    {
        OnDispose(true);
        return default;
    }

    public abstract void Send(in RespOperation message);

    internal virtual void Send(ReadOnlySpan<RespOperation> messages)
    {
        int i = 0;
        try
        {
            for (i = 0; i < messages.Length; i++)
            {
                Send(messages[i]);
            }
        }
        catch (Exception ex)
        {
            MarkFaulted(messages.Slice(i), ex);
            throw;
        }
    }

    public virtual Task SendAsync(in RespOperation message)
    {
        Send(message);
        return Task.CompletedTask;
    }

    internal virtual Task SendAsync(ReadOnlyMemory<RespOperation> messages)
    {
        switch (messages.Length)
        {
            case 0: return Task.CompletedTask;
            case 1: return SendAsync(messages.Span[0]);
        }

        int i = 0;
        try
        {
            for (; i < messages.Length; i++)
            {
                var pending = SendAsync(messages.Span[i]);
                if (!pending.IsCompleted)
                    return Awaited(this, pending, messages.Slice(i));
                pending.GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            MarkFaulted(messages.Span.Slice(i), ex);
            throw;
        }

        return Task.CompletedTask;

        static async Task Awaited(RespConnection connection, Task pending, ReadOnlyMemory<RespOperation> messages)
        {
            int i = 0;
            try
            {
                await pending.ConfigureAwait(false);
                for (i = 1; i < messages.Length; i++)
                {
                    await connection.SendAsync(messages.Span[i]).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                MarkFaulted(messages.Span.Slice(i), ex);
                throw;
            }
        }
    }

    protected static void MarkFaulted(ReadOnlySpan<RespOperation> messages, Exception fault)
    {
        foreach (var message in messages)
        {
            try
            {
                message.Message.TrySetException(message.Token, fault);
            }
            catch
            {
                // best efforts
            }
        }
    }
}
