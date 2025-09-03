using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Internal;

internal abstract class RespMessageBase : IValueTaskSource
{
    protected RespMessageBase() => RespOperation.DebugOnAllocateMessage();

    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationTokenRegistration;
    private int _requestRefCount, _flags;
    private ReadOnlySequence<byte> _request;
    public ref readonly CancellationToken CancellationToken => ref _cancellationToken;

    protected const int
        Flag_Sent = 1 << 0, // the request has been sent
        Flag_OutcomeKnown = 1 << 1, // controls which code flow gets to set an outcome
        Flag_Complete = 1 << 2, // indicates whether all follow-up has completed
        Flag_NoPulse = 1 << 4, // don't pulse when completing - either async, or timeout
        Flag_Parser = 1 << 5, // we have a parser
        Flag_MetadataParser = 1 << 6, // the parser wants to consume metadata
        Flag_InlineParser = 1 << 7, // we can safely use the parser on the IO thread
        Flag_Doomed = 1 << 8; // something went wrong, do not recycle

    protected int Flags => Volatile.Read(ref _flags);
    public virtual int MessageCount => 1;

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

    public bool TrySetResult(short token, ref RespReader reader)
    {
        if (HasFlag(Flag_OutcomeKnown) | Token != token) return false;
        var flags = _flags & (Flag_MetadataParser | Flag_Parser);
        switch (flags)
        {
            case Flag_Parser:
            case Flag_Parser | Flag_MetadataParser:
                try
                {
                    if ((flags & Flag_MetadataParser) == 0)
                    {
                        reader.MoveNext();
                    }

                    return TrySetResultPrecheckedToken(ref reader);
                }
                catch (Exception ex)
                {
                    return TrySetExceptionPrecheckedToken(ex);
                }
            default:
                return TrySetDefaultResultPrecheckedToken();
        }
    }

    public bool TrySetResult(short token, scoped ReadOnlySpan<byte> response)
    {
        RespReader reader = new(response);
        return TrySetResult(token, ref reader);
    }

    public bool TrySetResult(short token, in ReadOnlySequence<byte> response)
    {
        RespReader reader = new(response);
        return TrySetResult(token, ref reader);
    }

    protected abstract bool TrySetResultPrecheckedToken(ref RespReader reader);
    protected abstract bool TrySetDefaultResultPrecheckedToken();

    public abstract short Token { get; }

    private protected abstract void CheckToken(short token);

    private protected abstract ValueTaskSourceStatus OwnStatus { get; }

    public abstract ValueTaskSourceStatus GetStatus(short token);

    public bool IsSent(short token)
    {
        CheckToken(token);
        return HasFlag(Flag_Sent);
    }

    protected bool SetFlag(int flag)
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
    protected bool HasFlag(int flag) => (Volatile.Read(ref _flags) & flag) != 0;

    public void Init(bool sent, CancellationToken cancellationToken)
    {
        Debug.Assert(_flags == 0, "flags should be zero");
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        if (sent) SetFlag(Flag_Sent);
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationToken = cancellationToken;
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
        }
    }

    public void Init(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken) => Init(new ReadOnlySequence<byte>(request), cancellationToken);

    public void Init(
        ReadOnlySequence<byte> request,
        CancellationToken cancellationToken)
    {
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = request;
        _requestRefCount = 1;
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationToken = cancellationToken;
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
        }
    }

    protected void UnregisterCancellation()
    {
        _cancellationTokenRegistration.Dispose();
        _cancellationTokenRegistration = default;
        _cancellationToken = CancellationToken.None;
    }

    public virtual void Reset(bool recycle)
    {
        Debug.Assert(
            !recycle || OwnStatus == ValueTaskSourceStatus.Succeeded,
            "We should only be recycling completed messages");
        // note we only reset on success, and on
        // success we've already unregistered cancellation
        _request = default;
        _requestRefCount = 0;
        _flags = 0;
        NextToken();
        if (recycle) Recycle();
    }

    protected abstract void Recycle();
    protected abstract void NextToken();

    public bool TryReserveRequest(short token, out ReadOnlySequence<byte> payload, bool recordSent = true)
    {
        while (true) // redo in case of CEX failure
        {
            Debug.Assert(OwnStatus == ValueTaskSourceStatus.Pending);

            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0 | token != Token)
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

    private bool TryReleaseRequest() // bool here means "it wasn't already zero"; it doesn't mean "it became zero"
    {
        while (true)
        {
            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, oldCount - 1, oldCount) == oldCount)
            {
                if (oldCount == 1) // we were the last one; recycle
                {
                    BlockBufferSerializer.BlockBuffer.Release(in _request);
                    _request = default;
                }

                return true;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected void ThrowNotSent(short token)
    {
        CheckToken(token); // prefer a token explanation
        throw new InvalidOperationException(
            "This command has not yet been sent; waiting is not possible. If this is a transaction or batch, you must execute that first.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected void SetNotSentAsync(short token)
    {
        CheckToken(token);
        TrySetExceptionPrecheckedToken(new InvalidOperationException(
            "This command has not yet been sent; awaiting is not possible. If this is a transaction or batch, you must execute that first."));
    }

    // spoof untyped on top of typed
    void IValueTaskSource.GetResult(short token) => GetResultVoid(token);

    private bool TrySetOutcomeKnown(short token, bool withSuccess)
        => Token == token && TrySetOutcomeKnownPrecheckedToken(withSuccess);

    protected bool TrySetOutcomeKnownPrecheckedToken(bool withSuccess)
    {
        if (!SetFlag(Flag_OutcomeKnown)) return false;
        UnregisterCancellation();
        TryReleaseRequest(); // we won't be needing this again

        // configure threading model; failure can be triggered from any thread - *always*
        // dispatch to pool; in the success case, we're either on the IO thread
        // (if inline-parsing is enabled) - in which case, yes: dispatch - or we've
        // already jumped to a pool thread for the parse step. So: the only
        // time we want to complete inline is success and not inline-parsing.
        SetRunContinuationsAsynchronously(!withSuccess | AllowInlineParsing);

        return true;
    }

    private protected abstract void SetRunContinuationsAsynchronously(bool value);
    public abstract void GetResultVoid(short token);
    public abstract void WaitVoid(short token, TimeSpan timeout);

    public bool TrySetCanceled(short token, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            // use our own token if nothing more specific supplied
            cancellationToken = _cancellationToken;
        }

        return token == Token && TrySetCanceledPrecheckedToken(cancellationToken);
    }

    // this is the path used by cancellation registration callbacks; always use our own
    // cancellation token, and we must trust the version token
    internal void TrySetCanceledTrustToken() => TrySetCanceledPrecheckedToken(_cancellationToken);

    private bool TrySetCanceledPrecheckedToken(CancellationToken cancellationToken)
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false;
        SetExceptionPreChecked(new OperationCanceledException(cancellationToken));
        SetFullyComplete(success: false);
        return true;
    }

    public bool TrySetException(short token, Exception exception)
        => token == Token && TrySetExceptionPrecheckedToken(exception);

    private protected abstract void SetExceptionPreChecked(Exception exception);

    private bool TrySetExceptionPrecheckedToken(Exception exception)
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false; // first winner only
        SetExceptionPreChecked(exception);
        SetFullyComplete(success: false);
        return true;
    }

    protected void SetFullyComplete(bool success)
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

    protected bool TrySetTimeoutPrecheckedToken()
    {
        if (!TrySetOutcomeKnownPrecheckedToken(false)) return false;

        SetExceptionPreChecked(new TimeoutException());
        SetFullyComplete(success: false);
        return true;
    }

    public abstract void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags);

    public abstract void OnCompletedWithNotSentDetection(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags);
}
