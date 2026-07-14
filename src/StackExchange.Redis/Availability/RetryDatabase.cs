using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace StackExchange.Redis.Availability;

[AutoDatabase]
internal partial class RetryDatabase : IDatabase, IRedisArgsMutator
{
    private readonly IDatabase _inner;
    private readonly int _maxRetryCount;
    public RetryDatabase(IDatabase inner, int maxRetryCount)
    {
        _inner = inner;
        _maxRetryCount = maxRetryCount;
    }

    public int Database => _inner.Database;
    public IConnectionMultiplexer Multiplexer => _inner.Multiplexer;

    // the generated explicit interface implementations funnel every call through these two
    // overloads: the arguments are captured in a generated state struct and replayed against
    // the inner database via a cacheable static projection (no per-call closure). Retry/failover
    // policy will live here in due course; for now it is a straight pass-through.
    private TResult Execute<TState, TResult>(TState state, Func<TState, IDatabase, TResult> operation)
        where TState : struct, IRedisArgs
    {
        state.Map(this);
        int i = 0;
        TResult result;
        while (true)
        {
            try
            {
                result = operation(state, _inner);
                break;
            }
            catch (Exception ex) when (++i < _maxRetryCount)
            {
                // we can give it another attempt
                Debug.WriteLine(ex.Message);
            }
        }
        return this.UnMap(state, result);
    }

    private void Execute<TState>(TState state, Action<TState, IDatabase> operation)
        where TState : struct, IRedisArgs
        => operation(state, _inner);

    // async counterparts (Task<T> / Task); these get their own retry/failover policy in due course.
    private Task<TResult> ExecuteAsync<TState, TResult>(TState state, Func<TState, IDatabase, Task<TResult>> operation)
        where TState : struct, IRedisArgs
        => operation(state, _inner);

    private Task ExecuteAsync<TState>(TState state, Func<TState, IDatabase, Task> operation)
        where TState : struct, IRedisArgs
        => operation(state, _inner);

    void IRedisAsync.Wait(Task task) => _inner.Wait(task);
    T IRedisAsync.Wait<T>(Task<T> task) => _inner.Wait<T>(task);
    void IRedisAsync.WaitAll(Task[] tasks) => _inner.WaitAll(tasks);
    bool IRedisAsync.TryWait(Task task) => _inner.TryWait(task);

    // Methods the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod): the Wait
    // family and the streaming IEnumerable/IAsyncEnumerable scans don't fit the capture-and-replay
    // shape, so they are implemented by hand.
    System.Collections.Generic.IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<RedisValue> IDatabase.HashScanNoValues(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<RedisValue> IDatabase.VectorSetRangeEnumerate(RedisKey key, RedisValue start, RedisValue end, long count, Exclude exclude, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) => throw new NotImplementedException();
    System.Collections.Generic.IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags) => throw new NotImplementedException();

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
