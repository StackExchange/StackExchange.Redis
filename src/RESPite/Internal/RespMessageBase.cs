using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Internal;

internal abstract class RespMessageBase<TResponse> : IRespMessage, IValueTaskSource<TResponse>
{
    protected RespMessageBase() => RespOperation.DebugOnAllocateMessage();

    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationTokenRegistration;

    private ReadOnlyMemory<byte> _request;

    private int _requestRefCount;
    private int _flags;
    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;
    public ref readonly CancellationToken CancellationToken => ref _cancellationToken;

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
        if (parser is null)
        {
            SetFlag(Flag_InlineParser); // F+F
        }
        else
        {
            int flags = Flag_Parser;
            // detect parsers that want to manually parse attributes, errors, etc.
            if (parser is IRespMetadataParser) flags |= Flag_MetadataParser;
            // detect fast, internal, non-allocating parsers (int, bool, etc.)
            if (parser is IRespInlineParser) flags |= Flag_InlineParser;
            SetFlag(flags);
        }
    }

    public bool AllowInlineParsing => HasFlag(Flag_InlineParser);

    public bool TrySetResult(short token, scoped ReadOnlySpan<byte> response)
    {
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

                    return TrySetResultPrecheckedToken(Parse(ref reader));
                }
                catch (Exception ex)
                {
                    return TrySetExceptionPrecheckedToken(ex);
                }
            default:
                return TrySetResultPrecheckedToken(default(TResponse)!);
        }
    }

    public short Token => _asyncCore.Version;

    public bool IsSent(short token)
    {
        CheckToken(token);
        return HasFlag(Flag_Sent);
    }

    public bool TrySetResult(short token, in ReadOnlySequence<byte> response)
    {
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

                    return TrySetResultPrecheckedToken(Parse(ref reader));
                }
                catch (Exception ex)
                {
                    return TrySetExceptionPrecheckedToken(ex);
                }
            default:
                return TrySetResultPrecheckedToken(default(TResponse)!);
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

    public RespMessageBase<TResponse> Init(bool sent, CancellationToken cancellationToken)
    {
        Debug.Assert(_flags == 0, "flags should be zero");
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        if (sent) SetFlag(Flag_Sent);
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationToken = cancellationToken;
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
        }

        return this;
    }

    public RespMessageBase<TResponse> Init(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = request;
        _requestRefCount = 1;
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
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
        _requestRefCount = 0;
        _flags = 0;
        _asyncCore.Reset();
        if (recycle) Recycle();
    }

    protected abstract void Recycle();

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
                    _request.DebugScramble();
                    if (MemoryMarshal.TryGetMemoryManager<byte, BlockBufferSerializer.BlockBuffer>(_request, out var block))
                    {
                        block.Release();
                    }
                    _request = default;
                }

                return true;
            }
        }
    }

    /* asking about the status too early is usually a very bad sign that they're doing
     something like awaiting a message in a transaction that hasn't been sent */
    public ValueTaskSourceStatus GetStatus(short token)
        => _asyncCore.GetStatus(token);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowNotSent(short token)
    {
        CheckToken(token); // prefer a token explanation
        throw new InvalidOperationException(
            "This command has not yet been sent; waiting is not possible. If this is a transaction or batch, you must execute that first.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SetNotSentAsync(short token)
    {
        CheckToken(token);
        TrySetExceptionPrecheckedToken(new InvalidOperationException(
            "This command has not yet been sent; awaiting is not possible. If this is a transaction or batch, you must execute that first."));
    }

    private void CheckToken(short token)
    {
        if (token != _asyncCore.Version) // use cheap test
        {
            _ = _asyncCore.GetStatus(token); // get consistent exception message
        }
    }

    // this is used from Task/ValueTask; we can't avoid that - in theory
    // we *coiuld* sort of make it work for ValueTask, but if anyone
    // calls .AsTask() on it, it would fail
    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        CheckToken(token);
        SetFlag(Flag_NoPulse); // async doesn't need to be pulsed
        _asyncCore.OnCompleted(continuation, state, token, flags);
    }

    public void OnCompletedWithNotSentDetection(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        CheckToken(token);
        if (!HasFlag(Flag_Sent)) SetNotSentAsync(token);
        SetFlag(Flag_NoPulse); // async doesn't need to be pulsed
        _asyncCore.OnCompleted(continuation, state, token, flags);
    }

    // spoof untyped on top of typed
    void IValueTaskSource.GetResult(short token) => _ = GetResult(token);
    void IRespMessage.Wait(short token, TimeSpan timeout) => _ = Wait(token, timeout);

    private bool TrySetOutcomeKnown(short token, bool withSuccess)
        => _asyncCore.Version == token && TrySetOutcomeKnownPrecheckedToken(withSuccess);

    private bool TrySetOutcomeKnownPrecheckedToken(bool withSuccess)
    {
        if (!SetFlag(Flag_OutcomeKnown)) return false;
        UnregisterCancellation();
        TryReleaseRequest(); // we won't be needing this again

        // configure threading model; failure can be triggered from any thread - *always*
        // dispatch to pool; in the success case, we're either on the IO thread
        // (if inline-parsing is enabled) - in which case, yes: dispatch - or we've
        // already jumped to a pool thread for the parse step. So: the only
        // time we want to complete inline is success and not inline-parsing.
        _asyncCore.RunContinuationsAsynchronously = !withSuccess | AllowInlineParsing;

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
                ThrowNotSent(token); // always throws
                break;
        }

        bool isTimeout = false;
        CheckToken(token);
        lock (this)
        {
            switch (Volatile.Read(ref _flags) & (Flag_Complete | Flag_NoPulse))
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
        if (isTimeout) TrySetTimeoutPrecheckedToken();

        return GetResult(token);

        static void ThrowWillNotPulse() => throw new InvalidOperationException(
            "This operation cannot be waited because it entered async/await mode - most likely by calling AsTask()");
    }

    private bool TrySetResultPrecheckedToken(TResponse response)
    {
        if (!TrySetOutcomeKnownPrecheckedToken(true)) return false;

        _asyncCore.SetResult(response);
        SetFullyComplete(success: true);
        return true;
    }

    private bool TrySetTimeoutPrecheckedToken()
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false;

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

        return token == _asyncCore.Version && TrySetCanceledPrecheckedToken(cancellationToken);
    }

    // this is the path used by cancellation registration callbacks; always use our own
    // cancellation token, and we must trust the version token
    void IRespMessage.TrySetCanceled() => TrySetCanceledPrecheckedToken(_cancellationToken);

    private bool TrySetCanceledPrecheckedToken(CancellationToken cancellationToken)
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false;
        _asyncCore.SetException(new OperationCanceledException(cancellationToken));
        SetFullyComplete(success: false);
        return true;
    }

    public bool TrySetException(short token, Exception exception)
        => token == _asyncCore.Version && TrySetExceptionPrecheckedToken(exception);

    private bool TrySetExceptionPrecheckedToken(Exception exception)
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false; // first winner only
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
