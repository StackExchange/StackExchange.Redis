using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using RESPite.Internal;

namespace RESPite;

/// <summary>
/// Represents a RESP operation that returns a value of type <typeparamref name="T"/>.
/// This works almost identically to <see cref="ValueTask{T}"/> when based on
/// <see cref="IValueTaskSource{T}"/>, and the usage semantics are the same. In particular,
/// note that a value can only be consumed once. Unlike <see cref="ValueTask{T}"/>, the
/// value can be awaited synchronously if required.
/// </summary>
/// <typeparam name="T">The type of value returned by the operation.</typeparam>
public readonly struct RespOperation<T>
{
    // it is important that this layout remains identical between RespOperation and RespOperation<T>
    private readonly RespMessageBase<T> _message;
    private readonly short _token;
    private readonly bool _disableCaptureContext;

    internal RespOperation(RespMessageBase<T> message, short token, bool disableCaptureContext)
    {
        _message = message;
        _token = token;
        _disableCaptureContext = disableCaptureContext;
    }
    internal RespOperation(RespMessageBase<T> message, bool disableCaptureContext = false)
    {
        _message = message;
        _token = message.Token;
        _disableCaptureContext = disableCaptureContext;
    }

    public CancellationToken CancellationToken => Message.CancellationToken;

    private RespMessageBase<T> Message => _message ?? (RespMessageBase<T>)RespOperation.ThrowNoMessage();

    /// <summary>
    /// Treats this operation as an untyped <see cref="RespOperation"/>.
    /// </summary>
    #if PREVIEW_LANGVER
    [Obsolete($"When possible, prefer .Untyped")]
    #endif
    public static implicit operator RespOperation(in RespOperation<T> operation)
        => Unsafe.As<RespOperation<T>, RespOperation>(ref Unsafe.AsRef(in operation));

    /// <summary>
    /// Treats this operation as an untyped <see cref="ValueTask{T}"/>.
    /// </summary>
    public static implicit operator ValueTask<T>(in RespOperation<T> operation)
        => new(operation.Message, operation._token);

    /// <summary>
    /// Treats this operation as a <see cref="ValueTask"/>.
    /// </summary>
    public static implicit operator ValueTask(in RespOperation<T> operation)
        => new(operation.Message, operation._token);

    /// <inheritdoc cref="ValueTask.AsTask()"/>
    public Task<T> AsTask() => new ValueTask<T>(Message, _token).AsTask();

    public ValueTask<T> AsValueTask() => new(Message, _token);

    /// <inheritdoc cref="Task.Wait(TimeSpan)"/>
    public T Wait(TimeSpan timeout = default)
        => Message.Wait(_token, timeout);

    /// <inheritdoc cref="ValueTask.IsCompleted"/>
    public bool IsCompleted => Message.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    /// <inheritdoc cref="ValueTask.IsCompletedSuccessfully"/>
    public bool IsCompletedSuccessfully => Message.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;

    /// <inheritdoc cref="ValueTask.IsFaulted"/>
    public bool IsFaulted => Message.GetStatus(_token) == ValueTaskSourceStatus.Faulted;

    /// <inheritdoc cref="ValueTask.IsCanceled"/>
    public bool IsCanceled => Message.GetStatus(_token) == ValueTaskSourceStatus.Canceled;

    /// <inheritdoc cref="ValueTaskAwaiter.OnCompleted(Action)"/>
    /// <see cref="INotifyCompletion.OnCompleted(Action)"/>
    public void OnCompleted(Action continuation)
    {
        // UseSchedulingContext === continueOnCapturedContext, always add FlowExecutionContext
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
            : ValueTaskSourceOnCompletedFlags.FlowExecutionContext |
              ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompletedWithNotSentDetection(RespOperation.InvokeState, continuation, _token, flags);
    }

    public bool IsSent => Message.IsSent(_token);

    /// <inheritdoc cref="ICriticalNotifyCompletion.UnsafeOnCompleted(Action)"/>
    public void UnsafeOnCompleted(Action continuation)
    {
        // UseSchedulingContext === continueOnCapturedContext
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.None
            : ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompletedWithNotSentDetection(RespOperation.InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ValueTaskAwaiter.GetResult"/>
    public T GetResult() => Message.GetResult(_token);

    /// <inheritdoc cref="ValueTask.GetAwaiter()"/>
    public RespOperation<T> GetAwaiter() => this;

    /// <inheritdoc cref="ValueTask.ConfigureAwait(bool)"/>
    public RespOperation<T> ConfigureAwait(bool continueOnCapturedContext)
    {
        var clone = this;
        Unsafe.AsRef(in clone._disableCaptureContext) = !continueOnCapturedContext;
        return clone;
    }
}
