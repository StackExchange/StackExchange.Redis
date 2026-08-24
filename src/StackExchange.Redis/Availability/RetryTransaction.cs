using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.Availability;

// A retryable transaction. Unlike RetryDatabase - which replays a single captured typed call each attempt -
// a transaction is built up across many builder calls plus an Execute, so we *record* each captured call
// (the AutoDatabase (state, projection) pair) and hand the caller a durable, still-incomplete proxy task.
// Only ExecuteAsync actually runs anything: for each attempt it spins up a fresh, one-shot inner transaction
// against the underlying database (which, for a multi-group connection, resolves the currently-active member,
// so a retry after failover lands on the new member), replays every recorded operation and constraint onto
// it, and awaits it. On a clean execution the per-attempt results are forwarded onto the durable proxies; on
// a transient fault the whole attempt is discarded and replayed; on terminal failure the proxies are faulted.
//
// ITransaction (rather than just ITransactionAsync) so that callers written against the long-standing
// interface can move to a retrying database without rewriting their transaction code; the synchronous
// Execute is sync-over-async (see below). Note that ITransaction : IBatch : IDatabaseAsync, so the only
// members this adds over ITransactionAsync are the two Execute overloads.
[AutoDatabase]
internal sealed partial class RetryTransaction : IDatabaseAsync, ITransaction
{
    // Note: the *command* surface is async-only, exactly like RetryDatabase - retrying is inherently
    // delay-ish; only the terminal Execute is offered synchronously.
    private readonly IDatabaseAsync _source;
    private readonly RetryController _controller;

    private readonly List<IRecordedOp> _ops = new();
    private List<RecordedCondition>? _conditions;
    private int _executed;
    private volatile bool _watchConflict;

    /// <inheritdoc/>
    // reports the *final* attempt's outcome: false if we eventually committed (or aborted electively),
    // true if we ran out of watch-conflict attempts still losing the race
    public bool WasWatchConflict => _watchConflict;

    public RetryTransaction(IDatabaseAsync source, RetryController controller)
    {
        _source = source;
        _controller = controller;
    }

    public int Database => _source.Database;

    public IConnectionMultiplexer Multiplexer => _source.Multiplexer;

    /// <inheritdoc/>
    public override string ToString() => "retry-transaction: " + _source;

    // the generated explicit interface implementations funnel every builder call through these two
    // overloads, capturing the arguments in a generated state struct plus a cacheable static projection
    // (no per-call closure). Here we simply *record* them and return a durable proxy task.
    private Task<TResult> ExecuteAsync<TState, TResult>(in TState state, AutoDatabaseAsyncOperation<TState, TResult> operation)
        where TState : struct
    {
        CheckNotExecuted();
        var op = new RecordedOp<TState, TResult>(state, operation, IsFireAndForget(in state));
        _ops.Add(op);
        return op.Proxy;
    }

    private Task ExecuteAsync<TState>(in TState state, AutoDatabaseAsyncOperation<TState> operation)
        where TState : struct
    {
        CheckNotExecuted();
        var op = new RecordedVoidOp<TState>(state, operation, IsFireAndForget(in state));
        _ops.Add(op);
        return op.Proxy;
    }

    // A fire-and-forget operation must hand back an already-completed task, because that is what a plain
    // RedisTransaction does (see its ExecuteAsync: F+F never gets a TaskCompletionSource at all). Without this
    // the durable proxy stays incomplete until Execute, so identical caller code returns instantly on a plain
    // transaction and blocks for good on a retrying one - which defeats this class's stated goal of letting
    // ITransaction code move onto a retrying database unchanged.
    //
    // The op is still recorded and replayed like any other: fire-and-forget declines the *reply*, not the
    // command. What it does not get is a proxy - see RecordedOp - so forwarding and faulting both become
    // no-ops for it, and a discarded attempt can never fault a result the caller was told not to expect.
    //
    // The flags are captured inside TState, so this is the one thing the generated struct is asked to expose
    // (IFlaggedRedisArgs). Testing a generic value against an interface boxes in IL, and this sits on every
    // queued operation - but value-type generics are not shared, so the JIT knows the exact TState and
    // compiles the whole thing to a field load with no allocator call at all (11 bytes of machine code).
    //
    // Measured on net10.0 rather than reasoned about, because none of it is obvious - bytes per call:
    //
    //   `is` pattern, tier-0 ......... 24     `is` pattern, optimized ....... 0
    //   cast instead of `is` ......... 24     ...with AggressiveOptimization  0
    //
    // Two findings worth keeping. Rewriting the `is` pattern as a cast does *not* avoid the box - both
    // spellings emit the same one - so the attribute below is the fix rather than a different expression.
    // And a TState *without* the interface costs nothing even in tier-0: the test folds to a constant and
    // the dead box is removed, so the states that captured no flags never pay for this at all.

    // skips tiering, so the above is free from the first call rather than only after tier-1 promotion
#if NET
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    private static bool IsFireAndForget<TState>(in TState state)
        where TState : struct
        => state is IFlaggedRedisArgs flagged && (flagged.Flags & CommandFlags.FireAndForget) != 0;

    public ConditionResult AddCondition(Condition condition)
    {
        if (condition is null) throw new ArgumentNullException(nameof(condition));
        CheckNotExecuted();
        var recorded = new RecordedCondition(condition);
        (_conditions ??= new List<RecordedCondition>()).Add(recorded);
        return recorded.Result;
    }

    public async Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None)
    {
        if (Interlocked.CompareExchange(ref _executed, 1, 0) != 0)
        {
            throw new InvalidOperationException("This transaction has already been executed");
        }

        int attempt = 0;
        // capture the next-failover token *before* the first attempt - otherwise a failover between a
        // failed attempt and re-reading the token could be missed
        CancellationToken failover = _controller.TracksFailover ? _source.GetNextFailover() : CancellationToken.None;

        var conditions = _conditions;

        // watch contention gets its own budget; only meaningful when there are conditions to WATCH
        int watchAttempt = 0;
        int maxWatchAttempts = conditions is null ? 1 : _controller.MaxWatchConflictAttempts;
        while (true)
        {
            // no async-state: RetryDatabase refuses one (the durable proxies below cannot carry it)
            var inner = _source.CreateTransaction();

            // replay the recorded constraints and operations onto this fresh, one-shot transaction; the
            // per-attempt tasks they return are forwarded to the durable proxies only on a clean execution
            if (conditions is not null)
            {
                foreach (var c in conditions) c.Replay(inner);
            }
            foreach (var op in _ops) op.Replay(inner);

            // inject the aggregate retry category onto the EXEC flags so the resulting fault carries it, and
            // the shared RetryPolicy/FaultContext logic gates the whole transaction exactly like one command
            var category = inner is IInternalTransaction it ? it.GetAggregateRetryCategory() : CommandFlags.CommandRetryNever;
            var effectiveFlags = (flags & ~Message.MaskRetryCategory) | category;

            try
            {
                bool committed = await inner.ExecuteAsync(effectiveFlags).ConfigureAwait(false);
                _watchConflict = inner.WasWatchConflict; // surfaced to our own caller, per attempt

                // The server rejected an EXEC we really did issue, because another connection changed a
                // watched key: the conditions still held, nothing was applied, and we simply lost a race.
                // Re-read and try again (which re-issues the WATCH constraints, so a condition that has
                // genuinely stopped holding converges on an elective abort instead of looping). This is
                // contention rather than a fault, so it neither consumes the fault budget nor waits for a
                // failover, and the side-effect category does not apply - nothing happened.
                if (!committed
                    && _watchConflict
                    && ++watchAttempt < maxWatchAttempts)
                {
                    foreach (var op in _ops) op.Observe();
                    await _controller.WatchConflictDelayAsync().ConfigureAwait(false);
                    continue;
                }

                // clean completion (committed, or aborted); forward the per-attempt outcomes onto the
                // durable proxies and we're done
                if (conditions is not null)
                {
                    foreach (var c in conditions) c.ForwardSuccess();
                }
                foreach (var op in _ops) op.ForwardSuccess();
                return committed;
            }
            catch (Exception ex)
            {
                if (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
                {
                    // discard this attempt; observe its faulted per-attempt tasks so they don't surface as
                    // unobserved, then wait / failover and replay from the recorded snapshot
                    foreach (var op in _ops) op.Observe();
                    await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
                    continue;
                }

                // out of road: fault the durable proxies and surface the failure to the caller
                foreach (var op in _ops) op.Fault(ex);
                throw;
            }
        }
    }

    // ---- the synchronous ITransaction surface -------------------------------------------------------
    // Both are explicit implementations: they exist purely so that callers written against ITransaction can
    // move onto a retrying database, so they should be reachable only *through* that interface - internal
    // code (which has ExecuteAsync) has no business blocking, and this also keeps Execute() and
    // Execute(flags) from competing in overload resolution on the concrete type.

    /// <inheritdoc/>
    // The IBatch shape: enqueue, and don't ask about the outcome. Mirrors RedisTransaction by mapping onto
    // fire-and-forget - which for a *retrying* transaction means there is nothing for the retry machinery to
    // observe (no reply is requested, so no fault is ever reported) and it collapses to a single attempt:
    // the trade-off fire-and-forget always makes, here extending to the retries as well.
    void IBatch.Execute() => ((ITransaction)this).Execute(CommandFlags.FireAndForget);

    /// <inheritdoc/>
    // Sync-over-async, deliberately: retrying is inherently delay-ish, which is why the *command* surface
    // here is async-only - but a caller written against ITransaction has no async alternative for the
    // terminal Execute, and refusing it would mean such code cannot adopt a retrying database at all.
    // Blocking here is safe against a captured synchronization context: every await in ExecuteAsync (and in
    // RetryController) is ConfigureAwait(false), and the per-operation proxies complete asynchronously.
    //
    // The wait is *not* bounded by the multiplexer's SyncTimeout - the whole point of a retry is to outlast
    // the fault, so the delays between attempts are expected to exceed it. Each individual attempt is still
    // bounded by the inner connection's own timeout, so the total remains bounded by the attempt budget.
    // GetResult (rather than Wait) so the original exception surfaces, not an AggregateException wrapping it.
    bool ITransaction.Execute(CommandFlags flags) => ExecuteAsync(flags).GetAwaiter().GetResult();

    private void CheckNotExecuted()
    {
        if (Volatile.Read(ref _executed) != 0)
        {
            throw new InvalidOperationException("Operations cannot be added after the transaction has been executed");
        }
    }

    // ---- recorded work -------------------------------------------------------------------------------
    private interface IRecordedOp
    {
        void Replay(IDatabaseAsync inner);

        // Copy this attempt's outcome onto the durable proxy. A *replied* EXEC populates every queued
        // operation's result before its own task completes (TransactionProcessor does both while processing
        // that one reply), so the outcome is normally available inline. A fire-and-forget EXEC is the
        // exception: it completes as soon as it has been written, with the replies still in flight - so
        // forwarding must be deferred rather than blocking for a round-trip the caller explicitly declined
        // (which is what F+F means, and what a plain RedisTransaction does in the same situation).
        void ForwardSuccess();
        void Fault(Exception ex);
        void Observe();
    }

    private sealed class RecordedOp<TState, TResult> : IRecordedOp
        where TState : struct
    {
        private readonly TState _state;
        private readonly AutoDatabaseAsyncOperation<TState, TResult> _operation;
        private readonly TaskCompletionSource<TResult>? _proxy;
        private Task<TResult>? _attempt;

        public RecordedOp(in TState state, AutoDatabaseAsyncOperation<TState, TResult> operation, bool fireAndForget)
        {
            _state = state;
            _operation = operation;

            // Fire-and-forget declines the reply, so there is nothing for a proxy to carry and none is built -
            // saving *both* objects, the TaskCompletionSource and the Task it creates in its own constructor,
            // on every fire-and-forget operation queued. Proxy hands back the shared completed task instead,
            // which is the same instance a plain RedisTransaction returns for this case.
            //
            // The value is default(TResult) rather than the command's own "no reply" value, which lives inside
            // the command implementation and is not reachable from here - a distinction without a difference,
            // since neither is an answer from the server.
            _proxy = fireAndForget ? null : new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // the ! is an annotation artifact, not a null: TResult is unconstrained, so Task<TResult?> and
        // Task<TResult> are the same type - Default simply spells its return the other way round
        public Task<TResult> Proxy => _proxy?.Task ?? CompletedTask<TResult>.Default(null)!;

        public void Replay(IDatabaseAsync inner) => _attempt = _operation(in _state, inner);

        public void ForwardSuccess()
        {
            // fire-and-forget: Forward guards too, so this is here to skip the continuation below rather than
            // for correctness - there is no point registering one to settle a proxy that does not exist
            if (_proxy is null) return;

            // ForwardSuccess only runs after Replay, so this cannot be null; `?? throw` says that to the
            // compiler as well as the reader, and keeps the failure loud if the order ever changes
            var attempt = _attempt ?? throw new InvalidOperationException("Cannot forward a result before the operation has been replayed");
            if (attempt.IsCompleted)
            {
                Forward(attempt);
            }
            else
            {
                // fire-and-forget EXEC: see the note on ForwardLater
                ForwardLater(attempt);
            }
        }

        private void ForwardLater(Task<TResult> attempt) => attempt.ContinueWith(
            static (completed, state) => ((RecordedOp<TState, TResult>)state!).Forward(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        private void Forward(Task<TResult> attempt)
        {
            // a null proxy is fire-and-forget; `?.` then skips the argument too, including the Exception
            // access that would otherwise observe a fault. Safe only because such an attempt is the shared
            // completed task RedisTransaction hands back for F+F, so it cannot fault - see Observe for the
            // attempts that can.
            if (attempt.IsCanceled) _proxy?.TrySetCanceled();
            else if (attempt.IsFaulted) _proxy?.TrySetException(attempt.Exception!.InnerExceptions);
            else _proxy?.TrySetResult(attempt.GetAwaiter().GetResult());
        }

        // observe the (faulted) inner attempt before faulting the durable proxy, so the discarded
        // per-attempt task doesn't surface as an unobserved exception
        public void Fault(Exception ex)
        {
            Observe();
            _proxy?.TrySetException(ex);
        }

        public void Observe() => _ = _attempt?.Exception;
    }

    private sealed class RecordedVoidOp<TState> : IRecordedOp
        where TState : struct
    {
        private readonly TState _state;
        private readonly AutoDatabaseAsyncOperation<TState> _operation;
        private readonly TaskCompletionSource<bool>? _proxy;
        private Task? _attempt;

        public RecordedVoidOp(in TState state, AutoDatabaseAsyncOperation<TState> operation, bool fireAndForget)
        {
            _state = state;
            _operation = operation;
            _proxy = fireAndForget ? null : new(TaskCreationOptions.RunContinuationsAsynchronously); // see RecordedOp
        }

        public Task Proxy => _proxy?.Task ?? Task.CompletedTask;

        public void Replay(IDatabaseAsync inner) => _attempt = _operation(in _state, inner);

        public void ForwardSuccess()
        {
            if (_proxy is null) return; // fire-and-forget: see RecordedOp.ForwardSuccess

            // ForwardSuccess only runs after Replay, so this cannot be null; `?? throw` says that to the
            // compiler as well as the reader, and keeps the failure loud if the order ever changes
            var attempt = _attempt ?? throw new InvalidOperationException("Cannot forward a result before the operation has been replayed");
            if (attempt.IsCompleted)
            {
                Forward(attempt);
            }
            else
            {
                // fire-and-forget EXEC: see the note on ForwardLater
                ForwardLater(attempt);
            }
        }

        private void ForwardLater(Task attempt) => attempt.ContinueWith(
            static (completed, state) => ((RecordedVoidOp<TState>)state!).Forward(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        private void Forward(Task attempt)
        {
            // see RecordedOp.Forward for why `?.` is safe here
            if (attempt.IsCanceled) _proxy?.TrySetCanceled();
            else if (attempt.IsFaulted) _proxy?.TrySetException(attempt.Exception!.InnerExceptions);
            else _proxy?.TrySetResult(true);
        }

        // observe the (faulted) inner attempt before faulting the durable proxy, so the discarded
        // per-attempt task doesn't surface as an unobserved exception
        public void Fault(Exception ex)
        {
            Observe();
            _proxy?.TrySetException(ex);
        }

        public void Observe() => _ = _attempt?.Exception;
    }

    private sealed class RecordedCondition
    {
        private readonly Condition _condition;
        private ConditionResult? _attempt;

        public RecordedCondition(Condition condition)
        {
            _condition = condition;
            Result = new ConditionResult(condition);
        }

        // the durable result handed back to the caller from AddCondition
        public ConditionResult Result { get; }

        public void Replay(ITransactionAsync inner) => _attempt = inner.AddCondition(_condition);

        // Unlike the operations, this needs no deferral for a fire-and-forget EXEC: a conditional transaction
        // has to know whether its constraints held before it can decide between EXEC and DISCARD, so the
        // condition replies are resolved as part of writing it - even when the EXEC itself wants no reply.
        public void ForwardSuccess()
        {
            if (_attempt is not null) Result.SetSatisfied(_attempt.WasSatisfied);
        }
    }

    // ---- hand-implemented members the generator deliberately skips ----------------------------------
    // (the Wait family, the synchronous IsConnected probe, and the streaming scans). Wait/IsConnected are
    // straight pass-throughs; scans cannot participate in a transaction.
    // forwarding is not using: these decorators must implement the interface in full, and the
    // implementation cannot be dropped while the interface declares it
    #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
    void IRedisAsync.Wait(Task task) => _source.Wait(task);
    T IRedisAsync.Wait<T>(Task<T> task) => _source.Wait<T>(task);
    void IRedisAsync.WaitAll(Task[] tasks) => _source.WaitAll(tasks);
    bool IRedisAsync.TryWait(Task task) => _source.TryWait(task);
    #pragma warning restore SER308

    bool IDatabaseAsync.IsConnected(RedisKey key, CommandFlags flags) => _source.IsConnected(key, flags);

    // routing lookup against the underlying database, not a queued transaction operation
    Task<System.Net.EndPoint?> IDatabaseAsync.IdentifyEndpointAsync(RedisKey key, CommandFlags flags) => _source.IdentifyEndpointAsync(key, flags);

    // nested transactions are not supported (mirrors RedisTransaction)
    ITransactionAsync IDatabaseAsync.CreateTransaction(object? asyncState) => throw new NotSupportedException("Nested transactions are not supported");

    IAsyncEnumerable<HashEntry> IDatabaseAsync.HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotSupportedException("Scans cannot be used inside a transaction");
    IAsyncEnumerable<RedisValue> IDatabaseAsync.HashScanNoValuesAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotSupportedException("Scans cannot be used inside a transaction");
    IAsyncEnumerable<RedisValue> IDatabaseAsync.SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotSupportedException("Scans cannot be used inside a transaction");
    IAsyncEnumerable<RedisValue> IDatabaseAsync.VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start, RedisValue end, long count, Exclude exclude, CommandFlags flags) => throw new NotSupportedException("Scans cannot be used inside a transaction");
    IAsyncEnumerable<SortedSetEntry> IDatabaseAsync.SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotSupportedException("Scans cannot be used inside a transaction");
}
