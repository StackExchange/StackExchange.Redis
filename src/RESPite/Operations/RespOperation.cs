using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace RESPite.Operations;

/// <summary>
/// A handle to an operation whose reply carries no value.
/// </summary>
/// <remarks>
/// <para>
/// Behaves as <see cref="ValueTask"/> does over an <see cref="IValueTaskSource"/>, with the same rule:
/// the result may be consumed <b>once</b>. Unlike <see cref="ValueTask"/> it can also be waited
/// synchronously, which is what lets the sync and async paths share one mechanism.
/// </para>
/// <para>
/// <b>This is the answer to "how does element 3 of 5 get its result out?"</b> - the question the batch
/// work kept failing to answer with out-spans and parallel arrays. Element 3 <i>is</i> an operation, and
/// its message is its own completion, so there is no return channel to design.
/// </para>
/// </remarks>
internal readonly struct RespOperation : ICriticalNotifyCompletion
{
    // the layout must stay identical to RespOperation<T>; the conversion between them is a reinterpret
    private readonly IRespMessage _message;
    private readonly short _token;
    private readonly bool _disableCaptureContext; // default false, i.e. capture, matching await's default

    internal RespOperation(IRespMessage message, bool disableCaptureContext = false)
    {
        _message = message;
        _token = message.Token;
        _disableCaptureContext = disableCaptureContext;
    }

    /// <summary>Re-flag an existing handle, keeping the token it already holds.</summary>
    /// <remarks>
    /// Re-reading <c>message.Token</c> here would be a bug rather than a tidy-up: if the operation has
    /// completed and been recycled since this handle was taken, the fresh token would silently bind the
    /// handle to somebody else's command instead of failing.
    /// </remarks>
    private RespOperation(IRespMessage message, short token, bool disableCaptureContext)
    {
        _message = message;
        _token = token;
        _disableCaptureContext = disableCaptureContext;
    }

    internal IRespMessage Message => _message ?? ThrowNoMessage();

    internal short Token => _token;

    internal static IRespMessage ThrowNoMessage()
        => throw new InvalidOperationException($"{nameof(RespOperation)} was not correctly initialized.");

    /// <summary>Treat this operation as a <see cref="ValueTask"/>.</summary>
    /// <param name="operation">The operation.</param>
    public static implicit operator ValueTask(in RespOperation operation)
        => new(operation.Message, operation._token);

    /// <inheritdoc cref="ValueTask.AsTask()"/>
    public Task AsTask()
    {
        ValueTask pending = this;
        return pending.AsTask();
    }

    /// <inheritdoc cref="Task.Wait(TimeSpan)"/>
    /// <param name="timeout">How long to wait; <see cref="TimeSpan.Zero"/> waits indefinitely.</param>
    public void Wait(TimeSpan timeout = default) => Message.Wait(_token, timeout);

    /// <inheritdoc cref="ValueTask.IsCompleted"/>
    public bool IsCompleted => Message.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    /// <inheritdoc cref="ValueTask.IsCompletedSuccessfully"/>
    public bool IsCompletedSuccessfully => Message.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;

    /// <inheritdoc cref="ValueTask.IsFaulted"/>
    public bool IsFaulted => Message.GetStatus(_token) == ValueTaskSourceStatus.Faulted;

    /// <inheritdoc cref="ValueTask.IsCanceled"/>
    public bool IsCanceled => Message.GetStatus(_token) == ValueTaskSourceStatus.Canceled;

    /// <inheritdoc cref="ValueTask.GetAwaiter()"/>
    public RespOperation GetAwaiter() => this;

    /// <inheritdoc cref="ValueTask.ConfigureAwait(bool)"/>
    /// <param name="continueOnCapturedContext">Whether to marshal the continuation back.</param>
    public RespOperation ConfigureAwait(bool continueOnCapturedContext)
        => new(Message, _token, !continueOnCapturedContext);

    /// <inheritdoc cref="ValueTaskAwaiter.GetResult"/>
    public void GetResult() => Message.GetResult(_token);

    /// <inheritdoc cref="INotifyCompletion.OnCompleted(Action)"/>
    public void OnCompleted(Action continuation)
    {
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
            : ValueTaskSourceOnCompletedFlags.FlowExecutionContext | ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ICriticalNotifyCompletion.UnsafeOnCompleted(Action)"/>
    public void UnsafeOnCompleted(Action continuation)
    {
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.None
            : ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompleted(InvokeState, continuation, _token, flags);
    }

    internal static readonly Action<object?> InvokeState = static state => ((Action)state!).Invoke();
}
