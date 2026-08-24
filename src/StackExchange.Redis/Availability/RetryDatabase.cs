using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.Availability;

[AutoDatabase]
internal partial class RetryDatabase : IDatabaseAsync, IInternalDatabaseAsync
    // IRedisArgsMutator <==== if we ever want to support key-mapping
{
    // Note: we very deliberately do not include synchronous support for retry; it is inherently delay-ish

    // Note that only transient faults result in retries; this is defined by the RetryPolicy, along with
    // understanding the category. The default RetryPolicy works the same as the default CircuitBreaker.
    DatabaseFeatureFlags IInternalDatabaseAsync.GetFeatures(out string name)
        => _inner.GetFeatures(out name) | DatabaseFeatureFlags.Retry;

    // never: we refuse to wrap a database that carries one (see Validate)
    object? IInternalDatabaseAsync.AsyncState => null;

    /// <inheritdoc/>
    public override string ToString() => this.BuildString();

    private readonly IDatabaseAsync _inner;
    private readonly RetryController _controller;

    internal RetryPolicy Policy => _controller.Policy;

    public CancellationToken GetNextFailover()
        => _controller.TracksFailover ? _inner.GetNextFailover() : CancellationToken.None;

    public RetryDatabase(IDatabaseAsync inner, RetryPolicy policy)
        : this(inner, policy, Validate(inner))
    {
    }

    private static DatabaseFeatureFlags Validate(IDatabaseAsync inner)
    {
        // cannot nest retry, and cannot issue retries *inside* a batch/transaction
        var features = inner.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction | DatabaseFeatureFlags.Retry);

        // async-state is stamped onto the task that a *single* attempt produces; a retrying database hands
        // back its own durable task that spans however many attempts it takes, so it cannot preserve the
        // state. Refuse rather than dropping it silently. See also CreateTransaction, below.
        if (inner.GetAsyncState() is not null) ThrowAsyncState();
        return features;
    }

    internal static void ThrowAsyncState() => throw new InvalidOperationException(
        "Retrying databases do not support asyncState; the tasks they hand back are not the tasks that were sent to the server.");

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
        // as per the constructor: the per-operation tasks handed out at build time are durable proxies
        // that outlive any single attempt, so they cannot carry a per-attempt async-state
        if (asyncState is not null) ThrowAsyncState();

        // the inner database creates the "real" (one-shot) transactions we replay against each attempt
        return new RetryTransaction(_inner, _controller);
    }

    public int Database => _inner.Database;

    public IConnectionMultiplexer Multiplexer => _inner.Multiplexer;

    // the generated explicit interface implementations funnel every call through these two
    // overloads: the arguments are captured in a generated state struct and replayed against
    // the inner database via a cacheable static projection (no per-call closure). Retry/failover
    // policy will live here in due course; for now it is a straight pass-through.

    // async counterparts (Task<T> / Task); these get their own retry/failover policy in due course.
    // note: the state cannot be taken by `in` here (async methods forbid by-ref parameters);
    // only the projection takes it by readonly-ref, avoiding a copy per attempt
    private async Task<TResult> ExecuteAsync<TState, TResult>(TState state, AutoDatabaseAsyncOperation<TState, TResult> operation)
        where TState : struct
    {
        /* key mapping, not used currently
         * state.MapInPlace(this); */

        int attempt = 0;
        TResult result;
        // note we need to capture this *before* the attempt - otherwise the failover could happen
        // between the failed attempt and fetching this, and we'd miss it
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                result = await operation(in state, _inner).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }
        }
        /* key mapping, not used currently
        // post-process results outside the loop
        return this.UnMap(state, result);*/
        return result;
    }

    private async Task ExecuteAsync<TState>(TState state, AutoDatabaseAsyncOperation<TState> operation)
        where TState : struct
    {
        /* key mapping, not used currently
         * state.MapInPlace(this); */

        int attempt = 0;
        // note we need to capture this *before* the attempt - otherwise the failover could happen
        // between the failed attempt and fetching this, and we'd miss it
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                await operation(in state, _inner).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }
        }
        // (nothing to post-process)
    }

    // forwarding is not using: these decorators must implement the interface in full, and the
    // implementation cannot be dropped while the interface declares it
    #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
    void IRedisAsync.Wait(Task task) => _inner.Wait(task);
    T IRedisAsync.Wait<T>(Task<T> task) => _inner.Wait<T>(task);
    void IRedisAsync.WaitAll(Task[] tasks) => _inner.WaitAll(tasks);
    bool IRedisAsync.TryWait(Task task) => _inner.TryWait(task);
    #pragma warning restore SER308

    // Methods the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod): the Wait
    // family, the synchronous IsConnected probe, and the streaming IEnumerable/IAsyncEnumerable scans
    // don't fit the capture-and-replay shape, so they are implemented by hand.
    // IsConnected is a straight pass-through: it is a cheap status check, not a server round-trip to retry.
    bool IDatabaseAsync.IsConnected(RedisKey key, CommandFlags flags) => _inner.IsConnected(key, flags);

    // routing lookup, not a replayable server command - forward straight through (no retry)
    Task<System.Net.EndPoint?> IDatabaseAsync.IdentifyEndpointAsync(RedisKey key, CommandFlags flags) => _inner.IdentifyEndpointAsync(key, flags);

    // Scans are streaming/cursored, so they can't be captured-and-replayed as a single unit; rather than
    // failing outright we forward straight to the inner database - giving up retry, but keeping the scan working.
    System.Collections.Generic.IAsyncEnumerable<HashEntry> IDatabaseAsync.HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => _inner.HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.HashScanNoValuesAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => _inner.HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => _inner.SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);
    System.Collections.Generic.IAsyncEnumerable<RedisValue> IDatabaseAsync.VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start, RedisValue end, long count, Exclude exclude, CommandFlags flags) => _inner.VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
    System.Collections.Generic.IAsyncEnumerable<SortedSetEntry> IDatabaseAsync.SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => _inner.SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    /* optional: key mapping
    RedisKey IRedisArgsMutator.Map(RedisKey key) => key;

    RedisChannel IRedisArgsMutator.Map(RedisChannel channel) => channel;

    RedisKey IRedisArgsMutator.UnMap(RedisKey key) => key;
    RedisChannel IRedisArgsMutator.UnMap(RedisChannel channel) => channel;
    */
}
