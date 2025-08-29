using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Internal;

internal abstract class RespMessageBase<TResponse> : IRespMessage, IValueTaskSource<TResponse>
{
    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationTokenRegistration;

    private ReadOnlyMemory<byte> _request;
    private object? _requestOwner;

    private int _requestRefCount;
    private int _flags;
    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;

    private const int
        Flag_Sent = 1 << 0, // the request has been sent
        Flag_OutcomeKnown = 1 << 1, // controls which code flow gets to set an outcome
        Flag_Complete = 1 << 2, // indicates whether all follow-up has completed
        Flag_NoPulse = 1 << 4, // don't pulse when completing - either async, or timeout
        Flag_Parser = 1 << 5, // we have a parser
        Flag_MetadataParser = 1 << 6, // the parser wants to consume metadata
        Flag_InlineParser = 1 << 7, // we can safely use the parser on the IO thread
        Flag_Doomed = 1 << 8; // something went wrong, do not recyle

    protected abstract TResponse Parse(ref RespReader reader);

    protected void InitParser(object? parser)
    {
        if (parser is not null)
        {
            int flags = Flag_Parser;
            if (parser is IRespMetadataParser) flags |= Flag_MetadataParser;
            if (parser is IRespInlineParser) flags |= Flag_InlineParser;
            SetFlag(flags);
        }
    }

    public bool AllowInlineParsing => HasFlag(Flag_InlineParser);

    [Conditional("DEBUG")]
    private void DebugAssertPending() => Debug.Assert(
        GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Pending & !HasFlag(Flag_OutcomeKnown),
        "Message should be in a pending state");

    public bool TrySetResult(short token, scoped ReadOnlySpan<byte> response)
    {
        DebugAssertPending();
        if (HasFlag(Flag_OutcomeKnown) | _asyncCore.Version != token) return false;
        var flags = _flags & (Flag_MetadataParser | Flag_Parser);
        switch (flags)
        {
            case Flag_Parser:
            case Flag_Parser | Flag_MetadataParser:
                try
                {
                    RespReader reader = new(response);
                    if ((flags & Flag_MetadataParser) == 0)
                    {
                        reader.MoveNext();
                    }

                    return TrySetResult(Parse(ref reader));
                }
                catch (Exception ex)
                {
                    return TrySetException(ex);
                }
            default:
                return TrySetResult(default(TResponse)!);
        }
    }

    public short Token => _asyncCore.Version;

    public bool TrySetResult(short token, in ReadOnlySequence<byte> response)
    {
        DebugAssertPending();
        if (HasFlag(Flag_OutcomeKnown) | _asyncCore.Version != token) return false;
        var flags = _flags & (Flag_MetadataParser | Flag_Parser);
        switch (flags)
        {
            case Flag_Parser:
            case Flag_Parser | Flag_MetadataParser:
                try
                {
                    RespReader reader = new(response);
                    if ((flags & Flag_MetadataParser) == 0)
                    {
                        reader.MoveNext();
                    }

                    return TrySetResult(Parse(ref reader));
                }
                catch (Exception ex)
                {
                    return TrySetException(ex);
                }
            default:
                return TrySetResult(default(TResponse)!);
        }
    }

    private bool SetFlag(int flag)
    {
        Debug.Assert(flag != 0, "trying to set a zero flag");
#if NET5_0_OR_GREATER
        return (Interlocked.Or(ref _flags, flag) & flag) == 0;
#else
        while (true)
        {
            var oldValue = Volatile.Read(ref _flags);
            var newValue = oldValue | flag;
            if (oldValue == newValue ||
                Interlocked.CompareExchange(ref _flags, newValue, oldValue) == oldValue)
            {
                return (oldValue & flag) == 0;
            }
        }
#endif
    }

    // in the "any" sense
    private bool HasFlag(int flag) => (Volatile.Read(ref _flags) & flag) != 0;

    public RespMessageBase<TResponse> Init(byte[] oversized, int offset, int length, ArrayPool<byte>? pool, CancellationToken cancellation)
    {
        DebugAssertPending();
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = new ReadOnlyMemory<byte>(oversized, offset, length);
        _requestOwner = pool;
        _requestRefCount = 1;
        if (cancellation.CanBeCanceled)
        {
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellation);
        }
        return this;
    }

    public RespMessageBase<TResponse> SetRequest(ReadOnlyMemory<byte> request, IDisposable? owner, CancellationToken cancellation)
    {
        DebugAssertPending();
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = request;
        _requestOwner = owner;
        _requestRefCount = 1;
        if (cancellation.CanBeCanceled)
        {
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellation);
        }
        return this;
    }

    private void UnregisterCancellation()
    {
        _cancellationTokenRegistration.Dispose();
        _cancellationTokenRegistration = default;
        _cancellationToken = CancellationToken.None;
    }

    public virtual void Reset(bool recycle)
    {
        Debug.Assert(
            !recycle || _asyncCore.GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Succeeded,
            "We should only be recycling completed messages");
        // note we only reset on success, and on
        // success we've already unregistered cancellation
        _request = default;
        _requestOwner = null;
        _requestRefCount = 0;
        _flags = 0;
        _asyncCore.Reset();
    }

    public bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true)
    {
        while (true) // redo in case of CEX failure
        {
            Debug.Assert(_asyncCore.GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Pending);

            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0 | token != _asyncCore.Version)
            {
                payload = default;
                return false;
            }
            if (Interlocked.CompareExchange(ref _requestRefCount, checked(oldCount + 1), oldCount) == oldCount)
            {
                if (recordSent) SetFlag(Flag_Sent);

                payload = _request;
                return true;
            }
        }
    }

    public void ReleaseRequest()
    {
        if (!TryReleaseRequest()) ThrowReleased();

        static void ThrowReleased() =>
            throw new InvalidOperationException("The request payload has already been released");
    }

    private bool
        TryReleaseRequest() // bool here means "it wasn't already zero"; it doesn't mean "it became zero"
    {
        while (true)
        {
            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, oldCount - 1, oldCount) == oldCount)
            {
                if (oldCount == 1) // we were the last one; recycle
                {
                    if (_requestOwner is ArrayPool<byte> pool)
                    {
                        if (MemoryMarshal.TryGetArray(_request, out var segment))
                        {
                            pool.Return(segment.Array!);
                        }
                    }

                    if (_requestOwner is IDisposable owner)
                    {
                        owner.Dispose();
                    }

                    _request = default;
                    _requestOwner = null;
                }

                return true;
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _asyncCore.GetStatus(token);
    ValueTaskSourceStatus IValueTaskSource<TResponse>.GetStatus(short token) => _asyncCore.GetStatus(token);

    /* if they're awaiting our object directly (i.e. we don't need to worry about Task<T> pre-checking things),
     then we can tell them that a message hasn't been sent, for example transactions / batches */
    public ValueTaskSourceStatus GetStatus(short token)
    {
        // we'd rather see a token error, so check that first
        // (in reality, we expect the token to be right almost always)
        var status = _asyncCore.GetStatus(token);
        if (!HasFlag(Flag_Sent)) ThrowNotSent();
        return status;
    }

    private void CheckToken(short token)
    {
        if (token != _asyncCore.Version) // use cheap test
        {
            _ = _asyncCore.GetStatus(token); // get consistent exception message
        }
    }

    private static void ThrowNotSent()
        => throw new InvalidOperationException(
            "This command has not yet been sent; awaiting is not possible. If this is a transaction or batch, you must execute that first.");

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _asyncCore.OnCompleted(continuation, state, token, flags);
        SetFlag(Flag_NoPulse); // async doesn't need to be pulsed
    }

    // spoof untyped on top of typed
    void IValueTaskSource.GetResult(short token) => _ = GetResult(token);
    void IRespMessage.Wait(short token, TimeSpan timeout) => _ = Wait(token, timeout);

    private bool TrySetOutcomeKnown()
    {
        DebugAssertPending();
        if (!SetFlag(Flag_OutcomeKnown)) return false;
        UnregisterCancellation();
        return true;
    }

    public TResponse Wait(short token, TimeSpan timeout)
    {
        switch (Volatile.Read(ref _flags) & (Flag_Complete | Flag_Sent))
        {
            case Flag_Sent: // this is the normal case
                break;
            case Flag_Complete | Flag_Sent: // already complete
                return GetResult(token);
            default:
                ThrowNotSent();
                break;
        }

        bool isTimeout = false;
        CheckToken(token);
        lock (this)
        {
            switch (Volatile.Read(ref _flags) & Flag_Complete | Flag_NoPulse)
            {
                case Flag_NoPulse | Flag_Complete:
                case Flag_Complete:
                    break; // fine, we're complete
                case 0:
                    // THIS IS OUR EXPECTED BRANCH; not complete, and will pulse
                    if (timeout == TimeSpan.Zero)
                    {
                        Monitor.Wait(this);
                    }
                    else if (!Monitor.Wait(this, timeout))
                    {
                        isTimeout = true;
                        SetFlag(Flag_NoPulse); // no point in being woken, we're exiting
                    }

                    break;
                case Flag_NoPulse:
                    ThrowWillNotPulse();
                    break;
            }
        }

        UnregisterCancellation();
        if (isTimeout) TrySetTimeout();

        return GetResult(token);

        static void ThrowWillNotPulse() => throw new InvalidOperationException(
            "This operation cannot be waited because it entered async/await mode - most likely by calling AsTask()");
    }

    private bool TrySetResult(TResponse response)
    {
        if (!TrySetOutcomeKnown()) return false;

        _asyncCore.SetResult(response);
        SetFullyComplete(success: true);
        return true;
    }

    private bool TrySetTimeout()
    {
        if (!TrySetOutcomeKnown()) return false;

        _asyncCore.SetException(new TimeoutException());
        SetFullyComplete(success: false);
        return true;
    }

    public bool TrySetCanceled(short token, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            // use our own token if nothing more specific supplied
            cancellationToken = _cancellationToken;
        }
        return TrySetCanceled(cancellationToken);
    }

    // this is the path used by cancellation registration callbacks; always use our own token
    void IRespMessage.TrySetCanceled() => TrySetCanceled(_cancellationToken);

    private bool TrySetCanceled(CancellationToken cancellationToken)
    {
        if (!TrySetOutcomeKnown()) return false;
        _asyncCore.SetException(new OperationCanceledException(cancellationToken));
        SetFullyComplete(success: false);
        return true;
    }

    public bool TrySetException(short token, Exception exception)
        => token == _asyncCore.Version && TrySetException(exception);

    private bool TrySetException(Exception exception)
    {
        if (!TrySetOutcomeKnown()) return false; // first winner only
        _asyncCore.SetException(exception);
        SetFullyComplete(success: false);
        return true;
    }

    private void SetFullyComplete(bool success)
    {
        var pulse = !HasFlag(Flag_NoPulse);
        SetFlag(success
            ? (Flag_Complete | Flag_NoPulse)
            : (Flag_Complete | Flag_NoPulse | Flag_Doomed));

        // for safety, always take the lock unless we know they've actively exited
        if (pulse)
        {
            lock (this)
            {
                Monitor.PulseAll(this);
            }
        }
    }

    private TResponse ThrowFailure(short token)
    {
        try
        {
            return _asyncCore.GetResult(token);
        }
        finally
        {
            // we're not recycling; this is for GC reasons only
            Reset(false);
        }
    }

    public TResponse GetResult(short token)
    {
        // failure uses some try/catch logic, let's put that to one side
        if (HasFlag(Flag_Doomed)) return ThrowFailure(token);
        var result = _asyncCore.GetResult(token);
        /*
         If we get here, we're successful; increment "version"/"token" *immediately*. Technically
         we could defer to when it is reused (after recycling), but then repeated calls will appear
         to work for a while, which might lead to undetected problems in local builds (without much concurrency),
         and we'd rather make people know that there's a problem immediately. This also means that any
         continuation primitives (callback/state) are available for GC.
        */
        Reset(true);
        return result;
    }
}
