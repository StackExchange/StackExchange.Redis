using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;

namespace StackExchange.Redis;

internal abstract class BufferedStreamWriter(Stream target, CancellationToken cancellationToken)
    : IBufferWriter<byte>, IDisposable, IAsyncDisposable
{
    /* What is this?
     *
     * Basically, an abstraction similar to Pipe - it has a separate write and read head, etc, but
     * the key difference is that it is focused on reducing context switches:
     *
     * - explicit flush is *synchronous*, simply marking the tail buffer as "ready to read"; this is actually pretty
     *   similar to Pipe if we ignore back-pressure, which it kinda does by default
     * - implicit flush is implicit - i.e. when committed work fills a page, that page is flushed automatically
     * - the consumption API allows fully synchronous consumption, if desired
     *
     * At the moment, 2 concrete implementations are provided:
     * - SwitchableBufferedStreamWriter: combines a sync and async implementation, with the ability to
     *   start sync and transition to async - or start fully async.
     * - PipeStreamWriter: uses the pre-existing Pipe API and pre-built PipeWriter.CopyTo(Stream).
     *
     * The eventual intention is that:
     * - pub/sub always starts in async mode
     * - interactive uses sync mode in low-connection-count scenarios,
     *   and async mode in high-connection-count scenarios (there's some missing work here in
     *   tracking the count and transitioning from sync to async as we cross some threshold)
     *
     * However! currently, the default is "always sync"; the only way to get sync is via private APIs; this may
     * be reviewed later.
     *
     * So why the Pipe version? and why not *just* use Pipe? The custom writer out-performs Pipe, but Pipe remains
     * useful as a separate implementation for comparison and troubleshooting.
     *
     *  Threading model: this type assumes a single producer/writer and a single
     *  consumer/reader. The monitor protects the non-thread-safe CycleBuffer and
     *  shared state transitions between those two roles; it is not intended to
     *  support multiple concurrent producers.
     */

    public enum WriteMode
    {
        Default,
        Sync,
        Async,
        Pipe,
    }

    public virtual bool IsSync => false;

    public static BufferedStreamWriter Create(WriteMode mode, ConnectionType connectionType, Stream target, CancellationToken cancellationToken)
    {
        if (connectionType is ConnectionType.Subscription | mode is WriteMode.Default)
        {
            // sync-mode targets latency; pub/sub never needs that;
            // default write-mode should use async
            mode = WriteMode.Async;
        }
        return mode switch
        {
            WriteMode.Sync => new SwitchableBufferedStreamWriter(target, cancellationToken, initiallySync: true),
            WriteMode.Async => new SwitchableBufferedStreamWriter(target, cancellationToken, initiallySync: false),
            WriteMode.Pipe => new PipeStreamWriter(target, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly CancellationToken _cancellationToken = cancellationToken;
    protected ref readonly CancellationToken CancellationToken => ref _cancellationToken;
    protected Stream Target { get; } = target;

    public abstract Task WriteComplete { get; }

    protected void OnWritten(long count) => _totalBytesWritten += count; // single-writer; this is fine
    protected void OnWritten(int count) => _totalBytesWritten += count; // single-writer; this is fine
    private long _totalBytesWritten;
    public long TotalBytesWritten => Volatile.Read(ref _totalBytesWritten);

    public abstract void Complete(Exception? exception = null);
    public void Dispose() => Target.Dispose();

    public ValueTask DisposeAsync()
    {
        if (Target is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }
        Target.Dispose();
        return default;
    }

    public abstract void Advance(int count);

    public abstract Memory<byte> GetMemory(int sizeHint = 0);

    public abstract Span<byte> GetSpan(int sizeHint = 0);

    [Conditional("DEBUG")]
    public virtual void DebugSetLog(Action<string> log) { }

    public abstract void Flush();

    public virtual bool TransitionToAsync() => false;
}

internal abstract class CycleBufferStreamWriter : BufferedStreamWriter, ICycleBufferCallback
{
    protected CycleBufferStreamWriter(Stream target, CancellationToken cancellationToken, StateFlags flags = StateFlags.None)
        : base(target, cancellationToken)
    {
        _buffer = CycleBuffer.Create(callback: this);
        _stateFlags = flags;
    }

    private CycleBuffer _buffer;
    private volatile StateFlags _stateFlags;
    private Exception? _exception;

    protected bool IsFaulted => Volatile.Read(ref _exception) is not null;

    [Flags]
    internal enum StateFlags
    {
        None = 0,
        Flush = 1 << 0, // allow reading incomplete pages
        ActiveWriter = 1 << 1,
        Closed = 1 << 2,
        TransitionToAsync = 1 << 3,
        AsyncMode = 1 << 4,
    }

    protected bool GetFirstChunkInsideLock(int minBytes, out ReadOnlyMemory<byte> memory)
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(GetFirstChunkInsideLock)} must be called while holding the writer lock.");
        return _buffer.TryGetFirstCommittedMemory(minBytes, out memory);
    }

    protected void ReleaseBuffer() => _buffer.Release();

    protected void DiscardCommitted(int count) => _buffer.DiscardCommitted(count);

    /// <summary>
    /// Activate the writer if necessary, but only consume complete pages.
    /// </summary>
    void ICycleBufferCallback.PageComplete() => OnActivate(StateFlags.None);

    /// <summary>
    /// Activate the writer if necessary, and indicate that all committed data can be consumed, even incomplete pages.
    /// </summary>
    public override void Flush() => OnActivate(StateFlags.Flush);

    public override void Complete(Exception? exception = null)
    {
        if (exception is not null) Interlocked.CompareExchange(ref _exception, exception, null);
        OnActivate(StateFlags.Flush | StateFlags.Closed);
    }

    private void OnActivate(StateFlags newFlags)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            ActivateInsideLock(newFlags);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    protected void ActivateInsideLock(StateFlags newFlags)
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(ActivateInsideLock)} must be called while holding the writer lock.");

        var state = _stateFlags;
        if ((state & StateFlags.Closed) != 0) return;
        state |= newFlags & ~StateFlags.ActiveWriter;
        if ((state & StateFlags.ActiveWriter) == 0)
        {
            state |= StateFlags.ActiveWriter;
            _stateFlags = state;
            OnWakeReaderInsideLock();
        }
        else
        {
            _stateFlags = state;
        }
    }

    protected abstract void OnWakeReaderInsideLock();

    [Conditional("DEBUG")]
    protected void OnDebugBufferLog(ReadOnlyMemory<byte> memory)
    {
#if DEBUG
        if (_log is not null)
        {
            const string CR = "\u240D", LF = "\u240A", CRLF = CR + LF;
            string raw = Encoding.UTF8.GetString(memory.Span)
                .Replace("\r\n", CRLF).Replace("\r", CR).Replace("\n", LF);
            string s = $"{id}.{fragment++}: {raw}";
            OnDebugLog(s);
        }
#endif
    }

    protected void TakeLock(ref bool lockTaken)
    {
        if (!lockTaken)
        {
            Monitor.TryEnter(this, 10_000,  ref lockTaken);
            if (!lockTaken) Throw();
        }
        static void Throw() => throw new TimeoutException("Unable to acquire writer lock");
    }

    protected void ReleaseLock(ref bool lockTaken)
    {
        if (lockTaken)
        {
            Monitor.Exit(this);
            lockTaken = false;
        }
    }

    public override void Advance(int count)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            ThrowIfComplete();
            _buffer.Commit(count);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            ThrowIfComplete();
            return _buffer.GetUncommittedMemory(sizeHint);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            ThrowIfComplete();
            return _buffer.GetUncommittedSpan(sizeHint);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowIfComplete()
    {
        // prevents a writer continuing to write to a dead pipe
        if ((_stateFlags & StateFlags.Closed) != 0) ThrowCompleteOrFaulted();
    }

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    protected void ThrowCompleteOrFaulted()
    {
        var ex = Volatile.Read(ref _exception);
        if (ex is null) throw new InvalidOperationException("Output has been completed successfully.");
        throw new InvalidOperationException($"Output has been completed with fault: " + ex.Message, ex);
    }

    internal StateFlags State => _stateFlags;
    protected void RemoveStateFlagInsideLock(StateFlags flags)
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(RemoveStateFlagInsideLock)} must be called while holding the writer lock.");
        _stateFlags &= ~flags;
    }

    protected void AddStateFlagInsideLock(StateFlags flags)
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(AddStateFlagInsideLock)} must be called while holding the writer lock.");
        _stateFlags |= flags;
    }

#if DEBUG
    private readonly int id = Interlocked.Increment(ref s_id);
    private int fragment;
    private static int s_id;
#endif

    [Conditional("DEBUG")]
    protected void OnDebugLog(string message)
    {
#if DEBUG
        // deliberately get away from the working thread
        ThreadPool.QueueUserWorkItem(_ => _log?.Invoke(message));
#endif
    }

    public override void DebugSetLog(Action<string> log)
    {
#if DEBUG
        _log = log;
#endif
    }
#if DEBUG
    private Action<string>? _log;
#endif
}
