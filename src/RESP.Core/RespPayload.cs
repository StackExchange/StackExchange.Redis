using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Resp.RedisCommands;

namespace Resp;

// public abstract class RespPayload : IDisposable
// {
//     public virtual Task WaitAsync() => Task.CompletedTask;
//     public virtual void Wait(TimeSpan timeout) { }
//
//     private bool _isDisposed;
//
//     public void Dispose()
//     {
//         if (!_isDisposed)
//         {
//             _isDisposed = true;
//             Dispose(true);
//         }
//     }
//
//     protected abstract void Dispose(bool disposing);
//     protected abstract ReadOnlySequence<byte> GetPayload();
//
//     /// <inheritdoc/>
//     public override string ToString() => _isDisposed ? "(disposed)" : $"{GetPayload().Length} bytes";
//
//     public ReadOnlySequence<byte> Payload
//     {
//         get
//         {
//             if (_isDisposed) ThrowDisposed(this);
//             return GetPayload();
//
//             static void ThrowDisposed(RespPayload obj) => throw new ObjectDisposedException(obj.GetType().Name);
//         }
//     }
//
//     /// <summary>
//     /// Ensure that this is a valid RESP payload and contains the expected number of top-level elements.
//     /// </summary>
//     /// <param name="checkError">Whether to check for error replies.</param>
//     public void Validate(bool checkError = true)
//     {
//         RespReader reader = new(Payload);
//         int count = 0;
//         while (reader.TryMoveNext(checkError))
//         {
//             reader.SkipChildren();
//             count++;
//         }
//
//         if (count != 1)
//         {
//             throw new InvalidOperationException($"Expected single message, found {count}");
//         }
//     }
//
//     internal static RespPayload Create<TRequest>(
//         scoped ReadOnlySpan<byte> command,
//         in TRequest request,
//         IRespFormatter<TRequest> formatter)
//     {
//         int size = 0;
//         if (formatter is IRespSizeEstimator<TRequest> estimator)
//         {
//             size = estimator.EstimateSize(command, request);
//         }
//         var buffer = AmbientBufferWriter.Get(size);
//         try
//         {
//             var writer = new RespWriter(buffer);
//             formatter.Format(command, ref writer, request);
//             writer.Flush();
//             var payload = buffer.Detach(out int length);
//             return disposeOnWrite
//                 ? new DisposeOnWriteRespPayload(payload, length)
//                 : new ArrayPoolRespPayload(payload, length);
//         }
//         catch
//         {
//             buffer.Reset();
//             throw;
//         }
//     }
//
//     internal TResponse ParseAndDispose<TResponse>(IRespParser<TResponse>? parser = null, TimeSpan timeout = default)
//     {
//         try
//         {
//             Wait(timeout);
//             parser ??= DefaultParsers.Get<TResponse>();
//             var reader = new RespReader(Payload);
//             // ReSharper disable once SuspiciousTypeConversion.Global
//             if (parser is not IRespMetadataParser)
//             {
//                 reader.MoveNext(); // move to content by default
//             }
//             return parser.Parse(ref reader);
//         }
//         finally
//         {
//             Dispose();
//         }
//     }
//
//     internal TResponse ParseAndDispose<TRequest, TResponse>(in TRequest request, IRespParser<TRequest, TResponse> parser)
//     {
//         try
//         {
//             var reader = new RespReader(Payload);
//             // ReSharper disable once SuspiciousTypeConversion.Global
//             if (parser is not IRespMetadataParser)
//             {
//                 reader.MoveNext(); // move to content by default
//             }
//             return parser.Parse(in request, ref reader);
//         }
//         finally
//         {
//             Dispose();
//         }
//     }
//
//     public static RespPayload Create(ReadOnlyMemory<byte> payload) => new ReadOnlyMemoryRespPayload(payload);
//     public static RespPayload Create(ReadOnlySequence<byte> payload) =>
//         payload.IsSingleSegment ? new ReadOnlyMemoryRespPayload(payload.First) : new ReadOnlySequenceRespPayload(payload);
// }
public interface IRespMessage
{
    /// <summary>
    /// Gets the request payload, reserving the value. This must be released using <see cref="ReleaseRequest"/>.
    /// </summary>
    bool TryReserveRequest(out ReadOnlyMemory<byte> payload);

    bool IsCompleted { get; }

    /// <summary>
    /// Releases the request payload.
    /// </summary>
    void ReleaseRequest();

    bool TrySetCanceled(CancellationToken cancellationToken = default);
    bool TrySetException(Exception exception);

    /// <summary>
    /// Parse the response and complete the request.
    /// </summary>
    void ProcessResponse(ref RespReader reader);

    /// <summary>
    /// Cancellation associated with this message. Note that this <b>should not</b> typically be used to
    /// cancel IO operations (for example sockets), as that would break the entire stream - however, it
    /// can be used to interrupt intermediate processing before it is submitted.
    /// </summary>
    CancellationToken CancellationToken { get; }
}

internal static class ActivationHelper
{
    private sealed class WorkItem
#if NETCOREAPP3_0_OR_GREATER
        : IThreadPoolWorkItem
#endif
    {
        private WorkItem()
        {
#if NET5_0_OR_GREATER
            Unsafe.SkipInit(out _payload);
#else
            _payload = [];
#endif
        }

        private void Init(byte[] payload, int length, IRespMessage message)
        {
            _payload = payload;
            _length = length;
            _message = message;
        }

        private byte[] _payload;
        private int _length;
        private IRespMessage? _message;

        private static WorkItem? _spare; // do NOT use ThreadStatic - different producer/consumer, no overlap

        public static void UnsafeQueueUserWorkItem(
            IRespMessage message,
            ReadOnlySpan<byte> payload,
            ref byte[]? lease)
        {
            if (lease is null)
            {
                // we need to create our own copy of the data
                lease = ArrayPool<byte>.Shared.Rent(payload.Length);
                payload.CopyTo(lease);
            }

            var obj = Interlocked.Exchange(ref _spare, null) ?? new();
            obj.Init(lease, payload.Length, message);
            lease = null; // count as claimed

            DebugCounters.OnCopyOut(payload.Length);
#if NETCOREAPP3_0_OR_GREATER
            ThreadPool.UnsafeQueueUserWorkItem(obj, false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(WaitCallback, obj);
#endif
        }
#if !NETCOREAPP3_0_OR_GREATER
        private static readonly WaitCallback WaitCallback = state => ((WorkItem)state!).Execute();
#endif

        public static void Execute(IRespMessage? message, ReadOnlySpan<byte> payload)
        {
            if (message is { IsCompleted: false })
            {
                try
                {
                    var reader = new RespReader(payload);
                    message.ProcessResponse(ref reader);
                }
                catch (Exception ex)
                {
                    message.TrySetException(ex);
                }
            }
        }

        public void Execute()
        {
            var message = _message;
            var payload = _payload;
            var length = _length;
            _message = null;
            _payload = [];
            _length = 0;
            Interlocked.Exchange(ref _spare, this);
            Execute(message, new(payload, 0, length));
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    public static void ProcessResponse(IRespMessage? pending, ReadOnlySpan<byte> payload, ref byte[]? lease)
    {
        if (pending is null)
        {
            // nothing to do
        }
        else if (pending is IRespInternalMessage { AllowInlineParsing: true })
        {
            WorkItem.Execute(pending, payload);
        }
        else
        {
            WorkItem.UnsafeQueueUserWorkItem(pending, payload, ref lease);
        }
    }

    private static readonly Action<object?> CancellationCallback = static state
        => ((IRespMessage)state!).TrySetCanceled();

    public static CancellationTokenRegistration RegisterForCancellation(
        IRespMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cancellationToken.Register(CancellationCallback, message);
    }

    [Conditional("DEBUG")]
    public static void DebugBreak()
    {
#if DEBUG
        if (Debugger.IsAttached) Debugger.Break();
#endif
    }

    [Conditional("DEBUG")]
    public static void DebugBreakIf(bool condition)
    {
#if DEBUG
        if (condition && Debugger.IsAttached) Debugger.Break();
#endif
    }
}

internal abstract class InternalRespMessageBase<TState, TResponse> : IRespInternalMessage
{
    private IRespParser<TState, TResponse>? _parser;
    private byte[] _requestPayload = [];
    private int _requestLength, _requestRefCount = 1;
    private TState _state = default!;
    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationTokenRegistration;

    public abstract bool IsCompleted { get; }

    public bool TryReserveRequest(out ReadOnlyMemory<byte> payload)
    {
        payload = default;
        while (true) // need to take reservation
        {
            if (IsCompleted) return false;
            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, checked(oldCount + 1), oldCount) == oldCount) break;
        }

        payload = new(_requestPayload, 0, _requestLength);
        return true;
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

    protected abstract bool TrySetResult(TResponse value);
    public abstract bool TrySetException(Exception exception);
    public abstract bool TrySetCanceled(CancellationToken cancellationToken = default);

    // ReSharper disable once SuspiciousTypeConversion.Global
    public bool AllowInlineParsing => _parser is null or IRespInlineParser;

    void IRespMessage.ProcessResponse(ref RespReader reader)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (_parser is { } parser)
        {
            if (parser is not IRespMetadataParser)
            {
                reader.MoveNext(); // skip attributes and process errors
            }

            var result = parser.Parse(in _state, ref reader);
            TryReleaseRequest();
            TrySetResult(result);
        }
    }

    public CancellationToken CancellationToken => _cancellationToken;

    protected void UnregisterCancellation()
    {
        var reg = _cancellationTokenRegistration;
        _cancellationTokenRegistration = default;
        reg.Dispose();
    }

    protected void Reset()
    {
        _parser = null;
        _state = default!;
        _requestLength = 0;
        _requestPayload = [];
        _requestRefCount = 0;
        _cancellationToken = CancellationToken.None;
        _cancellationTokenRegistration = default;
    }

    protected void Init(
        byte[] requestPayload,
        int requestLength,
        IRespParser<TState, TResponse>? parser,
        in TState state,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
        }

        _parser = parser;
        _state = state;
        _requestPayload = requestPayload;
        _requestLength = requestLength;
        _requestRefCount = 1;
        _cancellationToken = cancellationToken;
    }
}

internal static class SyncRespMessageStatus // think "enum", but need Volatile.Read friendliness
{
    internal const int
        Pending = 0,
        Completed = 1,
        Faulted = 2,
        Cancelled = 3,
        Timeout = 4;
}

internal sealed class SyncInternalRespMessage<TState, TResponse> : InternalRespMessageBase<TState, TResponse>
{
    private SyncInternalRespMessage() { }

    private int _status;
    private TResponse _result = default!;
    private Exception? _exception;

    protected override bool TrySetResult(TResponse value)
    {
        if (Volatile.Read(ref _status) == SyncRespMessageStatus.Pending)
        {
            lock (this)
            {
                if (_status == SyncRespMessageStatus.Pending)
                {
                    _result = value;
                    _status = SyncRespMessageStatus.Completed;
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override bool TrySetException(Exception exception)
    {
        if (Volatile.Read(ref _status) == SyncRespMessageStatus.Pending)
        {
            var newStatus = exception switch
            {
                TimeoutException => SyncRespMessageStatus.Timeout,
                OperationCanceledException => SyncRespMessageStatus.Cancelled,
                _ => SyncRespMessageStatus.Faulted,
            };
            lock (this)
            {
                if (_status == SyncRespMessageStatus.Pending)
                {
                    _exception = exception;
                    _status = newStatus;
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _status) == SyncRespMessageStatus.Pending)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                // if the inbound token was not cancelled: use our own
                cancellationToken = CancellationToken;
            }

            lock (this)
            {
                if (_status == SyncRespMessageStatus.Pending)
                {
                    _status = SyncRespMessageStatus.Cancelled;
                    _exception = new OperationCanceledException(cancellationToken);
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override bool IsCompleted => Volatile.Read(ref _status) != SyncRespMessageStatus.Pending;

    public TResponse WaitAndRecycle(TimeSpan timeout)
    {
        int status = Volatile.Read(ref _status);
        if (status == SyncRespMessageStatus.Pending)
        {
            lock (this)
            {
                status = _status;
                if (status == SyncRespMessageStatus.Pending)
                {
                    if (timeout == TimeSpan.Zero)
                    {
                        Monitor.Wait(this);
                        status = _status;
                    }
                    else if (!Monitor.Wait(this, timeout))
                    {
                        status = _status = SyncRespMessageStatus.Timeout;
                    }
                    else
                    {
                        status = _status;
                    }
                }
            }
        }

        switch (status)
        {
            case SyncRespMessageStatus.Completed:
                var result = _result; // snapshot
                if (_spare is null && TryReset())
                {
                    _spare = this;
                }

                return result;
            case SyncRespMessageStatus.Faulted:
                throw _exception ?? new InvalidOperationException("Operation failed");
            case SyncRespMessageStatus.Cancelled:
                throw _exception ?? new OperationCanceledException(CancellationToken);
            case SyncRespMessageStatus.Timeout:
                throw _exception ?? new TimeoutException();
            default:
                throw new InvalidOperationException($"Unexpected status: {status}");
        }
    }

    private bool TryReset()
    {
        Reset();
        _exception = null;
        _result = default!;
        _status = SyncRespMessageStatus.Pending;
        return true;
    }

    [ThreadStatic]
    // this comment just to stop a weird formatter glitch
    private static SyncInternalRespMessage<TState, TResponse>? _spare;

    public static SyncInternalRespMessage<TState, TResponse> Create(
        byte[] requestPayload,
        int requestLength,
        IRespParser<TState, TResponse>? parser,
        in TState state,
        CancellationToken cancellationToken)
    {
        var obj = _spare ?? new();
        _spare = null;
        obj.Init(requestPayload, requestLength, parser, in state, cancellationToken);

        return obj;
    }
}

#if NET9_0_OR_GREATER && NEVER
internal sealed class AsyncInternalRespMessage<TResponse>(
    byte[] requestPayload,
    int requestLength,
    IRespParser<TResponse>? parser)
    : InternalRespMessageBase<TResponse>(requestPayload, requestLength, parser)
{
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern Task<TResponse> CreateTask(object? state, TaskCreationOptions options);

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern bool TrySetException(Task<TResponse> obj, Exception exception);

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern bool TrySetResult(Task<TResponse> obj, TResponse value);

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern bool TrySetCanceled(Task<TResponse> obj, CancellationToken cancellationToken);

    // ReSharper disable once SuspiciousTypeConversion.Global
    private readonly Task<TResponse> _task = CreateTask(
        null,
        // if we're using IO-thread parsing, we *must* still dispatch downstream continuations to the thread-pool to
        // prevent thread-theft; otherwise, we're fine to run downstream inline (we already jumped)
        parser is IRespInlineParser ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

    private CancellationTokenRegistration _cancellationTokenRegistration;

    public override bool IsCompleted => _task.IsCompleted;
    protected override bool TrySetResult(TResponse value)
    {
        UnregisterCancellation();
        return TrySetResult(_task, value);
    }

    public override bool TrySetException(Exception exception)
    {
        UnregisterCancellation();
        return TrySetException(_task, exception);
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken)
    {
        UnregisterCancellation();
        return TrySetCanceled(_task, cancellationToken);
    }

    private void UnregisterCancellation()
    {
        _cancellationTokenRegistration.Dispose();
        _cancellationTokenRegistration = default;
    }

    public Task<TResponse> WaitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationTokenRegistration = ActivationHelper.RegisterForCancellation(this, cancellationToken);
        }

        return _task;
    }
}
#else
internal sealed class AsyncInternalRespMessage<TState, TResponse> : InternalRespMessageBase<TState, TResponse>,
    IValueTaskSource<TResponse>, IValueTaskSource
{
    [ThreadStatic]
    // this comment just to stop a weird formatter glitch
    private static AsyncInternalRespMessage<TState, TResponse>? _spare;

    // we need synchronization over multiple attempts (completion, cancellation, abort) trying
    // to signal the MRTCS
    private int _completedFlag;

    private bool SetCompleted(bool withSuccess = false)
    {
        if (Interlocked.CompareExchange(ref _completedFlag, 1, 0) == 0)
        {
            // stop listening for CT notifications
            UnregisterCancellation();

            // configure threading model; failure can be triggered from any thread - *always*
            // dispatch to pool; in the success case, we're either on the IO thread
            // (if inline-parsing is enabled) - in which case, yes: dispatch - or we've
            // already jumped to a pool thread for the parse step. So: the only
            // time we want to complete inline is success and not inline-parsing.
            _asyncCore.RunContinuationsAsynchronously = !withSuccess || AllowInlineParsing;

            return true;
        }

        return false;
    }

    public static AsyncInternalRespMessage<TState, TResponse> Create(
        byte[] requestPayload,
        int requestLength,
        IRespParser<TState, TResponse>? parser,
        in TState state,
        CancellationToken cancellationToken)
    {
        var obj = _spare ?? new();
        _spare = null;
        obj._asyncCore.RunContinuationsAsynchronously = true;
        obj.Init(requestPayload, requestLength, parser, in state, cancellationToken);
        return obj;
    }

    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;

    public override bool IsCompleted => Volatile.Read(ref _completedFlag) == 1;

    protected override bool TrySetResult(TResponse value)
    {
        if (SetCompleted(withSuccess: true))
        {
            _asyncCore.SetResult(value);
            return true;
        }

        return false;
    }

    public override bool TrySetException(Exception exception)
    {
        if (SetCompleted())
        {
            _asyncCore.SetException(exception);
            return true;
        }

        return false;
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        if (SetCompleted())
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                // if the inbound token was not cancelled: use our own
                cancellationToken = CancellationToken;
            }

            _asyncCore.SetException(new OperationCanceledException(cancellationToken));
            return true;
        }

        return false;
    }

    public ValueTask<TResponse> WaitTypedAsync() => new(this, _asyncCore.Version);

    internal ValueTask<TResponse> WaitTypedAsync(Task send)
    {
        if (!send.IsCompleted) return Awaited(send, this);
        send.GetAwaiter().GetResult();
        return new(this, _asyncCore.Version);

        static async ValueTask<TResponse> Awaited(Task task, AsyncInternalRespMessage<TState, TResponse> @this)
        {
            await task.ConfigureAwait(false);
            return await @this.WaitTypedAsync().ConfigureAwait(false);
        }
    }

    public ValueTask WaitUntypedAsync() => new(this, _asyncCore.Version);

    internal ValueTask WaitUntypedAsync(Task send)
    {
        if (!send.IsCompleted) return Awaited(send, this);
        send.GetAwaiter().GetResult();
        return new(this, _asyncCore.Version);

        static async ValueTask Awaited(Task task, AsyncInternalRespMessage<TState, TResponse> @this)
        {
            await task.ConfigureAwait(false);
            await @this.WaitUntypedAsync().ConfigureAwait(false);
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _asyncCore.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _asyncCore.OnCompleted(continuation, state, token, flags);

    public TResponse GetResult(short token)
    {
        Debug.Assert(IsCompleted, "Async payload should already be completed");
        var result = _asyncCore.GetResult(token);
        // recycle on success (only)
        if (_spare is null && TryReset())
        {
            _spare = this;
        }

        return result;
    }

    private bool TryReset()
    {
        Reset();
        _asyncCore.Reset(); // incr version, etc
        _completedFlag = 0;
        return true;
    }

    void IValueTaskSource.GetResult(short token) => _ = GetResult(token);
}
#endif
