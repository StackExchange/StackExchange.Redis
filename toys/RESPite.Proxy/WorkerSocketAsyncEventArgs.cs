using System.Net.Sockets;
using System.Threading.Tasks.Sources;

namespace RESPite.Proxy;

internal sealed class WorkerSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<int>
{
    // note: we only support *one* OnCompleted; undefined behaviour otherwise; we explicitly
    // consider "completion before await", and "completion after await", but we *do not* consider
    // double-await, overlapped await, etc
    private readonly WorkerPool _pool;
    public WorkerPool Pool => _pool;
    internal WorkerSocketAsyncEventArgs(WorkerPool? pool = null)
    {
        _pool = pool ?? WorkerPool.Demand();
    }

    private int _step;
    public WorkerStep Step
    {
        get => (WorkerStep)_step;
        set => _step = (int)value;
    }

    private static readonly Action<object?> _continuationCompleted = _ => { };
    private object? _continuationState;
    private volatile Action<object?>? _continuation;

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        var c = _continuation;

        if (c != null || (c = Interlocked.CompareExchange(ref _continuation, _continuationCompleted, null)) != null)
        {
            // await mode; inspired by aspnetcore
            var continuationState = UserToken;
            UserToken = null;
            _continuation = _continuationCompleted;

            _pool.Enqueue(this, WorkerStep.SocketPumpAwait, c);
        }
        else
        {
            // push mode
            var next = Interlocked.Exchange(ref _step, 0);
            if (next is not 0)
            {
                _pool.Enqueue(this, (WorkerStep)next);
            }
        }
    }

    public int GetResult(short token)
    {
        _continuation = null;
        if (SocketError != SocketError.Success) Throw();
        return BytesTransferred;
    }
    private void Throw() => throw new SocketException((int)SocketError);

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return !ReferenceEquals(_continuation, _continuationCompleted) ? ValueTaskSourceStatus.Pending :
            SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
            ValueTaskSourceStatus.Faulted;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _continuationState = state;
        var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
        if (ReferenceEquals(prevContinuation, _continuationCompleted))
        {
            // push back to pool rather than invoke inline, to prevent stack-dive
            _pool.Enqueue(this, WorkerStep.SocketPumpAwait, continuation);
        }
    }

    internal void PumpAwait(Action<object?> continuation)
        => continuation.Invoke(Interlocked.Exchange(ref _continuationState, null));
}
