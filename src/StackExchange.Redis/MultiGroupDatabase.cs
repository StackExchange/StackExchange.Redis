using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase : IDatabase
{
    private readonly MultiGroupMultiplexer _parent;
    private readonly int _database;
    private readonly object? _asyncState;

    public MultiGroupDatabase(MultiGroupMultiplexer parent, int database, object? asyncState)
    {
        _parent = parent;
        _database = database;
        _asyncState = asyncState;
    }

    public object? AsyncState => _asyncState;
    public int Database => _database < 0 ? GetDatabase().Database : _database;

    public IConnectionMultiplexer Multiplexer => _parent;

    // for high DB numbers this might allocate even for null async-state scenarios; unavoidable for now
    private IDatabase GetDatabase() => _parent.Active.GetDatabase(_database, _asyncState);

    // Core methods
    public IBatch CreateBatch(object? asyncState = null)
        => GetDatabase().CreateBatch(asyncState);

    public ITransaction CreateTransaction(object? asyncState = null)
        => GetDatabase().CreateTransaction(asyncState);

    public void KeyMigrate(RedisKey key, System.Net.EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyMigrate(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);

    public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().DebugObject(key, flags);

    public System.Net.EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().IdentifyEndpoint(key, flags);

    public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().IsConnected(key, flags);

    public System.TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        => GetDatabase().Ping(flags);

    public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        => GetDatabase().Publish(channel, message, flags);

    public RedisResult Execute(string command, params object[] args)
        => GetDatabase().Execute(command, args);

    public RedisResult Execute(string command, System.Collections.Generic.ICollection<object> args, CommandFlags flags = CommandFlags.None)
        => GetDatabase().Execute(command, args, flags);

    public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluate(script, keys, values, flags);

    public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluate(hash, keys, values, flags);

    public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluate(script, parameters, flags);

    public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluate(script, parameters, flags);

    public RedisResult ScriptEvaluateReadOnly(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateReadOnly(script, keys, values, flags);

    public RedisResult ScriptEvaluateReadOnly(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateReadOnly(hash, keys, values, flags);

    public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockExtend(key, value, expiry, flags);

    public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockQuery(key, flags);

    public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockRelease(key, value, flags);

    public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockTake(key, value, expiry, flags);

    public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().Sort(key, skip, take, order, sortType, by, get, flags);

    public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SortAndStore(destination, key, skip, take, order, sortType, by, get, flags);

    // IRedisAsync methods
    public bool TryWait(System.Threading.Tasks.Task task)
        => GetDatabase().TryWait(task);

    public void Wait(System.Threading.Tasks.Task task)
        => GetDatabase().Wait(task);

    public T Wait<T>(System.Threading.Tasks.Task<T> task)
        => GetDatabase().Wait(task);

    public void WaitAll(params System.Threading.Tasks.Task[] tasks)
        => GetDatabase().WaitAll(tasks);
}
