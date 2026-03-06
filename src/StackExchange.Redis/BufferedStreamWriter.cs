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
    public enum WriteMode
    {
        Default,
        Sync,
        Async,
        Pipe,
    }

    public static BufferedStreamWriter Create(WriteMode mode, ConnectionType connectionType, Stream target, CancellationToken cancellationToken)
    {
        // TODO: change to Async when debugged
        const WriteMode DefaultAsyncMode = WriteMode.Pipe;

        if (connectionType is ConnectionType.Subscription)
        {
            // sync-mode targets latency; pub/sub doens't need that
            mode = DefaultAsyncMode;
        }
        else if (mode is WriteMode.Default)
        {
            mode = DefaultAsyncMode;
        }
        return mode switch
        {
            WriteMode.Sync => new BufferedSyncStreamWriter(target, cancellationToken),
            WriteMode.Async => new BufferedAsyncStreamWriter(target, cancellationToken),
            WriteMode.Pipe => new PipeStreamWriter(target, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly CancellationToken _cancellationToken = cancellationToken;
    protected ref readonly CancellationToken CancellationToken => ref _cancellationToken;
    protected Stream Target { get; } = target;

    public abstract Task WriteComplete { get; }

    protected void OnWritten(long count) => _totalBytesWritten += count;
    protected void OnWritten(int count) => _totalBytesWritten += count;
    private long _totalBytesWritten;
    public long TotalBytesWritten => _totalBytesWritten;

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
}

internal abstract class CycleBufferStreamWriter : BufferedStreamWriter, ICycleBufferCallback
{
    protected CycleBufferStreamWriter(Stream target, CancellationToken cancellationToken)
        : base(target, cancellationToken)
    {
        _buffer = CycleBuffer.Create(callback: this);
    }

    private CycleBuffer _buffer;
    private StateFlags _stateFlags;
    private Exception? _exception;

    protected bool IsFaulted => _exception is not null;

    [Flags]
    protected enum StateFlags
    {
        None = 0,
        Flush = 1 << 0, // allow reading incomplete pages
        ActiveWriter = 1 << 1,
        Closed = 1 << 2,
    }

    protected bool GetFirstChunkInsideLock(int minBytes, out ReadOnlyMemory<byte> memory)
        => _buffer.TryGetFirstCommittedMemory(minBytes, out memory);

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
        _exception ??= exception;
        OnActivate(StateFlags.Flush | StateFlags.Closed);
    }

    private void OnActivate(StateFlags newFlags)
    {
        bool activate = false;
        lock (this)
        {
            var state = _stateFlags;
            if ((state & StateFlags.Closed) != 0) return;
            state |= newFlags & ~StateFlags.ActiveWriter;
            if ((state & StateFlags.ActiveWriter) == 0)
            {
                state |= StateFlags.ActiveWriter;
                activate = true;
            }
            _stateFlags = state;
        }

        if (activate) OnWakeReader();
    }

    protected abstract void OnWakeReader();

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

    public override void Advance(int count)
    {
        ThrowIfComplete();
        lock (this)
        {
            _buffer.Commit(count);
        }
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfComplete();
        lock (this)
        {
            return _buffer.GetUncommittedMemory(sizeHint);
        }
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfComplete();
        lock (this)
        {
            return _buffer.GetUncommittedSpan(sizeHint);
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
        var ex = _exception;
        if (ex is null) throw new InvalidOperationException("Output has been completed successfully.");
        throw new InvalidOperationException($"Output has been completed with fault: " + ex.Message, ex);
    }

    protected StateFlags State => _stateFlags;
    protected void OnWriterInactive() => _stateFlags &= ~StateFlags.ActiveWriter;

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
