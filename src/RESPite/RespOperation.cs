using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using RESPite.Internal;
using RESPite.Messages;

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
#if DEBUG
    [ThreadStatic]
    // how many resp-operations have we chewed through?
    private static int _debugPerThreadMessageAllocations;

    internal static int DebugPerThreadMessageAllocations => _debugPerThreadMessageAllocations;
#else
    internal static int DebugPerThreadMessageAllocations => 0;
#endif

    [Conditional("DEBUG")]
    internal static void DebugOnAllocateMessage()
    {
#if DEBUG
        _debugPerThreadMessageAllocations++;
#endif
    }

    // it is important that this layout remains identical between RespOperation and RespOperation<T>
    private readonly RespMessageBase _message;
    private readonly short _token;
    private readonly bool _disableCaptureContext; // default is false, so: bypass

    internal RespOperation(RespMessageBase message, bool disableCaptureContext = false)
    {
        _message = message;
        _token = message.Token;
        _disableCaptureContext = disableCaptureContext;
    }

    public bool IsSent => Message.IsSent(_token);
    internal RespMessageBase Message => _message ?? ThrowNoMessage();

    internal static RespMessageBase ThrowNoMessage()
        => throw new InvalidOperationException($"{nameof(RespOperation)} is not correctly initialized");

    /// <summary>
    /// Treats this operation as a <see cref="ValueTask"/>.
    /// </summary>
    public static implicit operator ValueTask(in RespOperation operation)
        => new(operation.Message, operation._token);

    /// <inheritdoc cref="ValueTask.AsTask()"/>
    public Task AsTask() => new ValueTask(Message, _token).AsTask();

    public ValueTask AsValueTask() => new(Message, _token);

    /// <inheritdoc cref="Task.Wait(TimeSpan)"/>
    public void Wait(TimeSpan timeout = default)
        => Message.WaitVoid(_token, timeout);

    /// <inheritdoc cref="ValueTask.IsCompleted"/>
    public bool IsCompleted => Message.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    /// <inheritdoc cref="ValueTask.IsCompletedSuccessfully"/>
    public bool IsCompletedSuccessfully => Message.GetStatus(_token) == ValueTaskSourceStatus.Succeeded;

    /// <inheritdoc cref="ValueTask.IsFaulted"/>
    public bool IsFaulted => Message.GetStatus(_token) == ValueTaskSourceStatus.Faulted;

    /// <inheritdoc cref="ValueTask.IsCanceled"/>
    public bool IsCanceled => Message.GetStatus(_token) == ValueTaskSourceStatus.Canceled;

    public ref readonly CancellationToken CancellationToken => ref Message.CancellationToken;

    internal short Token => _token;
    internal int MessageCount => Message.MessageCount;
    internal bool TrySetException(Exception exception) => Message.TrySetException(_token, exception);
    internal bool TrySetCancelled(CancellationToken cancellationToken = default) => Message.TrySetCanceled(_token, cancellationToken);
    internal bool TryReserveRequest(out ReadOnlySequence<byte> payload, bool recordSent = true) => Message.TryReserveRequest(_token, out payload, recordSent);
    internal void ReleaseRequest() => Message.ReleaseRequest();

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
        Message.OnCompletedWithNotSentDetection(InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ICriticalNotifyCompletion.UnsafeOnCompleted(Action)"/>
    public void UnsafeOnCompleted(Action continuation)
    {
        // UseSchedulingContext === continueOnCapturedContext
        var flags = _disableCaptureContext
            ? ValueTaskSourceOnCompletedFlags.None
            : ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        Message.OnCompletedWithNotSentDetection(InvokeState, continuation, _token, flags);
    }

    /// <inheritdoc cref="ValueTaskAwaiter.GetResult"/>
    public void GetResult() => Message.GetResultVoid(_token);

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
        private readonly RespMessageBase _message;
        private readonly short _token;

        internal Remote(RespMessageBase message)
        {
            _message = message;
            _token = message.Token;
        }

        public bool IsTokenMatch => _token == _message.Token;

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

    /// <summary>
    /// Create a disconnected <see cref="RespOperation"/> without a RESP parser; this is only intended for testing purposes.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public static RespOperation Create(
        out Remote remote,
        bool sent = true,
        CancellationToken cancellationToken = default)
    {
        var msg = RespMessage<bool>.Get(null);
        msg.Init(sent, cancellationToken);
        remote = new(msg);
        return new RespOperation(msg);
    }

    /// <summary>
    /// Create a disconnected <see cref="RespOperation"/> with a stateless RESP parser; this is only intended for testing purposes.
    /// </summary>
    /// <typeparam name="TResult">The result of the operation.</typeparam>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public static RespOperation<TResult> Create<TResult>(
        IRespParser<TResult>? parser,
        out Remote remote,
        bool sent = true,
        CancellationToken cancellationToken = default)
    {
        var msg = RespMessage<TResult>.Get(parser);
        msg.Init(sent, cancellationToken);
        remote = new(msg);
        return new RespOperation<TResult>(msg);
    }

    /// <summary>
    /// Create a disconnected <see cref="RespOperation"/> with a stateful RESP parser; this is only intended for testing purposes.
    /// </summary>
    /// <typeparam name="TState">The state used by the parser.</typeparam>
    /// <typeparam name="TResult">The result of the operation.</typeparam>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public static RespOperation<TResult> Create<TState, TResult>(
        in TState state,
        IRespParser<TState, TResult>? parser,
        out Remote remote,
        bool sent = true,
        CancellationToken cancellationToken = default)
    {
        var msg = RespMessage<TState, TResult>.Get(in state, parser);
        msg.Init(sent, cancellationToken);
        remote = new(msg);
        return new RespOperation<TResult>(msg);
    }
}
