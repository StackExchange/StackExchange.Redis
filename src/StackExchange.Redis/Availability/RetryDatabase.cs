using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.Availability;

[AutoDatabase]
internal partial class RetryDatabase : IDatabaseAsync, IRedisArgsMutator, IInternalDatabaseAsync
{
    // Note: we very deliberately do not include synchronous support for retry; it is inherently delay-ish

    // Note that only transient faults result in retries; this is defined by the RetryPolicy, along with
    // understanding the category. The default RetryPolicy works the same as the default CircuitBreaker.
    DatabaseFeatureFlags IInternalDatabaseAsync.GetFeatures(out string name)
        => _inner.GetFeatures(out name) | DatabaseFeatureFlags.Retry;

    /// <inheritdoc/>
    public override string ToString() => this.BuildString();

    private readonly IDatabaseAsync _inner;
    private readonly int _maxBeforeFailover, _maxAttempts, _delayMillis, _jitterMillis, _failoverMillis;
    private readonly RetryPolicy _policy;

    public CancellationToken GetNextFailover()
        => _maxAttempts > 1 & _maxBeforeFailover < _maxAttempts ? _inner.GetNextFailover() : CancellationToken.None;

    public RetryDatabase(IDatabaseAsync inner, RetryPolicy policy)
    {
        // cannot nest retry, and cannot issue retries *inside* a batch/transaction
        var features = inner.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction | DatabaseFeatureFlags.Retry);

        _policy = policy;

        // capture config locally rather than constant cross-object lookups (plus: mutability)
        _maxBeforeFailover = (features & DatabaseFeatureFlags.Failover) == 0 ? int.MaxValue : policy.MaxAttemptsBeforeFailover;
        _maxAttempts = policy.MaxAttempts;
        if (_maxBeforeFailover == _maxAttempts) _maxBeforeFailover = int.MaxValue; // then we'll never look

        // guard the failover threshold: values < 1 can never be hit by the loop counter (which starts at 1),
        // so they would *silently* disable failover rather than erroring; validate the raw policy value
        if (policy.MaxAttemptsBeforeFailover < 1) throw new ArgumentOutOfRangeException(nameof(policy.MaxAttemptsBeforeFailover));
        _delayMillis = policy.DelayMilliseconds;
        _failoverMillis = policy.FailoverMilliseconds;
        _jitterMillis = policy.JitterMilliseconds;
        if (_delayMillis < 0) throw new ArgumentOutOfRangeException(nameof(policy.RetryDelay));
        if (_jitterMillis < 0) throw new ArgumentOutOfRangeException(nameof(policy.JitterMax));
        if (_failoverMillis < 0) throw new ArgumentOutOfRangeException(nameof(policy.FailoverDelay));
        _inner = inner;
    }

    public int Database => _inner is IDatabase db ? db.Database : -1;

    public IConnectionMultiplexer Multiplexer => _inner.Multiplexer;

    // the generated explicit interface implementations funnel every call through these two
    // overloads: the arguments are captured in a generated state struct and replayed against
    // the inner database via a cacheable static projection (no per-call closure). Retry/failover
    // policy will live here in due course; for now it is a straight pass-through.

    // async counterparts (Task<T> / Task); these get their own retry/failover policy in due course.
    private async Task<TResult> ExecuteAsync<TState, TResult>(TState state, Func<TState, IDatabaseAsync, Task<TResult>> operation)
        where TState : struct, IRedisArgs
    {
        state.Map(this);

        int attempt = 0;
        TResult result;
        // note we need to capture this *before* the attempt - otherwise the failover could happen
        // between the failed attempt and fetching this, and we'd miss it
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                result = await operation(state, _inner).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (CanRetry(++attempt, ex, state.Flags, ref failover, out var delay))
            {
                await FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }
        }
        // post-process results outside the loop
        return this.UnMap(state, result);
    }

    private async Task ExecuteAsync<TState>(TState state, Func<TState, IDatabaseAsync, Task> operation)
        where TState : struct, IRedisArgs
    {
        state.Map(this);

        int attempt = 0;
        // note we need to capture this *before* the attempt - otherwise the failover could happen
        // between the failed attempt and fetching this, and we'd miss it
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                await operation(state, _inner).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (CanRetry(++attempt, ex, state.Flags, ref failover, out var delay))
            {
                await FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }
        }
        // (nothing to post-process)
    }

    private bool CanRetry(
        int attempt,
        Exception fault,
        CommandFlags flags,
        ref CancellationToken failover,
        out CancellationToken delay)
    {
        delay = CancellationToken.None;
        if (attempt >= _maxAttempts)
        {
            // all used up
            return false;
        }

        // ask the retry policy for advice, and mask off the bits we know about
        FaultContext ctx = new(fault, flags);
        var policy = _policy.CanRetry(ctx) &
                     (RetryPolicy.RetryPolicyResult.FailoverServer | RetryPolicy.RetryPolicyResult.SameServer);
        if (policy is 0)
        {
            // retry policy says: nope
            return false;
        }

        if (policy is RetryPolicy.RetryPolicyResult.FailoverServer)
        {
            // we can *only* retry on a different server; is failover available?
            delay = failover;
            failover = CancellationToken.None; // only failover once
            return delay.CanBeCanceled;
        }

        if (attempt == _maxBeforeFailover)
        {
            // by count, we should really switch over to the failover now; is failover available *and* are we allowed?
            delay = failover;
            failover = CancellationToken.None; // only failover once
            return delay.CanBeCanceled & (policy & RetryPolicy.RetryPolicyResult.FailoverServer) != 0;
        }

        // can we pause and retry on the same server?
        return (policy & RetryPolicy.RetryPolicyResult.SameServer) != 0;
    }

    private Task FailoverOrDelayAsync(CancellationToken delay)
    {
        if (delay.CanBeCanceled)
        {
            return AwaitFailover(delay);
        }

        // this is just a routine wait between operations; await delay+jitter
        return Task.Delay(_delayMillis + ServerSelectionStrategy.SharedRandom.Next(_jitterMillis), CancellationToken.None);
    }
    private async Task AwaitFailover(CancellationToken failover)
    {
        if (!failover.IsCancellationRequested)
        {
            // failover hasn't happened yet; allow up to "delay" time for that
            try
            {
                await Task.Delay(_failoverMillis, failover).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (failover.IsCancellationRequested)
            {
                // we observed a failover, nice!
            }
        }

        // either way, we need to add jitter onto that; we can't add in the original delay, because if the failover
        // happened before the timeout+jitter, all the awaiters would stampede
        await Task.Delay(ServerSelectionStrategy.SharedRandom.Next(_jitterMillis), CancellationToken.None).ConfigureAwait(false);
    }

    void IRedisAsync.Wait(Task task) => _inner.Wait(task);
    T IRedisAsync.Wait<T>(Task<T> task) => _inner.Wait<T>(task);
    void IRedisAsync.WaitAll(Task[] tasks) => _inner.WaitAll(tasks);
    bool IRedisAsync.TryWait(Task task) => _inner.TryWait(task);

    // Methods the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod): the Wait
    // family, the synchronous IsConnected probe, and the streaming IEnumerable/IAsyncEnumerable scans
    // don't fit the capture-and-replay shape, so they are implemented by hand.
    // IsConnected is a straight pass-through: it is a cheap status check, not a server round-trip to retry.
    bool IDatabaseAsync.IsConnected(RedisKey key, CommandFlags flags) => _inner.IsConnected(key, flags);

    System.Collections.Generic.IAsyncEnumerable<HashEntry> IDatabaseAsync.HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.HashScanNoValuesAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start, RedisValue end, long count, Exclude exclude, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IAsyncEnumerable<SortedSetEntry> IDatabaseAsync.SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();

    RedisKey IRedisArgsMutator.Map(RedisKey key) => key;

    RedisChannel IRedisArgsMutator.Map(RedisChannel channel) => channel;

    RedisKey IRedisArgsMutator.UnMap(RedisKey key) => key;
    RedisChannel IRedisArgsMutator.UnMap(RedisChannel channel) => channel;
}
