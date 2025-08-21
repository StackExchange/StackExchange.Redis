using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
    ReadOnlyMemory<byte> ReserveRequest();

    /// <summary>
    /// Releases the request payload.
    /// </summary>
    void ReleaseRequest();
    bool TrySetCanceled(CancellationToken cancellationToken);
    bool TrySetException(Exception exception);

    /// <summary>
    /// Capture the response payload. This operation occurs on the IO thread, so should be as fast as possible;
    /// it should not usually process the response.
    /// </summary>
    void SetResponse(RespPrefix prefix, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Parse the response and complete the request.
    /// </summary>
    void ProcessResponse();
}

internal static class ActivationHelper
{
    private static readonly WaitCallback ExecuteCallback = state => ((IRespMessage?)state)?.ProcessResponse();

    public static void UnsafeQueueUserWorkItem(IRespMessage message)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (message is IThreadPoolWorkItem tpwi)
        {
            ThreadPool.UnsafeQueueUserWorkItem(tpwi, false);
            return;
        }
#endif
        ThreadPool.UnsafeQueueUserWorkItem(ExecuteCallback, message);
    }

    private static readonly Action<object?> CancellationCallback =
        static state => ((IRespMessage)state!).TrySetCanceled(CancellationToken.None);

    public static CancellationTokenRegistration RegisterForCancellation(
        IRespMessage message,
        CancellationToken cancellationToken)
        => cancellationToken.Register(CancellationCallback, message);
}

internal abstract class InternalRespMessageBase<TResponse> : IRespMessage
#if NETCOREAPP3_0_OR_GREATER
#pragma warning disable SA1001
    , IThreadPoolWorkItem
#pragma warning restore SA1001
#endif
{
    private IRespParser<TResponse>? _parser;
    private byte[] _requestPayload, _responsePayload;
    private int _requestLength, _responseLength;
    private int _requestRefCount = 1;

    /// <summary>
    /// Create a new instance using the supplied payload.
    /// </summary>
    internal InternalRespMessageBase(byte[] requestPayload, int requestLength, IRespParser<TResponse>? parser)
    {
        _parser = parser;
        _requestPayload = requestPayload;
        _requestLength = requestLength;
        _responsePayload = [];
        _responseLength = 0;
    }

    public abstract bool IsCompleted { get; }

    public ReadOnlyMemory<byte> ReserveRequest()
    {
        while (true) // need to take reservation
        {
            if (IsCompleted) ThrowComplete();
            var oldCount = Volatile.Read(ref _requestRefCount);
            if (oldCount == 0) ThrowReleased();
            if (Interlocked.CompareExchange(ref _requestRefCount, checked(oldCount + 1), oldCount) == oldCount) break;
        }
        return new(_requestPayload, 0, _requestLength);

        static void ThrowComplete() => throw new InvalidOperationException("The request has already completed");
        static void ThrowReleased() => throw new InvalidOperationException("The request payload has already been released");
    }

    public void ReleaseRequest()
    {
        if (!TryReleaseRequest()) ThrowReleased();
        static void ThrowReleased() => throw new InvalidOperationException("The request payload has already been released");
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
    public abstract bool TrySetCanceled(CancellationToken cancellationToken);

#if NETCOREAPP3_0_OR_GREATER
    void IThreadPoolWorkItem.Execute() => ParseResponse();
#endif

    void IRespMessage.ProcessResponse() => ParseResponse();

    private void ParseResponse(ReadOnlySpan<byte> payload = default)
    {
        try
        {
            if (!IsCompleted && _parser is { } parser)
            {
                if (payload.IsEmpty)
                {
                    payload = new(_responsePayload, 0, _responseLength);
                }

                var reader = new RespReader(payload);
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (parser is not IRespMetadataParser)
                {
                    reader.MoveNext(); // skip attributes and process errors
                }

                var result = parser.Parse(ref reader);
                TryReleaseRequest();
                TrySetResult(result);
            }
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
        finally
        {
            var arr = _responsePayload;
            _responseLength = 0;
            _responsePayload = [];
            ArrayPool<byte>.Shared.Return(arr);
        }
    }

    void IRespMessage.SetResponse(RespPrefix prefix, ReadOnlySpan<byte> payload)
    {
        if (!IsCompleted && _parser is { } parser)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (parser is IRespInlineParser) // complete on IO thread
            {
                ParseResponse(payload);
            }
            else
            {
                var tmp = ArrayPool<byte>.Shared.Rent(payload.Length);
                payload.CopyTo(tmp);
                _responsePayload = tmp;
                _responseLength = payload.Length;
                ActivationHelper.UnsafeQueueUserWorkItem(this);
            }
        }
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

internal sealed class SyncInternalRespMessage<TResponse>(
    byte[] requestPayload,
    int requestLength,
    IRespParser<TResponse>? parser)
    : InternalRespMessageBase<TResponse>(requestPayload, requestLength, parser)
{
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
            lock (this)
            {
                if (_status == SyncRespMessageStatus.Pending)
                {
                    _exception = exception;
                    _status = SyncRespMessageStatus.Faulted;
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _status) == SyncRespMessageStatus.Pending)
        {
            lock (this)
            {
                if (_status == SyncRespMessageStatus.Pending)
                {
                    _status = SyncRespMessageStatus.Cancelled;
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override bool IsCompleted => Volatile.Read(ref _status) != SyncRespMessageStatus.Pending;

    public TResponse Wait(TimeSpan timeout)
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
                return _result;
            case SyncRespMessageStatus.Faulted:
                throw _exception ?? new InvalidOperationException("Operation failed");
            case SyncRespMessageStatus.Cancelled:
                throw new OperationCanceledException();
            case SyncRespMessageStatus.Timeout:
                throw new TimeoutException();
            default:
                throw new InvalidOperationException($"Unexpected status: {status}");
        }
    }
}

internal sealed class AsyncInternalRespMessage<TResponse>(
    byte[] requestPayload,
    int requestLength,
    IRespParser<TResponse>? parser)
    : InternalRespMessageBase<TResponse>(requestPayload, requestLength, parser)
{
    // ReSharper disable once SuspiciousTypeConversion.Global
    private readonly TaskCompletionSource<TResponse> _tcs = new(
        // if we're using IO-thread parsing, we *must* still dispatch downstream continuations to the thread-pool to
        // prevent thread-theft; otherwise, we're fine to run downstream inline (we already jumped)
        parser is IRespInlineParser ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

    private CancellationTokenRegistration _cancellationTokenRegistration;

    public override bool IsCompleted => _tcs.Task.IsCompleted;
    protected override bool TrySetResult(TResponse value)
    {
        UnregisterCancellation();
        return _tcs.TrySetResult(value);
    }

    public override bool TrySetException(Exception exception)
    {
        UnregisterCancellation();
        return _tcs.TrySetException(exception);
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken)
    {
        UnregisterCancellation();
        return _tcs.TrySetCanceled(cancellationToken);
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

        return _tcs.Task;
    }
}
