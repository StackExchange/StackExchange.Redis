using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Internal;

internal abstract class RespMessageBase<TResponse> : RespMessageBase, IValueTaskSource<TResponse>
{
    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;

    protected abstract TResponse Parse(ref RespReader reader);

    public override short Token => _asyncCore.Version;

    private protected override ValueTaskSourceStatus OwnStatus => _asyncCore.GetStatus(_asyncCore.Version);

    /* asking about the status too early is usually a very bad sign that they're doing
     something like awaiting a message in a transaction that hasn't been sent */
    public override ValueTaskSourceStatus GetStatus(short token)
        => _asyncCore.GetStatus(token);

    private protected override void CheckToken(short token)
    {
        if (token != _asyncCore.Version) // use cheap test
        {
            // note that _asyncCore just gives a default InvalidOperationException message; let's see if we can do better
            ThrowInvalidToken();
        }
        static void ThrowInvalidToken() => throw new InvalidOperationException(
            $"The {nameof(RespOperation)} token is invalid; the most likely cause is awaiting an operation multiple times.");
    }

    // this is used from Task/ValueTask; we can't avoid that - in theory
    // we *coiuld* sort of make it work for ValueTask, but if anyone
    // calls .AsTask() on it, it would fail
    public override void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        CheckToken(token);
        SetFlag(StateFlags.NoPulse); // async doesn't need to be pulsed
        _asyncCore.OnCompleted(continuation, state, token, flags);
    }

    public override void OnCompletedWithNotSentDetection(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        CheckToken(token);
        if (!HasFlag(StateFlags.IsSent)) SetNotSentAsync(token);
        SetFlag(StateFlags.NoPulse); // async doesn't need to be pulsed
        _asyncCore.OnCompleted(continuation, state, token, flags);
    }

    private protected override void SetRunContinuationsAsynchronously(bool value)
        => _asyncCore.RunContinuationsAsynchronously = value;

    public override void GetResultVoid(short token) => _ = GetResult(token);
    public override void WaitVoid(short token, TimeSpan timeout) => _ = Wait(token, timeout);

    public TResponse Wait(short token, TimeSpan timeout)
    {
        switch (Flags & (StateFlags.Complete | StateFlags.IsSent))
        {
            case StateFlags.IsSent: // this is the normal case
                break;
            case StateFlags.Complete | StateFlags.IsSent: // already complete
                return GetResult(token);
            default:
                ThrowNotSent(token); // always throws
                break;
        }

        bool isTimeout = false;
        CheckToken(token);
        lock (this)
        {
            switch (Flags & (StateFlags.Complete | StateFlags.NoPulse))
            {
                case StateFlags.NoPulse | StateFlags.Complete:
                case StateFlags.Complete:
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
                        SetFlag(StateFlags.NoPulse); // no point in being woken, we're exiting
                    }

                    break;
                case StateFlags.NoPulse:
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

    private TResponse ThrowFailureWithCleanup(short token)
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

    private static void ThrowSentNotComplete() => throw new InvalidOperationException(
        "This operation has been sent but has not yet completed; the result is not available.");

    public TResponse GetResult(short token)
    {
        // failure uses some try/catch logic, let's put that to one side
        CheckToken(token);
        if (HasFlag(StateFlags.Doomed)) return ThrowFailureWithCleanup(token);

#if DEBUG // more detail
        // Failure uses some try/catch logic, let's put that to one side, and concentrate on success.
        // Also, note that we use OutcomeKnown, not Complete, because it might be an inline callback,
        // in which case we need the caller to be able to get the result *right now*.
        var flags = Flags & (StateFlags.OutcomeKnown | StateFlags.Doomed | StateFlags.IsSent);
        switch (flags)
        {
            // anything doomed
            case StateFlags.OutcomeKnown | StateFlags.Doomed | StateFlags.IsSent:
            case StateFlags.Doomed | StateFlags.IsSent:
            case StateFlags.OutcomeKnown | StateFlags.Doomed:
            case StateFlags.Doomed:
                return ThrowFailureWithCleanup(token);
            // not complete, but sent
            case StateFlags.IsSent when _asyncCore.GetStatus(token) == ValueTaskSourceStatus.Pending:
                ThrowSentNotComplete();
                break;
            // not sent
            case 0:
                ThrowNotSent(token);
                break;
            // everything else is success
        }
#endif

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

    private protected override void SetExceptionPreChecked(Exception exception)
        => _asyncCore.SetException(exception);

    protected override bool TrySetResultPrecheckedToken(ref RespReader reader) =>
        TrySetResultPrecheckedToken(Parse(ref reader));

    protected override bool TrySetDefaultResultPrecheckedToken()
        => TrySetResultPrecheckedToken(default!);

    protected override void NextToken() => _asyncCore.Reset();
}
