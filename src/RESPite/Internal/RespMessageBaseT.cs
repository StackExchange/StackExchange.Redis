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
            _ = _asyncCore.GetStatus(token); // get consistent exception message
        }
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
        SetFlag(Flag_NoPulse); // async doesn't need to be pulsed
        _asyncCore.OnCompleted(continuation, state, token, flags);
    }

    public override void OnCompletedWithNotSentDetection(
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

    private protected override void SetRunContinuationsAsynchronously(bool value)
        => _asyncCore.RunContinuationsAsynchronously = value;

    public override void GetResultVoid(short token) => _ = GetResult(token);
    public override void WaitVoid(short token, TimeSpan timeout) => _ = Wait(token, timeout);

    public TResponse Wait(short token, TimeSpan timeout)
    {
        switch (Flags & (Flag_Complete | Flag_Sent))
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
            switch (Flags & (Flag_Complete | Flag_NoPulse))
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

    private protected override void SetExceptionPreChecked(Exception exception)
        => _asyncCore.SetException(exception);

    protected override bool TrySetResultPrecheckedToken(ref RespReader reader) =>
        TrySetResultPrecheckedToken(Parse(ref reader));

    protected override bool TrySetDefaultResultPrecheckedToken()
        => TrySetResultPrecheckedToken(default!);

    protected override void NextToken() => _asyncCore.Reset();
}
