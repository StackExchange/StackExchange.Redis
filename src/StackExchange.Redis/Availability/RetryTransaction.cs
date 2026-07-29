using System;
using System.Collections.Generic;
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
[AutoDatabase]
internal sealed partial class RetryTransaction : IDatabaseAsync, ITransactionAsync
{
    // Note: async-only, exactly like RetryDatabase - retrying is inherently delay-ish.
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
        var op = new RecordedOp<TState, TResult>(state, operation);
        _ops.Add(op);
        return op.Proxy;
    }

    private Task ExecuteAsync<TState>(in TState state, AutoDatabaseAsyncOperation<TState> operation)
        where TState : struct
    {
        CheckNotExecuted();
        var op = new RecordedVoidOp<TState>(state, operation);
        _ops.Add(op);
        return op.Proxy;
    }

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
        void ForwardSuccess();
        void Fault(Exception ex);
        void Observe();
    }

    private sealed class RecordedOp<TState, TResult> : IRecordedOp
        where TState : struct
    {
        private readonly TState _state;
        private readonly AutoDatabaseAsyncOperation<TState, TResult> _operation;
        private readonly TaskCompletionSource<TResult> _proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<TResult>? _attempt;

        public RecordedOp(in TState state, AutoDatabaseAsyncOperation<TState, TResult> operation)
        {
            _state = state;
            _operation = operation;
        }

        public Task<TResult> Proxy => _proxy.Task;

        public void Replay(IDatabaseAsync inner) => _attempt = _operation(in _state, inner);

        public void ForwardSuccess()
        {
            var attempt = _attempt!;
            if (attempt.IsCanceled) _proxy.TrySetCanceled();
            else if (attempt.IsFaulted) _proxy.TrySetException(attempt.Exception!.InnerExceptions);
            else _proxy.TrySetResult(attempt.GetAwaiter().GetResult());
        }

        // observe the (faulted) inner attempt before faulting the durable proxy, so the discarded
        // per-attempt task doesn't surface as an unobserved exception
        public void Fault(Exception ex)
        {
            Observe();
            _proxy.TrySetException(ex);
        }

        public void Observe() => _ = _attempt?.Exception;
    }

    private sealed class RecordedVoidOp<TState> : IRecordedOp
        where TState : struct
    {
        private readonly TState _state;
        private readonly AutoDatabaseAsyncOperation<TState> _operation;
        private readonly TaskCompletionSource<bool> _proxy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _attempt;

        public RecordedVoidOp(in TState state, AutoDatabaseAsyncOperation<TState> operation)
        {
            _state = state;
            _operation = operation;
        }

        public Task Proxy => _proxy.Task;

        public void Replay(IDatabaseAsync inner) => _attempt = _operation(in _state, inner);

        public void ForwardSuccess()
        {
            var attempt = _attempt!;
            if (attempt.IsCanceled) _proxy.TrySetCanceled();
            else if (attempt.IsFaulted) _proxy.TrySetException(attempt.Exception!.InnerExceptions);
            else _proxy.TrySetResult(true);
        }

        // observe the (faulted) inner attempt before faulting the durable proxy, so the discarded
        // per-attempt task doesn't surface as an unobserved exception
        public void Fault(Exception ex)
        {
            Observe();
            _proxy.TrySetException(ex);
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

        public void ForwardSuccess()
        {
            if (_attempt is not null) Result.SetSatisfied(_attempt.WasSatisfied);
        }
    }

    // ---- hand-implemented members the generator deliberately skips ----------------------------------
    // (the Wait family, the synchronous IsConnected probe, and the streaming scans). Wait/IsConnected are
    // straight pass-throughs; scans cannot participate in a transaction.
    void IRedisAsync.Wait(Task task) => _source.Wait(task);
    T IRedisAsync.Wait<T>(Task<T> task) => _source.Wait<T>(task);
    void IRedisAsync.WaitAll(Task[] tasks) => _source.WaitAll(tasks);
    bool IRedisAsync.TryWait(Task task) => _source.TryWait(task);

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
