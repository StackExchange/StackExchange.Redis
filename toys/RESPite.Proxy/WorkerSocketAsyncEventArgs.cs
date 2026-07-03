using System.Net.Sockets;
using System.Runtime.InteropServices;
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

    private WorkerStep _step;
    public WorkerStep Step => _step;

    private static readonly Action<object?> _continuationCompleted = _ => { };
    private object? _continuationState;
    private volatile Action<object?>? _continuation;

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        var step = _step;
        if (step is WorkerStep.None)
        {
            // async/await mode
            var c = _continuation;

            if (c != null || (c = Interlocked.CompareExchange(ref _continuation, _continuationCompleted, null)) != null)
            {
                _continuation = _continuationCompleted;
                _pool.Enqueue(this, WorkerStep.SocketPumpAwait, c);
            }
        }
        else
        {
            // push mode
            _pool.Enqueue(_continuationState!, _step);
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

    public void SetBuffer(ReadOnlyMemory<byte> buffer) // very common for write scenarios
        => base.SetBuffer(MemoryMarshal.AsMemory(buffer));

    public void Init(object target, WorkerStep step)
    {
        _continuationState = target;
        _step = step;
    }
}
