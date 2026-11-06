using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Availability;

/// <summary>
/// A transaction that replays as a unit: the frame-level successor to the capture-and-replay
/// <c>RetryTransaction</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is stored is the rendered request, not the method call.</b> Where <c>RetryTransaction</c>
/// recorded every builder invocation into a generated state struct and replayed it against a fresh
/// <see cref="ITransaction"/>, this accumulates what the command surface already produced - the rendered
/// <see cref="RespRequest"/>, its handler, and the conditions in semantic form - and each attempt feeds
/// them into a fresh, one-shot <see cref="RespTransactionExecutor"/>. Same unit, same loop, no generator.
/// </para>
/// <para>
/// <b>Why replay cannot reuse the attempt's operations.</b> Once a run is written, each queued operation
/// is a pooled, single-completion source enrolled with that connection: the connection's own teardown
/// faults it, and a second enrollment while the first may still touch it is a use-after-release on the
/// pool. So the operations enrolled per attempt are the attempt's own, and the caller holds durable
/// proxies that settle only when the unit has finished for good - exactly the shape
/// <c>RetryTransaction</c> used, one layer down.
/// </para>
/// <para>
/// <b>Why replaying the unit is safe at all</b>: the WATCH constraints live in the conditions, not the
/// queue, and each attempt re-issues them on its own connection - so a replay re-adjudicates against
/// fresh state rather than running with a dead connection's guard silently gone. The queued commands
/// run only if the re-issued conditions hold and the <c>EXEC</c> commits. An <c>EXEC</c> that was
/// written and never answered is NOT replayed blindly: the fault carries the queue's aggregate retry
/// category, so the shared policy gates the whole transaction exactly like one command.
/// </para>
/// </remarks>
internal sealed class RespRetryTransactionExecutor : RespExecutorBase, ITransactionExecutor
{
    private readonly RespExecutorBase _inner;
    private readonly RespContext _context;
    private readonly RetryController _controller;
    private readonly Func<CancellationToken>? _failoverSource;
    private readonly object _sync = new();

    private List<IRecorded>? _recorded;
    private List<RecordedCondition>? _conditions;
    private int _executed;
    private volatile bool _watchConflict;

    /// <summary>Create a retrying transaction over an executor.</summary>
    /// <param name="inner">The executor underneath the retry link; each attempt resolves through it.</param>
    /// <param name="context">The context conditions render through; it must NOT carry the retry link.</param>
    /// <param name="controller">The shared retry controller, from the database's <c>RespRetryExecutor</c>.</param>
    internal RespRetryTransactionExecutor(RespExecutorBase inner, RespContext context, RetryController controller)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _failoverSource = inner.GetFailoverSource();
    }

    /// <inheritdoc/>
    internal override bool Accumulates => true;

    /// <inheritdoc/>
    internal override bool Transactional => true;

    /// <inheritdoc/>
    public override int Database => _inner.Database;

    /// <inheritdoc/>
    internal override IConnectionMultiplexer? ExposedMultiplexer => _inner.ExposedMultiplexer;

    /// <inheritdoc/>
    internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
        => _inner.ResolveFor(in key, command, flags);

    /// <inheritdoc/>
    /// <remarks>Yes: the recorded request is this executor's own reference (or copy) by the time this returns.</remarks>
    internal override bool CopiesRequestOnSend => true;

    /// <inheritdoc/>
    public bool WasWatchConflict => _watchConflict;

    /// <inheritdoc/>
    public override RespPayload Send(in RespRequest request)
        => throw new NotSupportedException("A queued command cannot be waited on before the transaction is executed.");

    /// <inheritdoc/>
    /// <remarks>
    /// The payload shape: the recorded command owns no handler, and the final attempt's payload is handed
    /// to the caller intact rather than parsed - the same contract as the plain executor's untyped path.
    /// The request is NOT retained here: by that path's convention the caller holds its reference until
    /// the returned task completes, which is exactly the final attempt.
    /// </remarks>
    public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
    {
        if ((request.Flags & CommandFlags.FireAndForget) != 0)
        {
            Record(new RecordedPayload(request, ownsRequest: false, proxy: null, cancellationToken));
            return default;
        }

        var proxy = new TaskCompletionSource<RespPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        Record(new RecordedPayload(request, ownsRequest: false, proxy, cancellationToken));
        return new ValueTask<RespPayload>(proxy.Task);
    }

    /// <inheritdoc/>
    internal override ValueTask<TResult> SendTypedAsync<TResult>(
        RespRequest request, IRespHandler<TResult> handler, CancellationToken cancellationToken)
    {
        // the same special case as the plain executor: a payload-typed result goes the untyped route
        if (typeof(TResult) == typeof(RespPayload)) return RespExecutor.AwaitUncached(this, request, handler, cancellationToken);

        // ToOwned: a borrowed request is copied, an owned one is kept - this path's convention
        // (CopiesRequestOnSend) makes the incoming reference ours to keep, and Release gives it back
        var owned = request.ToOwned();

        if ((owned.Flags & CommandFlags.FireAndForget) != 0)
        {
            Record(new Recorded<TResult>(owned, ownsRequest: true, handler, proxy: null, _inner, cancellationToken));

            // the one shared completed-default task, as the plain transaction hands out for the same case
            return new ValueTask<TResult>(CompletedTask<TResult>.Default(null)!);
        }

        var proxy = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Record(new Recorded<TResult>(owned, ownsRequest: true, handler, proxy, _inner, cancellationToken));
        return new ValueTask<TResult>(proxy.Task);
    }

    /// <inheritdoc/>
    internal override ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, CancellationToken cancellationToken)
    {
        var pending = SendTypedAsync(request, handler, cancellationToken);
        if (pending.IsCompletedSuccessfully)
        {
            _ = pending.Result;
            return default;
        }

        return new ValueTask(pending.AsTask());
    }

    private void Record(IRecorded recorded)
    {
        lock (_sync)
        {
            if (_executed != 0) throw new InvalidOperationException("This transaction has already been executed.");
            (_recorded ??= []).Add(recorded);
        }
    }

    /// <inheritdoc/>
    public void AddCondition(Condition condition, Action<bool>? onVerdict = null)
    {
        if (condition is null) throw new ArgumentNullException(nameof(condition));
        lock (_sync)
        {
            if (_executed != 0) throw new InvalidOperationException("Operations cannot be added after the transaction has been executed.");
            (_conditions ??= []).Add(new RecordedCondition(condition, onVerdict));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The fold the retry policy gates the whole unit with; see the plain executor's twin.</remarks>
    public CommandFlags AggregateRetryCategory
    {
        get
        {
            var result = CommandFlags.None;
            lock (_sync)
            {
                if (_recorded is { } recorded)
                {
                    foreach (var op in recorded)
                    {
                        var category = op.Flags & CommandFlagsInternal.MaskRetryCategory;
                        if (category > result) result = category;
                    }
                }
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None)
    {
        List<IRecorded>? recorded;
        List<RecordedCondition>? conditions;
        lock (_sync)
        {
            if (_executed != 0) throw new InvalidOperationException("This transaction has already been executed.");
            _executed = 1;

            // taken under the lock, so the loops below run over lists nothing can still be adding to
            recorded = _recorded;
            conditions = _conditions;
            _recorded = null;
            _conditions = null;
        }

        try
        {
            return await ExecuteCoreAsync(flags, recorded ?? [], conditions).ConfigureAwait(false);
        }
        finally
        {
            if (recorded is not null)
            {
                foreach (var op in recorded) op.Release();
            }
        }
    }

    private async Task<bool> ExecuteCoreAsync(CommandFlags flags, List<IRecorded> recorded, List<RecordedCondition>? conditions)
    {
        var attemptCount = 0;

        // captured BEFORE the first attempt - otherwise a failover between a failed attempt and
        // re-reading the token could be missed (same reasoning as RespRetryExecutor)
        var failover = _controller.TracksFailover ? (_failoverSource?.Invoke() ?? CancellationToken.None) : CancellationToken.None;

        // watch contention gets its own budget; only meaningful when there are conditions to WATCH
        var watchAttempt = 0;
        var maxWatchAttempts = conditions is null ? 1 : _controller.MaxWatchConflictAttempts;
        while (true)
        {
            // a fresh, one-shot unit per attempt: the conditions re-render and re-WATCH on whatever
            // connection this attempt lands on, and the queued frames re-enroll as that attempt's own
            // operations - the durable proxies the caller holds are settled only at the end
            var attempt = new RespTransactionExecutor(_inner, _context, reusable: false);
            if (conditions is not null)
            {
                foreach (var condition in conditions) condition.Replay(attempt);
            }

            foreach (var op in recorded) op.Replay(attempt);

            // the aggregate category rides on the EXEC's flags, so the fault it reports carries it and
            // the shared policy gates the whole transaction exactly like one command
            var effectiveFlags = (flags & ~CommandFlagsInternal.MaskRetryCategory) | attempt.AggregateRetryCategory;

            try
            {
                var committed = await attempt.ExecuteAsync(effectiveFlags).ConfigureAwait(false);
                _watchConflict = attempt.WasWatchConflict;

                // the server rejected an EXEC we really did issue because a watched key changed under us:
                // contention rather than a fault, so it has its own budget, waits its own delay, and the
                // side-effect category does not apply - nothing happened
                if (!committed && _watchConflict && ++watchAttempt < maxWatchAttempts)
                {
                    DiscardAttempt(recorded, conditions);
                    await _controller.WatchConflictDelayAsync().ConfigureAwait(false);
                    continue;
                }

                // clean completion (committed, or electively aborted): forward the per-attempt outcomes
                // onto the durable proxies - settled before we return, unless the caller declined the
                // EXEC's own reply, in which case waiting is the opposite of what they asked
                if (conditions is not null)
                {
                    foreach (var condition in conditions) condition.Forward();
                }

                List<Task>? settling = null;
                foreach (var op in recorded)
                {
                    if (op.ForwardOutcome() is { } pending) (settling ??= []).Add(pending);
                }

                if (settling is not null && (effectiveFlags & CommandFlags.FireAndForget) == 0)
                {
                    foreach (var pending in settling)
                    {
                        await pending.ConfigureAwait(false); // never faults: the forwarders catch and settle the proxy
                    }
                }

                return committed;
            }
            catch (Exception ex)
            {
                if (_controller.CanRetry(++attemptCount, ex, ref failover, out var delay))
                {
                    DiscardAttempt(recorded, conditions);
                    await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
                    continue;
                }

                // out of road: fault the durable proxies and surface the failure to the caller
                foreach (var op in recorded) op.Fault(ex);
                throw;
            }
        }
    }

    /// <summary>Consume a discarded attempt's results and reset for the next.</summary>
    private static void DiscardAttempt(List<IRecorded> recorded, List<RecordedCondition>? conditions)
    {
        foreach (var op in recorded) op.Drain();
        if (conditions is not null)
        {
            foreach (var condition in conditions) condition.Reset();
        }
    }

    // ---- recorded work -----------------------------------------------------------------------------
    private interface IRecorded
    {
        /// <summary>The recorded request's flags, for the aggregate retry category.</summary>
        CommandFlags Flags { get; }

        /// <summary>Queue the recorded request onto this attempt's unit.</summary>
        void Replay(RespTransactionExecutor attempt);

        /// <summary>
        /// Copy this attempt's outcome onto the durable proxy; the returned task is the settling still in
        /// flight (a fire-and-forget EXEC completes before the replies land), or null when it is done.
        /// </summary>
        Task? ForwardOutcome();

        /// <summary>Consume a discarded attempt's result, so a pooled source is released and a fault is observed.</summary>
        void Drain();

        /// <summary>Fault the durable proxy; the unit is out of attempts.</summary>
        void Fault(Exception ex);

        /// <summary>Give back the recorded request, once, after the unit has finished with it for good.</summary>
        void Release();
    }

    /// <summary>A recorded command whose caller wants a parsed <typeparamref name="TResult"/>.</summary>
    private sealed class Recorded<TResult> : IRecorded
    {
        private readonly RespRequest _request;
        private readonly bool _ownsRequest;
        private readonly IRespHandler<TResult> _handler;
        private readonly TaskCompletionSource<TResult>? _proxy; // null: fire-and-forget declined the reply
        private readonly RespExecutorBase _authSource;
        private readonly CancellationToken _cancellationToken;
        private ValueTask<RespPayload> _attempt;
        private bool _hasAttempt;

        public Recorded(
            in RespRequest request,
            bool ownsRequest,
            IRespHandler<TResult> handler,
            TaskCompletionSource<TResult>? proxy,
            RespExecutorBase authSource,
            CancellationToken cancellationToken)
        {
            _request = request;
            _ownsRequest = ownsRequest;
            _handler = handler;
            _proxy = proxy;
            _authSource = authSource;
            _cancellationToken = cancellationToken;
        }

        public CommandFlags Flags => _request.Flags;

        public void Replay(RespTransactionExecutor attempt)
        {
            _attempt = attempt.SendAsync(_request, _cancellationToken);
            _hasAttempt = true;
        }

        public Task? ForwardOutcome()
        {
            if (_proxy is null)
            {
                Drain(); // fire-and-forget: nothing to carry, but a pooled source still has to be consumed
                return null;
            }

            if (!_hasAttempt) return null; // loud failure below would hide the real bug; Replay always precedes
            var attempt = _attempt;
            _attempt = default;
            _hasAttempt = false;
            if (attempt.IsCompleted)
            {
                Forward(attempt);
                return null;
            }

            return ForwardAsync(attempt);
        }

        private void Forward(ValueTask<RespPayload> attempt)
        {
            RespPayload? payload = null;
            try
            {
                payload = attempt.GetAwaiter().GetResult();
                _proxy!.TrySetResult(RespExecutor.Parse(_handler, payload));
            }
            catch (OperationCanceledException)
            {
                _proxy!.TrySetCanceled();
            }
            catch (RedisServerException ex) when (RespExecutor.AuthFault(_authSource, ex) is { } authFault)
            {
                _proxy!.TrySetException(authFault);
            }
            catch (Exception ex)
            {
                _proxy!.TrySetException(ex);
            }
            finally
            {
                payload?.Release();
            }
        }

        private async Task ForwardAsync(ValueTask<RespPayload> attempt)
        {
            RespPayload? payload = null;
            try
            {
                payload = await attempt.ConfigureAwait(false);
                _proxy!.TrySetResult(RespExecutor.Parse(_handler, payload));
            }
            catch (OperationCanceledException)
            {
                _proxy!.TrySetCanceled();
            }
            catch (RedisServerException ex) when (RespExecutor.AuthFault(_authSource, ex) is { } authFault)
            {
                _proxy!.TrySetException(authFault);
            }
            catch (Exception ex)
            {
                _proxy!.TrySetException(ex);
            }
            finally
            {
                payload?.Release();
            }
        }

        public void Drain()
        {
            if (!_hasAttempt) return;
            var attempt = _attempt;
            _attempt = default;
            _hasAttempt = false;
            DrainCore(attempt);
        }

        public void Fault(Exception ex)
        {
            Drain();
            _proxy?.TrySetException(ex);
        }

        public void Release()
        {
            if (_ownsRequest) _request.Dispose();
        }
    }

    /// <summary>A recorded command whose caller wants the raw payload, handed over intact.</summary>
    private sealed class RecordedPayload : IRecorded
    {
        private readonly RespRequest _request;
        private readonly bool _ownsRequest;
        private readonly TaskCompletionSource<RespPayload>? _proxy;
        private readonly CancellationToken _cancellationToken;
        private ValueTask<RespPayload> _attempt;
        private bool _hasAttempt;

        public RecordedPayload(in RespRequest request, bool ownsRequest, TaskCompletionSource<RespPayload>? proxy, CancellationToken cancellationToken)
        {
            _request = request;
            _ownsRequest = ownsRequest;
            _proxy = proxy;
            _cancellationToken = cancellationToken;
        }

        public CommandFlags Flags => _request.Flags;

        public void Replay(RespTransactionExecutor attempt)
        {
            _attempt = attempt.SendAsync(_request, _cancellationToken);
            _hasAttempt = true;
        }

        public Task? ForwardOutcome()
        {
            if (_proxy is null)
            {
                Drain();
                return null;
            }

            if (!_hasAttempt) return null;
            var attempt = _attempt;
            _attempt = default;
            _hasAttempt = false;
            if (attempt.IsCompleted)
            {
                Forward(attempt);
                return null;
            }

            return ForwardAsync(attempt);
        }

        // ownership transfers to the caller: the payload is NOT released here
        private void Forward(ValueTask<RespPayload> attempt)
        {
            try
            {
                _proxy!.TrySetResult(attempt.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException)
            {
                _proxy!.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _proxy!.TrySetException(ex);
            }
        }

        private async Task ForwardAsync(ValueTask<RespPayload> attempt)
        {
            try
            {
                _proxy!.TrySetResult(await attempt.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                _proxy!.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _proxy!.TrySetException(ex);
            }
        }

        public void Drain()
        {
            if (!_hasAttempt) return;
            var attempt = _attempt;
            _attempt = default;
            _hasAttempt = false;
            DrainCore(attempt);
        }

        public void Fault(Exception ex)
        {
            Drain();
            _proxy?.TrySetException(ex);
        }

        public void Release()
        {
            if (_ownsRequest) _request.Dispose();
        }
    }

    /// <summary>Consume a discarded attempt's result: release any payload, observe any fault.</summary>
    private static void DrainCore(ValueTask<RespPayload> attempt)
    {
        if (attempt.IsCompleted)
        {
            try
            {
                attempt.GetAwaiter().GetResult()?.Release();
            }
            catch
            {
                // a discarded attempt; the outcome that matters is the retry decision already made
            }

            return;
        }

        _ = Awaited(attempt);

        static async Task Awaited(ValueTask<RespPayload> pending)
        {
            try
            {
                (await pending.ConfigureAwait(false))?.Release();
            }
            catch
            {
                // observed; see above
            }
        }
    }

    /// <summary>A condition, re-issued per attempt, whose durable verdict sink hears only the final attempt.</summary>
    private sealed class RecordedCondition
    {
        private readonly Condition _condition;
        private readonly Action<bool>? _sink;
        private bool _hasVerdict;
        private bool _verdict;

        public RecordedCondition(Condition condition, Action<bool>? sink)
        {
            _condition = condition;
            _sink = sink;
        }

        public void Replay(RespTransactionExecutor attempt)
            => attempt.AddCondition(_condition, OnVerdict);

        private void OnVerdict(bool satisfied)
        {
            _verdict = satisfied;
            _hasVerdict = true;
        }

        public void Reset() => _hasVerdict = false;

        public void Forward()
        {
            if (_hasVerdict) _sink?.Invoke(_verdict);
        }
    }
}
