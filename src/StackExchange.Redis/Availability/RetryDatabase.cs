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
    private readonly RetryController _controller;

    public CancellationToken GetNextFailover()
        => _controller.TracksFailover ? _inner.GetNextFailover() : CancellationToken.None;

    public RetryDatabase(IDatabaseAsync inner, RetryPolicy policy)
        // cannot nest retry, and cannot issue retries *inside* a batch/transaction
        : this(inner, policy, inner.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction | DatabaseFeatureFlags.Retry))
    {
    }

    // test-only: supply the inner database's feature set directly (in particular whether failover is
    // available), instead of probing a live inner - so that failover behaviour can be exercised over a
    // null inner without a full IDatabaseAsync double.
    internal RetryDatabase(IDatabaseAsync inner, RetryPolicy policy, DatabaseFeatureFlags features)
    {
        _controller = new RetryController(policy, features);
        _inner = inner;
    }

    ITransactionAsync IDatabaseAsync.CreateTransaction(object? asyncState)
    {
        // the underlying database must be able to create a "real" transaction for us to replay against;
        // every first-party async database is also an IDatabase, so this only bites exotic custom doubles
        if (_inner is not IDatabase source)
        {
            throw new NotSupportedException("The underlying database does not support transactions");
        }
        return new RetryTransaction(source, _controller, asyncState);
    }

    public int Database => _inner.Database;

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
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
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
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }
        }
        // (nothing to post-process)
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
