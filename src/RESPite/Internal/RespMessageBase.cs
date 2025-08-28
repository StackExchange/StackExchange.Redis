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
        Flag_InlineParser = 1 << 7; // we can safely use the parser on the IO thread

    protected abstract TResponse Parse(ref RespReader reader);

    protected RespMessageBase<TResponse> InitParser(object? parser)
    {
        if (parser is not null)
        {
            int flags = Flag_Parser;
            if (parser is IRespMetadataParser) flags |= Flag_MetadataParser;
            if (parser is IRespInlineParser) flags |= Flag_InlineParser;
            SetFlag(flags);
        }

        return this;
    }

    public bool TrySetResult(scoped ReadOnlySpan<byte> payload) => throw new NotImplementedException();

    public bool TrySetResult(ReadOnlySequence<byte> response)
    {
        Debug.Assert(GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Pending);
        if (HasFlag(Flag_OutcomeKnown)) return false;
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

    public RespMessageBase<TResponse> SetRequest(byte[] oversized, int offset, int length, ArrayPool<byte>? pool)
    {
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = new ReadOnlyMemory<byte>(oversized, offset, length);
        _requestOwner = pool;
        _requestRefCount = 1;
        return this;
    }

    public RespMessageBase<TResponse> SetRequest(ReadOnlyMemory<byte> request, IDisposable? owner = null)
    {
        Debug.Assert(_requestRefCount == 0, "trying to set a request more than once");
        _request = request;
        _requestOwner = owner;
        _requestRefCount = 1;
        return this;
    }

    private void UnregisterCancellation()
    {
        _cancellationTokenRegistration.Dispose();
        _cancellationTokenRegistration = default;
        _cancellationToken = CancellationToken.None;
    }

    public virtual void Reset()
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

    public bool TryReserveRequest(out ReadOnlyMemory<byte> payload, bool recordSent = true)
    {
        payload = default;
        while (true) // need to take reservation
        {
            Debug.Assert(_asyncCore.GetStatus(_asyncCore.Version) == ValueTaskSourceStatus.Pending);

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
        }

        return GetResult(token);

        static void ThrowWillNotPulse() => throw new InvalidOperationException(
            "This operation cannot be waited because it entered async/await mode - most likely by calling AsTask()");
    }

    private bool TrySetResult(TResponse response)
    {
        if (!TrySetOutcomeKnown()) return false;

        _asyncCore.SetResult(response);
        SetFullyComplete();
        return true;
    }

    private bool TrySetTimeout()
    {
        if (!TrySetOutcomeKnown()) return false;

        _asyncCore.SetException(new TimeoutException());
        SetFullyComplete();
        return true;
    }

    public bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        if (!TrySetOutcomeKnown()) return false;
        if (!cancellationToken.IsCancellationRequested)
        {
            // use our own token if nothing more specific supplied
            cancellationToken = _cancellationToken;
        }

        _asyncCore.SetException(new OperationCanceledException(cancellationToken));
        SetFullyComplete();
        return true;
    }

    public bool TrySetException(Exception exception)
    {
        if (!TrySetOutcomeKnown()) return false; // first winner only
        _asyncCore.SetException(exception);
        SetFullyComplete();
        return true;
    }

    private void SetFullyComplete()
    {
        var pulse = !HasFlag(Flag_NoPulse);
        SetFlag(Flag_Complete | Flag_NoPulse);

        // for safety, always take the lock unless we know they've actively exited
        if (pulse)
        {
            lock (this)
            {
                Monitor.PulseAll(this);
            }
        }
    }

    public TResponse GetResult(short token)
    {
        Debug.Assert(HasFlag(Flag_Complete), "Operation should already be completed");
        var result = _asyncCore.GetResult(token);
        // if we get here, we're success
        return result;
    }
}
