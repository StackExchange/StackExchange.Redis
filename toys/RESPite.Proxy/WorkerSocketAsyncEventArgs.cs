using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;

namespace RESPite.Proxy;

internal sealed class WorkerSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource<int>, IValueTaskSource
{
    // note: we only support *one* OnCompleted; undefined behaviour otherwise; we explicitly
    // consider "completion before await", and "completion after await", but we *do not* consider
    // double-await, overlapped await, etc
    private readonly WorkerPool _pool;
    public WorkerPool Pool => _pool;
    private PointerManager? _pointerBuffer;

    internal WorkerSocketAsyncEventArgs(WorkerPool? pool = null)
    {
        _pool = pool ?? WorkerPool.Demand();
    }

    private WorkerStep _step;
    public WorkerStep Step => _step;

    private static readonly Action<object?> _continuationCompleted = _ => { };
    private object? _continuationState;
    private volatile Action<object?>? _continuation;
    private CancellationTokenRegistration _cancellation;

    public Task AsUntypedTask()
    {
        return GetStatus(0) switch
        {
            ValueTaskSourceStatus.Succeeded => Task.CompletedTask,
            ValueTaskSourceStatus.Faulted => FromFault(SocketError),
            _ => new ValueTask(this, 0).AsTask(),
        };

        static Task FromFault(SocketError error) =>
            Task.FromException(new SocketException((int)error));
    }

    public ValueTask AsUntypedValueTask()
    {
        return GetStatus(0) switch
        {
            ValueTaskSourceStatus.Succeeded => default,
            ValueTaskSourceStatus.Faulted => FromFault(SocketError),
            _ => new(this, 0),
        };

        static ValueTask FromFault(SocketError error) =>
            ValueTask.FromException(new SocketException((int)error));
    }

    public ValueTask<int> AsTypedValueTask()
    {
        return GetStatus(0) switch
        {
            ValueTaskSourceStatus.Succeeded => new(BytesTransferred),
            ValueTaskSourceStatus.Faulted => FromFault(SocketError),
            _ => new(this, 0),
        };

        static ValueTask<int> FromFault(SocketError error) =>
            ValueTask.FromException<int>(new SocketException((int)error));
    }

    public Task<int> AsTypedTask()
    {
        return GetStatus(0) switch
        {
            ValueTaskSourceStatus.Succeeded => Task.FromResult(BytesTransferred),
            ValueTaskSourceStatus.Faulted => FromFault(SocketError),
            _ => new ValueTask<int>(this, 0).AsTask(),
        };

        static Task<int> FromFault(SocketError error) =>
            Task.FromException<int>(new SocketException((int)error));
    }

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        var step = _step;
        if (step is WorkerStep.SocketPumpAwait or WorkerStep.MonitorPulse
            && LastOperation is SocketAsyncOperation.Send
            && UserToken is Socket socket
            && SliceSendBuffer() is SliceOutcome.Sliced)
        {
            // partial write; do more!
            if (SendAllAsync(socket)) return; // went async
        }

        var tmp = _cancellation;
        _cancellation = default;
        tmp.Dispose();

        switch (step)
        {
            case WorkerStep.None:
                base.OnCompleted(e);
                break;
            case WorkerStep.MonitorPulse:
                if (!WorkerPool.TryPulse(_continuationState!, 0))
                {
                    // not immediately available - push to worker
                    _pool.Enqueue(_continuationState!, WorkerStep.MonitorPulse, e);
                }
                break;
            case WorkerStep.SocketPumpAwait:
                // async/await mode
                var c = _continuation;

                if (c != null || (c = Interlocked.CompareExchange(ref _continuation, _continuationCompleted, null)) != null)
                {
                    _continuation = _continuationCompleted;
                    _pool.Enqueue(this, WorkerStep.SocketPumpAwait, c);
                }

                break;
            default:
                // push mode
                _pool.Enqueue(_continuationState!, step);
                break;
        }
    }

    void IValueTaskSource.GetResult(short token) => GetResult(token);

    public int GetResult(short token = 0)
    {
        _continuation = null;
        if (SocketError != SocketError.Success) Throw();
        return BytesTransferred;
    }
    private void Throw() => throw new SocketException((int)SocketError);

    public ValueTaskSourceStatus GetStatus(short token = 0) => !ReferenceEquals(_continuation, _continuationCompleted) ? ValueTaskSourceStatus.Pending :
            SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
            ValueTaskSourceStatus.Faulted;

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

    public void SetCancellation(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cancellation = cancellationToken.Register(
                static state => Unsafe.As<WorkerSocketAsyncEventArgs>(state!).Abort(), this, false);
        }
    }

    private void Abort()
    {
        SocketError = SocketError.OperationAborted;
        Dispose();
    }

    public unsafe void SetBuffer(byte* ptr, int length)
    {
        if (length is 0)
        {
            base.SetBuffer(default);
        }
        else
        {
            var buffer = _pointerBuffer ??= new PointerManager();
            buffer.Init(ptr, length);
            base.SetBuffer(buffer.Memory);
        }
    }

    private sealed unsafe class PointerManager : MemoryManager<byte>
    {
        private byte* _pointer;
        private int _length;
        public void Init(byte* pointer, int length)
        {
            _length = length;
            _pointer = pointer;
        }

        protected override void Dispose(bool disposing)
        {
            _length = 0;
            _pointer = null;
        }

        public override Span<byte> GetSpan() => _length is 0 ? default : new(_pointer, _length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);
        public override void Unpin() { }
    }

    public void SetBuffer(ReadOnlyMemory<byte> buffer) // very common for write scenarios
        => base.SetBuffer(MemoryMarshal.AsMemory(buffer));

    public void SetStep(WorkerStep step) => _step = step;
    public void SetTarget(object target) => _continuationState = target;

    public void Init(object target, WorkerStep step, CancellationToken cancellationToken)
    {
        Init(target, step);
        SetCancellation(cancellationToken);
    }

    public bool SendAllAsync(Socket socket)
    {
        do
        {
            if (socket.SendAsync(this)) return true; // went async
        }
        while (SliceSendBuffer() is SliceOutcome.Sliced);
        return false; // completed sync, one way or another
    }

    private enum SliceOutcome
    {
        Fault,
        Sliced,
        Complete,
    }
    private SliceOutcome SliceSendBuffer()
    {
        Debug.Assert(LastOperation is SocketAsyncOperation.Send, "expected send");
        if (SocketError is not SocketError.Success) return SliceOutcome.Fault;
        var sent = BytesTransferred;
        var count = Count;
        if (sent <= 0)
        {
            if (count > 0) return UnableToWriteFullBuffer();
        }
        else if (sent < count)
        {
            if (Buffer is not null)
            {
                // array-based, can slice in place
                SetBuffer(Offset + sent, count - sent);
            }
            else
            {
                var buffer = MemoryBuffer;
                if (buffer.IsEmpty) return UnableToWriteFullBuffer(); // multi-segment?
                SetBuffer(buffer.Slice(sent));
            }
            Debug.Assert(Count == count - sent, "buffer slicing failure");
            return SliceOutcome.Sliced;
        }

        return SliceOutcome.Complete;
    }

    private SliceOutcome UnableToWriteFullBuffer()
    {
        // well that's weird
        SocketError = SocketError.Interrupted;
        return SliceOutcome.Fault;
    }

    public void Init(object target, WorkerStep step)
    {
        _step = step;
        _continuationState = target;
    }

    public void SetForAsync() => _step = WorkerStep.SocketPumpAwait;

    public void SetForPulse(object @lock)
    {
        _step = WorkerStep.MonitorPulse;
        _continuationState = @lock;
    }

    public void WaitForPulse() => Monitor.Wait(_continuationState!);
}
