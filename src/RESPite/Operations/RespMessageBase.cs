using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Operations;

/// <summary>
/// One object that is the request, the completion, and the awaitable.
/// </summary>
/// <typeparam name="TResponse">What the reply parses into.</typeparam>
/// <remarks>
/// <para>
/// This replaces <c>Message</c> <b>and</b> <c>IResultBox</c>. Today an awaited command allocates a
/// message, a result box, and a <c>Task</c>; here it allocates one instance which is an
/// <see cref="IValueTaskSource{TResult}"/> in its own right, so the caller's <c>ValueTask&lt;T&gt;</c>
/// wraps it directly. The synchronous path uses the same core rather than a second mechanism, which is
/// what retires <c>SimpleResultBox</c> and its <c>Monitor.PulseAll</c> box.
/// </para>
/// <para>
/// <b>Version and flags share one word, and that is load-bearing.</b> Claiming the outcome has to check
/// "nobody else has completed this" and "the caller is not holding a handle to a previous life" as a
/// single atomic step. Two fields cannot do that: a stale completer can read a matching version, be
/// pre-empted while the instance completes and is recycled, and then win the flag on somebody else's
/// command. Packing the version into the same <see cref="int"/> makes the compare-and-swap cover both.
/// </para>
/// <para>
/// <b>Recycling is not the same as completing.</b> A definite outcome - a reply parsed, a server error,
/// a cancellation observed - proves the pipeline is finished with the instance. A timeout or a
/// connection fault does not: the write may still be in flight, and handing that instance to a pool is
/// how a reply lands on somebody else's command. Only the definite cases set <see cref="IsRecyclable"/>.
/// </para>
/// </remarks>
internal abstract class RespMessageBase<TResponse> : IRespMessage, IValueTaskSource<TResponse>
{
    private ManualResetValueTaskSourceCore<TResponse> _asyncCore;

    /// <summary>Version in the high half, flags in the low half; see the type remarks.</summary>
    private int _state;

    /// <summary>
    /// The parse-capability flags, which describe the <i>type</i> rather than the life.
    /// </summary>
    /// <remarks>
    /// Held separately because <see cref="Reset"/> clears the flag word, and these have to survive it: a
    /// recycled instance that forgot it had a parser would silently return <c>default</c> for every
    /// subsequent command.
    /// </remarks>
    private readonly int _parseFlags;

    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationRegistration;

    /// <summary>
    /// What this operation carries for the sake of explaining itself; see §4 of the design notes.
    /// </summary>
    /// <remarks>
    /// One struct rather than seven fields so that <see cref="Reset"/> clears it in a line the compiler
    /// keeps complete. A recycled operation leaking a previous life's timestamps does not fail anything -
    /// it produces a timeout report describing the wrong command.
    /// </remarks>
    private RespOperationDiagnostics _diagnostics;

    private ReadOnlyMemory<byte> _request;
    private object? _requestOwner;
    private int _requestRefCount;

    private const int
        Flag_Sent = 1 << 0,             // the request has been handed to a writer
        Flag_OutcomeKnown = 1 << 1,     // exactly one code path gets to set an outcome; this is the claim
        Flag_Complete = 1 << 2,         // the outcome is set and any follow-up has run
        Flag_NoPulse = 1 << 3,          // nobody is blocked in Wait, so completing need not take the lock
        Flag_Parser = 1 << 4,           // a parser was supplied
        Flag_MetadataParser = 1 << 5,   // the parser wants to see attributes/metadata itself
        Flag_InlineParser = 1 << 6,     // the parser is safe to run on the IO thread
        Flag_Indefinite = 1 << 7;       // the outcome does not prove the pipeline is done with us

    private const int FlagMask = 0xFFFF;

    /// <summary>Create a message in the pending state.</summary>
    /// <param name="options">What to do with the reply when it arrives.</param>
    protected RespMessageBase(RespParseOptions options = RespParseOptions.Parse)
    {
        _parseFlags = ToFlags(options);
        _state = Pack(_asyncCore.Version, _parseFlags);

        // Continuations are QUEUED, not run inline, and this is a correctness decision rather than a
        // tuning one. The thread that publishes an outcome is the connection's read loop; running a
        // caller's continuation on it means arbitrary user code - a database call, a lock, a Thread.Sleep -
        // sits in front of every other reply on that connection. That is head-of-line blocking for
        // everyone sharing the socket, which is the whole point of multiplexing.
        //
        // It is also what the existing core already does: every ResultBox completes with
        // RunContinuationsAsynchronously. This is not a new position, just the same one restated.
        _asyncCore.RunContinuationsAsynchronously = true;
    }

    private static int ToFlags(RespParseOptions options)
    {
        var flags = 0;
        if ((options & RespParseOptions.Parse) != 0) flags |= Flag_Parser;
        if ((options & RespParseOptions.Metadata) == RespParseOptions.Metadata) flags |= Flag_MetadataParser;
        if ((options & RespParseOptions.Inline) == RespParseOptions.Inline) flags |= Flag_InlineParser;
        return flags;
    }

    /// <summary>The current version; a handle taken now is valid until this instance is reset.</summary>
    public short Token => _asyncCore.Version;

    /// <inheritdoc/>
    public bool AllowInlineParsing => HasFlag(Flag_InlineParser);

    /// <summary>What this operation carries for diagnostics; see design notes section 4.</summary>
    public ref RespOperationDiagnostics Diagnostics => ref _diagnostics;

    /// <summary>Whether the outcome proved the pipeline is finished with this instance.</summary>
    /// <remarks>
    /// The owner consults this before returning the instance to a pool. It is <see langword="false"/>
    /// until an outcome is set, and stays <see langword="false"/> for timeouts and connection faults.
    /// </remarks>
    public bool IsRecyclable => (Volatile.Read(ref _state) & (Flag_Complete | Flag_Indefinite)) == Flag_Complete;

    /// <summary>Turn a reply into the response value.</summary>
    /// <param name="reader">Positioned at the reply, or before it when the parser handles metadata.</param>
    /// <remarks>
    /// Override <b>this</b> for the ordinary case - a value read out of the reply. Override
    /// <see cref="ParseFrame(ReadOnlySpan{byte})"/> instead when the response IS the frame: a payload to
    /// be handed on, retained, or cached, where turning the bytes into a value and back would be the
    /// copy this design exists to avoid. Exactly one of the two.
    /// </remarks>
    protected virtual TResponse Parse(ref RespReader reader)
        => throw new NotSupportedException($"{GetType().Name} must override {nameof(Parse)} or {nameof(ParseFrame)}.");

    /// <summary>Turn a complete reply frame into the response value.</summary>
    /// <param name="frame">The whole frame, including its prefix.</param>
    /// <remarks>
    /// The default positions a reader and defers to <see cref="Parse(ref RespReader)"/>, which is what
    /// almost everything wants. See that method for when to override this one instead.
    /// </remarks>
    protected virtual TResponse ParseFrame(scoped ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        if ((Volatile.Read(ref _state) & Flag_MetadataParser) == 0) reader.MoveNext();
        return Parse(ref reader);
    }

    /// <summary>Turn a complete reply frame, spanning several segments, into the response value.</summary>
    /// <param name="frame">The whole frame, including its prefix.</param>
    /// <remarks>See <see cref="ParseFrame(ReadOnlySpan{byte})"/>; this is the multi-segment twin.</remarks>
    protected virtual TResponse ParseFrame(in ReadOnlySequence<byte> frame)
    {
        var reader = new RespReader(frame);
        if ((Volatile.Read(ref _state) & Flag_MetadataParser) == 0) reader.MoveNext();
        return Parse(ref reader);
    }

    /// <summary>Called when the instance is reset, so derived state can be cleared too.</summary>
    /// <remarks>
    /// Exhaustive clearing matters more than it looks: a recycled instance that leaks a previous life's
    /// timestamps produces a timeout report describing the wrong command. See design notes section 4.
    /// </remarks>
    protected virtual void OnReset()
    {
    }

    /// <summary>Called after a definite outcome has been consumed, for pools to reclaim the instance.</summary>
    protected virtual void OnRecyclable()
    {
    }

    // ---- state helpers ------------------------------------------------------------------------------
    private static int Pack(short version, int flags) => (version << 16) | (flags & FlagMask);

    private static short VersionOf(int state) => unchecked((short)(state >> 16));

    private bool HasFlag(int flag) => (Volatile.Read(ref _state) & flag) != 0;

    /// <summary>Set flags, preserving the version. Returns whether this call was the one that set them.</summary>
    private bool SetFlag(int flag)
    {
        Debug.Assert(flag != 0 && (flag & FlagMask) == flag, "flags live in the low half");
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & flag) == flag) return false;
            if (Interlocked.CompareExchange(ref _state, state | flag, state) == state)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Claim the right to set the outcome, for the holder of <paramref name="token"/>.
    /// </summary>
    /// <remarks>
    /// The whole point of packing: version and claim move together, so a stale holder cannot win the
    /// claim on a later life no matter how it is scheduled.
    /// </remarks>
    private bool TryClaimOutcome(short token)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (VersionOf(state) != token) return false;        // stale handle, or already recycled
            if ((state & Flag_OutcomeKnown) != 0) return false;  // somebody else got there first
            if (Interlocked.CompareExchange(ref _state, state | Flag_OutcomeKnown, state) == state)
            {
                UnregisterCancellation();
                return true;
            }
        }
    }

    // ---- initialisation -----------------------------------------------------------------------------
    /// <summary>Attach the rendered request and arm cancellation.</summary>
    /// <param name="request">The rendered request bytes.</param>
    /// <param name="owner">An <see cref="ArrayPool{T}"/> or <see cref="IDisposable"/> that owns them, if any.</param>
    /// <param name="cancellationToken">Cancellation for this operation.</param>
    protected void SetRequest(ReadOnlyMemory<byte> request, object? owner, CancellationToken cancellationToken)
    {
        Debug.Assert(_requestRefCount == 0, "the request is being set more than once");
        _diagnostics.OnCreated(); // per LIFE, not per instance: a recycled operation is a new command
        _request = request;
        _requestOwner = owner;
        _requestRefCount = 1;
        _cancellationToken = cancellationToken;
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationRegistration = cancellationToken.Register(CancellationCallback, this);
        }
    }

    private static readonly Action<object?> CancellationCallback =
        static state => ((IRespMessage)state!).TrySetCanceled();

    private void UnregisterCancellation()
    {
        _cancellationRegistration.Dispose();
        _cancellationRegistration = default;
        _cancellationToken = default;
    }

    // ---- the request payload ------------------------------------------------------------------------
    /// <inheritdoc/>
    public bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true)
    {
        while (true)
        {
            var count = Volatile.Read(ref _requestRefCount);
            if (count == 0 || Token != token)
            {
                payload = default;
                return false;
            }

            if (Interlocked.CompareExchange(ref _requestRefCount, checked(count + 1), count) == count)
            {
                if (recordSent)
                {
                    SetFlag(Flag_Sent);
                    _diagnostics.Status = RespCommandStatus.Sent;
                }

                payload = _request;
                return true;
            }
        }
    }

    /// <inheritdoc/>
    public void OnEnqueued(object connection, long bytesSent, long bytesReceived)
        => _diagnostics.OnEnqueued(connection, bytesSent, bytesReceived);

    /// <inheritdoc/>
    public void ReleaseRequest()
    {
        if (!TryReleaseRequest()) Throw();

        static void Throw() => throw new InvalidOperationException("The request payload has already been released.");
    }

    /// <returns>Whether there was a reference to release; <b>not</b> whether it reached zero.</returns>
    private bool TryReleaseRequest()
    {
        while (true)
        {
            var count = Volatile.Read(ref _requestRefCount);
            if (count == 0) return false;
            if (Interlocked.CompareExchange(ref _requestRefCount, count - 1, count) == count)
            {
                if (count == 1) ReturnRequestBuffer();
                return true;
            }
        }
    }

    private void ReturnRequestBuffer()
    {
        switch (_requestOwner)
        {
            case ArrayPool<byte> pool when MemoryMarshal.TryGetArray(_request, out var segment) && segment.Array is not null:
                pool.Return(segment.Array);
                break;
            case IDisposable owner:
                owner.Dispose();
                break;
        }

        _request = default;
        _requestOwner = null;
    }

    // ---- outcomes -----------------------------------------------------------------------------------
    /// <inheritdoc/>
    public bool TrySetResult(short token, scoped ReadOnlySpan<byte> response)
    {
        if (!TryClaimOutcome(token)) return false;
        if ((Volatile.Read(ref _state) & Flag_Parser) == 0) return Complete(default!, definite: true);

        try
        {
            return Complete(ParseFrame(response), definite: true);
        }
        catch (Exception ex)
        {
            return Fail(ex, definite: true); // the server answered; parsing it is our problem, not the queue's
        }
    }

    /// <inheritdoc/>
    public bool TrySetResult(short token, in ReadOnlySequence<byte> response)
    {
        if (!TryClaimOutcome(token)) return false;
        if ((Volatile.Read(ref _state) & Flag_Parser) == 0) return Complete(default!, definite: true);

        try
        {
            return Complete(ParseFrame(in response), definite: true);
        }
        catch (Exception ex)
        {
            return Fail(ex, definite: true);
        }
    }

    /// <inheritdoc/>
    public bool TrySetException(short token, Exception exception, bool definite = false)
        => TryClaimOutcome(token) && Fail(exception, definite);

    /// <inheritdoc/>
    public bool TrySetCanceled(short token, CancellationToken cancellationToken = default)
    {
        var named = cancellationToken.IsCancellationRequested ? cancellationToken : _cancellationToken;
        return TryClaimOutcome(token) && Fail(new OperationCanceledException(named), definite: true);
    }

    /// <inheritdoc/>
    void IRespMessage.TrySetCanceled()
    {
        // the cancellation callback races everything else; the claim decides, and losing is normal
        var named = _cancellationToken;
        if (TryClaimOutcome(Token)) Fail(new OperationCanceledException(named), definite: true);
    }

    /// <summary>Fail with a timeout, which is never a definite outcome.</summary>
    /// <remarks>
    /// Marc's rule, and the reason it is worth a named method: <i>"timeouts are undefined chaos"</i>. The
    /// pipeline has not told us anything; it may still write the request and complete us later.
    /// </remarks>
    private bool TrySetTimeout()
        => TryClaimOutcome(Token) && Fail(new TimeoutException(), definite: false);

    private bool Complete(TResponse response, bool definite)
    {
        var pulse = Mark(definite);
        _asyncCore.SetResult(response);
        Pulse(pulse);
        return true;
    }

    private bool Fail(Exception exception, bool definite)
    {
        var pulse = Mark(definite);
        _asyncCore.SetException(exception);
        Pulse(pulse);
        return true;
    }

    /// <summary>
    /// Record how this life ended, <b>before</b> the outcome is published.
    /// </summary>
    /// <returns>Whether a synchronous waiter still needs waking.</returns>
    /// <remarks>
    /// <para>
    /// <b>The ordering here is the whole of it, and getting it wrong is silent.</b> Publishing the
    /// outcome can run an awaiting continuation <i>inline</i> - that is what
    /// <c>ManualResetValueTaskSourceCore.SetResult</c> does when a continuation is already registered -
    /// and that continuation calls <see cref="GetResult"/>, which reads <see cref="IsRecyclable"/> and
    /// then <see cref="Reset"/>s. So anything recorded after publishing is recorded too late to be read,
    /// and worse, lands on the instance's <i>next</i> life.
    /// </para>
    /// <para>
    /// The first version marked afterwards. Nothing failed: every test passed, the results were correct,
    /// and the only symptom was that pooling never engaged - <c>IsRecyclable</c> was false at every
    /// single <c>GetResult</c>, so an executor's pool took 0 hits out of 56,642 sends. It was found by
    /// benchmarking allocations, not by testing behaviour.
    /// </para>
    /// </remarks>
    private bool Mark(bool definite)
    {
        var pulse = !HasFlag(Flag_NoPulse);
        SetFlag(definite ? (Flag_Complete | Flag_NoPulse) : (Flag_Complete | Flag_NoPulse | Flag_Indefinite));
        return pulse;
    }

    private void Pulse(bool pulse)
    {
        if (!pulse) return;
        lock (this)
        {
            Monitor.PulseAll(this);
        }
    }

    // ---- consumption --------------------------------------------------------------------------------
    /// <summary>Read the outcome, resetting the instance.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <remarks>
    /// <b>The version moves immediately, not when the instance is next used.</b> Deferring it would let
    /// a second <c>GetResult</c> appear to work for a while - fine under a local build, a cross-wired
    /// reply under load. Moving it here turns that into an exception at the first offence.
    /// </remarks>
    public TResponse GetResult(short token)
    {
        var recyclable = IsRecyclable;
        try
        {
            return _asyncCore.GetResult(token);
        }
        finally
        {
            Reset();
            if (recyclable) OnRecyclable();
        }
    }

    /// <summary>Clear every trace of this life and move the version on.</summary>
    /// <remarks>
    /// This drops <i>our</i> reference on the request and no more. Clearing the buffer fields outright
    /// would strand a writer that still holds a reservation: its <see cref="ReleaseRequest"/> reads
    /// those fields to find the array to return, so nulling them here leaks the buffer instead of
    /// pooling it. The last releaser clears them, whoever that turns out to be.
    /// </remarks>
    private void Reset()
    {
        UnregisterCancellation();
        TryReleaseRequest();
        _diagnostics = default; // the whole point of keeping it in one struct
        OnReset();

        _asyncCore.Reset();

        // the version and the cleared flags land together, so nothing can observe a fresh version with
        // a previous life's claim still set; the parse capability is of the type, not the life, so it
        // is re-applied rather than cleared
        Volatile.Write(ref _state, Pack(_asyncCore.Version, _parseFlags));
    }

    /// <inheritdoc/>
    public TResponse Wait(short token, TimeSpan timeout)
    {
        // the CORE's status, not Flag_Complete: the flag is now set before the outcome is published, so
        // a waiter that trusted it could call GetResult on a core that has nothing in it yet
        if (_asyncCore.GetStatus(token) != ValueTaskSourceStatus.Pending) return GetResult(token);
        if (!HasFlag(Flag_Sent)) ThrowNotSent();

        CheckToken(token);
        var timedOut = false;
        lock (this)
        {
            switch (_asyncCore.GetStatus(token) != ValueTaskSourceStatus.Pending
                ? Flag_Complete
                : Volatile.Read(ref _state) & Flag_NoPulse)
            {
                case 0:
                    // the expected branch: not complete, and whoever completes it will pulse
                    if (timeout == TimeSpan.Zero)
                    {
                        Monitor.Wait(this);
                    }
                    else if (!Monitor.Wait(this, timeout))
                    {
                        timedOut = true;
                        SetFlag(Flag_NoPulse); // we are leaving; nobody need wake us
                    }

                    break;
                case Flag_NoPulse:
                    ThrowWillNotPulse();
                    break;
                default:
                    break; // already complete
            }
        }

        if (timedOut) TrySetTimeout();
        return GetResult(token);

        static void ThrowWillNotPulse() => throw new InvalidOperationException(
            "This operation cannot be waited on because it entered async mode - most likely by calling AsTask().");
    }

    private void CheckToken(short token)
    {
        if (token != _asyncCore.Version) _ = _asyncCore.GetStatus(token); // for the consistent message
    }

    private static void ThrowNotSent() => throw new InvalidOperationException(
        "This command has not been sent, so it cannot be awaited. If it belongs to a batch or transaction, execute that first.");

    /// <summary>The status, with a guard that catches awaiting an unsent command.</summary>
    /// <param name="token">The version this caller holds.</param>
    /// <remarks>
    /// Only the direct handle checks this. The <see cref="IValueTaskSource"/> implementations below do
    /// not, because a <c>ValueTask</c> pre-checks status on paths where throwing would be wrong.
    /// </remarks>
    public ValueTaskSourceStatus GetStatus(short token)
    {
        var status = _asyncCore.GetStatus(token);
        if (!HasFlag(Flag_Sent)) ThrowNotSent();
        return status;
    }

    /// <inheritdoc/>
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _asyncCore.OnCompleted(continuation, state, token, flags);
        SetFlag(Flag_NoPulse); // an async consumer will never be blocked in Wait
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _asyncCore.GetStatus(token);

    ValueTaskSourceStatus IValueTaskSource<TResponse>.GetStatus(short token) => _asyncCore.GetStatus(token);

    void IValueTaskSource.GetResult(short token) => _ = GetResult(token);

    void IRespMessage.Wait(short token, TimeSpan timeout) => _ = Wait(token, timeout);
}
