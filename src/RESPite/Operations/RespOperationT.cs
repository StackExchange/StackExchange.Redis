using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace RESPite.Operations;

/// <summary>
/// A handle to an operation whose reply parses into <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
/// <remarks>
/// The layout is deliberately identical to <see cref="RespOperation"/>, so dropping the type is a
/// reinterpret rather than a copy. That is what lets a batch hold one array of untyped operations while
/// each element still completes its own caller with the right type.
/// </remarks>
internal readonly struct RespOperation<T> : ICriticalNotifyCompletion
{
    // the layout must stay identical to RespOperation; see the type remarks
    private readonly RespMessageBase<T> _message;
    private readonly short _token;
    private readonly bool _disableCaptureContext;

    internal RespOperation(RespMessageBase<T> message, bool disableCaptureContext = false)
    {
        _message = message;
        _token = message.Token;
        _disableCaptureContext = disableCaptureContext;
    }

    /// <summary>Re-flag an existing handle, keeping the token it already holds.</summary>
    /// <remarks>See the note on the untyped equivalent: re-reading the token here would be a bug.</remarks>
    private RespOperation(RespMessageBase<T> message, short token, bool disableCaptureContext)
    {
        _message = message;
        _token = token;
        _disableCaptureContext = disableCaptureContext;
    }

    private RespMessageBase<T> Message
        => _message ?? (RespMessageBase<T>)RespOperation.ThrowNoMessage();

    internal short Token => _token;

    /// <summary>Drop the result type, yielding the handle the pipeline deals in.</summary>
    /// <param name="operation">The operation.</param>
    public static implicit operator RespOperation(in RespOperation<T> operation)
        => Unsafe.As<RespOperation<T>, RespOperation>(ref Unsafe.AsRef(in operation));

    /// <summary>Treat this operation as a <see cref="ValueTask{TResult}"/>.</summary>
    /// <param name="operation">The operation.</param>
    public static implicit operator ValueTask<T>(in RespOperation<T> operation)
        => new(operation.Message, operation._token);

    /// <inheritdoc cref="ValueTask{TResult}.AsTask()"/>
    public Task<T> AsTask()
    {
        ValueTask<T> pending = this;
        return pending.AsTask();
    }

    /// <inheritdoc cref="Task.Wait(TimeSpan)"/>
    /// <param name="timeout">How long to wait; <see cref="TimeSpan.Zero"/> waits indefinitely.</param>
    public T Wait(TimeSpan timeout = default) => Message.Wait(_token, timeout);

    /// <inheritdoc cref="ValueTask{TResult}.IsCompleted"/>
    public bool IsCompleted => Message.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    /// <inheritdoc cref="ValueTask{TResult}.IsCompletedSuccessfully"/>
    public bool IsCompletedSuccessfully => Message.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;

    /// <inheritdoc cref="ValueTask{TResult}.IsFaulted"/>
    public bool IsFaulted => Message.GetStatus(_token) == ValueTaskSourceStatus.Faulted;

    /// <inheritdoc cref="ValueTask{TResult}.IsCanceled"/>
    public bool IsCanceled => Message.GetStatus(_token) == ValueTaskSourceStatus.Canceled;

    /// <inheritdoc cref="ValueTask{TResult}.GetAwaiter()"/>
    public RespOperation<T> GetAwaiter() => this;

    /// <inheritdoc cref="ValueTask{TResult}.ConfigureAwait(bool)"/>
    /// <param name="continueOnCapturedContext">Whether to marshal the continuation back.</param>
    public RespOperation<T> ConfigureAwait(bool continueOnCapturedContext)
        => new(Message, _token, !continueOnCapturedContext);

    /// <inheritdoc cref="ValueTaskAwaiter{TResult}.GetResult"/>
    public T GetResult() => Message.GetResult(_token);

    /// <inheritdoc cref="INotifyCompletion.OnCompleted(Action)"/>
    public void OnCompleted(Action continuation)
    {
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
            : ValueTaskSourceOnCompletedFlags.FlowExecutionContext | ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(RespOperation.InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ICriticalNotifyCompletion.UnsafeOnCompleted(Action)"/>
    public void UnsafeOnCompleted(Action continuation)
    {
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.None
            : ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(RespOperation.InvokeState, continuation, _token, flags);
    }
}
