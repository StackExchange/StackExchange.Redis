using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using RESPite.Internal;

namespace RESPite;

/// <summary>
/// Represents a RESP operation that does not return a value (other than to signal completion).
/// This works almost identically to <see cref="ValueTask"/> when based on
/// <see cref="IValueTaskSource"/>, and the usage semantics are the same. In particular,
/// note that a value can only be consumed once. Unlike <see cref="ValueTask"/>, the
/// value can be awaited synchronously if required.
/// </summary>
public readonly struct RespOperation : ICriticalNotifyCompletion
{
    // it is important that this layout remains identical between RespOperation and RespOperation<T>
    private readonly IRespMessage _message;
    private readonly short _token;
    private readonly bool _disableCaptureContext; // default is false, so: bypass

    internal RespOperation(IRespMessage message, bool disableCaptureContext = false)
    {
        _message = message;
        _token = message.Token;
        _disableCaptureContext = disableCaptureContext;
    }

    internal IRespMessage Message => _message ?? ThrowNoMessage();

    internal static IRespMessage ThrowNoMessage()
        => throw new InvalidOperationException($"{nameof(RespOperation)} is not correctly initialized");

    /// <summary>
    /// Treats this operation as a <see cref="ValueTask"/>.
    /// </summary>
    public static implicit operator ValueTask(in RespOperation operation)
        => new(operation.Message, operation._token);

    /// <inheritdoc cref="ValueTask.AsTask()"/>
    public Task AsTask()
    {
        ValueTask vt = this;
        return vt.AsTask();
    }

    /// <inheritdoc cref="Task.Wait(TimeSpan)"/>
    public void Wait(TimeSpan timeout = default)
        => Message.Wait(_token, timeout);

    /// <inheritdoc cref="ValueTask.IsCompleted"/>
    public bool IsCompleted => Message.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    /// <inheritdoc cref="ValueTask.IsCompletedSuccessfully"/>
    public bool IsCompletedSuccessfully => Message.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;

    /// <inheritdoc cref="ValueTask.IsFaulted"/>
    public bool IsFaulted => Message.GetStatus(_token) == ValueTaskSourceStatus.Faulted;

    /// <inheritdoc cref="ValueTask.IsCanceled"/>
    public bool IsCanceled => Message.GetStatus(_token) == ValueTaskSourceStatus.Canceled;

    internal short Token => _token;

    internal static readonly Action<object?> InvokeState = static state => ((Action)state!).Invoke();

    /// <inheritdoc cref="ValueTaskAwaiter.OnCompleted(Action)"/>
    /// <see cref="INotifyCompletion.OnCompleted(Action)"/>
    public void OnCompleted(Action continuation)
    {
        // UseSchedulingContext === continueOnCapturedContext, always add FlowExecutionContext
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
            : ValueTaskSourceOnCompletedFlags.FlowExecutionContext |
              ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ICriticalNotifyCompletion.UnsafeOnCompleted(Action)"/>
    public void UnsafeOnCompleted(Action continuation)
    {
        // UseSchedulingContext === continueOnCapturedContext
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.None
            : ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ValueTaskAwaiter.GetResult"/>
    public void GetResult() => Message.GetResult(_token);

    /// <inheritdoc cref="ValueTask.GetAwaiter()"/>
    public RespOperation GetAwaiter() => this;

    /// <inheritdoc cref="ValueTask.ConfigureAwait(bool)"/>
    public RespOperation ConfigureAwait(bool continueOnCapturedContext)
    {
        var clone = this;
        Unsafe.AsRef(in clone._disableCaptureContext) = !continueOnCapturedContext;
        return clone;
    }

    /// <summary>
    /// Provides a mechanism to control the outcome of a <see cref="RespOperation"/>; this is mostly
    /// intended for testing purposes. It is broadly comparable to <see cref="TaskCompletionSource{TResult}"/>.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct Remote
    {
        private readonly IRespMessage _message;
        private readonly short _token;
        internal Remote(IRespMessage message)
        {
            _message = message;
            _token = message.Token;
        }

        /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetCanceled(CancellationToken)"/>
        public bool TrySetCanceled(CancellationToken cancellationToken = default)
            => _message.TrySetCanceled(_token);

        /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetException(Exception)"/>
        public bool TrySetException(Exception exception)
            => _message.TrySetException(_token, exception);

        /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetResult(TResult)"/>
        /// <remarks>The parser provided during creation is used to process the result.</remarks>
        public bool TrySetResult(scoped ReadOnlySpan<byte> response)
            => _message.TrySetResult(_token, response);

        /// <inheritdoc cref="TaskCompletionSource{TResult}.TrySetResult(TResult)"/>
        /// <remarks>The parser provided during creation is used to process the result.</remarks>
        public bool TrySetResult(in ReadOnlySequence<byte> response)
            => _message.TrySetResult(_token, response);
    }
}
