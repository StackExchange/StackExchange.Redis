using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace StackExchange.Redis.Availability;

[Conditional("DEBUG")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
internal sealed class AutoDatabaseAttribute : Attribute
{
}

internal interface IRedisArgs
{
    void ApplyKeys(Func<RedisKey, RedisKey> selector);
    CommandFlags Flags { get; set; }
}

[AutoDatabase]
internal partial class RetryDatabase : IDatabase
{
    private readonly IDatabase _inner;

    public RetryDatabase(IDatabase inner) => _inner = inner;

    public int Database => _inner.Database;
    public IConnectionMultiplexer Multiplexer => _inner.Multiplexer;

    // the generated explicit interface implementations funnel every call through these two
    // overloads: the arguments are captured in a generated state struct and replayed against
    // the inner database via a cacheable static projection (no per-call closure). Retry/failover
    // policy will live here in due course; for now it is a straight pass-through.
    private TResult Execute<TState, TResult>(TState state, Func<TState, IDatabase, TResult> operation)
        => operation(state, _inner);

    private void Execute<TState>(TState state, Action<TState, IDatabase> operation)
        => operation(state, _inner);

    // async counterparts (Task<T> / Task); these get their own retry/failover policy in due course.
    private Task<TResult> ExecuteAsync<TState, TResult>(TState state, Func<TState, IDatabase, Task<TResult>> operation)
        => operation(state, _inner);

    private Task ExecuteAsync<TState>(TState state, Func<TState, IDatabase, Task> operation)
        => operation(state, _inner);
}
