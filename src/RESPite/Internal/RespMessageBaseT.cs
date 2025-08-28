using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace RESPite.Internal;

internal abstract class RespMessageBaseT<TResponse> : IRespMessage, IValueTaskSource<TResponse>
{
    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationTokenRegistration;

    private ReadOnlyMemory<byte> _request;
    private object? _requestOwner;

    private int _requestRefCount;
    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;
    private int _flags;

    private const int
        Flag_Sent = 1 << 0, // the request has been sent
        Flag_OutcomeKnown = 1 << 1, // controls which code flow gets to set an outcome
        Flag_Complete = 1 << 2, // indicates whether all follow-up has completed
        Flag_NoPulse = 1 << 3; // don't pulse when completing - either async, or timeout

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

    public void Init(byte[] oversized, int offset, int length, ArrayPool<byte>? pool)
    {
        _request = new ReadOnlyMemory<byte>(oversized, offset, length);
        _requestOwner = pool;
        _requestRefCount = 1;
    }

    public void Init(IMemoryOwner<byte> owner, int offset, int length)
    {
        _request = owner.Memory.Slice(offset, length);
        _requestOwner = owner;
        _requestRefCount = 1;
    }

    private void UnregisterCancellation()
    {
        _cancellationTokenRegistration.Dispose();
        _cancellationTokenRegistration = default;
        _cancellationToken = CancellationToken.None;
    }

    public void Reset()
    {
        Debug.Assert(
            _asyncCore.GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Succeeded,
            "We should only be resetting completed messages");
        // note we only reset on success, and on
        // success we've already unregistered cancellation
        _request = default;
        _requestOwner = null;
        _requestRefCount = 0;
        _asyncCore.Reset();
        _flags = 0;
    }

    public bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true)
    {
        payload = default;
        while (true) // need to take reservation
        {
            // check completion (and the token)
            if (_asyncCore.GetStatus(token) != ValueTaskSourceStatus.Pending) return false;

            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, checked(oldCount + 1), oldCount) == oldCount)
            {
                break;
            }
        }

        if (recordSent) SetFlag(Flag_Sent);

        payload = _request;
        return true;
    }

    public void ReleaseRequest(short token)
    {
        if (!TryReleaseRequest(token)) ThrowReleased();

        static void ThrowReleased() =>
            throw new InvalidOperationException("The request payload has already been released");
    }

    private bool TryReleaseRequest(short token) // bool here means "it wasn't already zero"; it doesn't mean "it became zero"
    {
        while (true)
        {
            CheckToken(token);
            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, oldCount - 1, oldCount) == oldCount)
            {
                if (oldCount == 1) // we were the last one; recycle
                {
                    _parser = null;
                    var arr = _requestPayload;
                    _requestLength = 0;
                    _requestPayload = [];
                    ArrayPool<byte>.Shared.Return(arr);
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
        CheckSent();
        return status;
    }

    private void CheckToken(short token)
    {
        if (token != _asyncCore.Version) // use cheap test
        {
            _ = _asyncCore.GetStatus(token); // get consistent exception message
        }
    }

    private void CheckSent()
    {
        if (!HasFlag(Flag_Sent)) ThrowNotSent();

        static void ThrowNotSent()
            => throw new InvalidOperationException(
                "This command has not yet been sent; awaiting is not possible. If this is a transaction or batch, you must execute that first.");
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _asyncCore.OnCompleted(continuation, state, token, flags);
        SetFlag(Flag_NoPulse); // async doesn't need to be pulsed
    }

    public bool TryCancel(short token, CancellationToken cancellationToken = default)
    {
        if (!TrySetOutcomeKnown(token)) return false;
        if (!cancellationToken.IsCancellationRequested)
        {
            // use our own token if nothing more specific supplied
            cancellationToken = _cancellationToken;
        }

        return TrySetException(token, new OperationCanceledException(cancellationToken));
    }

    public bool TrySetException(short token, Exception exception)
    {
        if (!TrySetOutcomeKnown(token)) return false; // first winner only
        _asyncCore.SetException(exception);
        SetFlag(Flag_Complete);

        // for safety, always take the lock unless we know they've actively exited
        if (!HasFlag(Flag_NoPulse))
        {
            lock (this)
            {
                Monitor.PulseAll(this);
            }
        }

        return true;
    }

    // spoof untyped on top of typed
    void IValueTaskSource.GetResult(short token) => _ = GetResult(token);
    void IRespMessage.Wait(short token, TimeSpan timeout) => _ = Wait(token, timeout);

    private bool TrySetOutcomeKnown(short token)
    {
        CheckToken(token);
        if (!SetFlag(Flag_OutcomeKnown)) return false;
        UnregisterCancellation();
        return true;
    }

    public TResponse Wait(short token, TimeSpan timeout)
    {
        CheckToken(token);
        CheckSent();
        if (!HasFlag(Flag_Complete))
        {
            bool isTimeout = false;
            lock (this)
            {
                if (!HasFlag(Flag_Complete))
                {
                    if (timeout == TimeSpan.Zero)
                    {
                        Monitor.Wait(this);
                    }
                    else if (!Monitor.Wait(this, timeout))
                    {
                        isTimeout = true;
                        SetFlag(Flag_NoPulse);
                    }
                }
            }

            UnregisterCancellation();
            if (isTimeout && TrySetOutcomeKnown(token))
            {
                _asyncCore.SetException(new TimeoutException());
                SetFlag(Flag_Complete);
            }
        }

        return GetResult(token);
    }

    public TResponse GetResult(short token)
    {
        Debug.Assert(HasFlag(Flag_Complete), "Operation should already be completed");
        var result = _asyncCore.GetResult(token);
        // if we get here, we're success
        return result;
    }
}
