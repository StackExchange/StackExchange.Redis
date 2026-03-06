using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase(MultiGroupMultiplexer parent, int database, object? asyncState)
    : IDatabase
{
    public object? AsyncState => asyncState;
    public int Database => database < 0 ? GetActiveDatabase().Database : database;

    public IConnectionMultiplexer Multiplexer => parent;

    // for high DB numbers this might allocate even for null async-state scenarios; unavoidable for now
    private IDatabase GetActiveDatabase() => parent.Active.GetDatabase(database, asyncState);

    // Core methods
    public IBatch CreateBatch(object? asyncState = null)
        => GetActiveDatabase().CreateBatch(asyncState);

    public ITransaction CreateTransaction(object? asyncState = null)
        => GetActiveDatabase().CreateTransaction(asyncState);

    public void KeyMigrate(RedisKey key, System.Net.EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyMigrate(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);

    public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().DebugObject(key, flags);

    public System.Net.EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().IdentifyEndpoint(key, flags);

    public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().IsConnected(key, flags);

    public System.TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().Ping(flags);

    public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().Publish(channel, message, flags);

    public RedisResult Execute(string command, params object[] args)
        => GetActiveDatabase().Execute(command, args);

    public RedisResult Execute(string command, System.Collections.Generic.ICollection<object> args, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().Execute(command, args, flags);

    public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluate(script, keys, values, flags);

    public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluate(hash, keys, values, flags);

    public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluate(script, parameters, flags);

    public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluate(script, parameters, flags);

    public RedisResult ScriptEvaluateReadOnly(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluateReadOnly(script, keys, values, flags);

    public RedisResult ScriptEvaluateReadOnly(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().ScriptEvaluateReadOnly(hash, keys, values, flags);

    public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().LockExtend(key, value, expiry, flags);

    public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().LockQuery(key, flags);

    public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().LockRelease(key, value, flags);

    public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().LockTake(key, value, expiry, flags);

    public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().Sort(key, skip, take, order, sortType, by, get, flags);

    public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortAndStore(destination, key, skip, take, order, sortType, by, get, flags);

    // IRedisAsync methods
    public bool TryWait(System.Threading.Tasks.Task task)
        => GetActiveDatabase().TryWait(task);

    public void Wait(System.Threading.Tasks.Task task)
        => GetActiveDatabase().Wait(task);

    public T Wait<T>(System.Threading.Tasks.Task<T> task)
        => GetActiveDatabase().Wait(task);

    public void WaitAll(params System.Threading.Tasks.Task[] tasks)
        => GetActiveDatabase().WaitAll(tasks);
}
