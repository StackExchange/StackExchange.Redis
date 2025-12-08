using System;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Async methods - Core operations
    public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().DebugObjectAsync(key, flags);

    public Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().IdentifyEndpointAsync(key, flags);

    public Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyMigrateAsync(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);

    public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        => GetDatabase().PingAsync(flags);

    public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
        => GetDatabase().PublishAsync(channel, message, flags);

    public Task<RedisResult> ExecuteAsync(string command, params object[] args)
        => GetDatabase().ExecuteAsync(command, args);

    public Task<RedisResult> ExecuteAsync(string command, System.Collections.Generic.ICollection<object>? args, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ExecuteAsync(command, args, flags);

    public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateAsync(script, keys, values, flags);

    public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateAsync(hash, keys, values, flags);

    public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateAsync(script, parameters, flags);

    public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateAsync(script, parameters, flags);

    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateReadOnlyAsync(script, keys, values, flags);

    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().ScriptEvaluateReadOnlyAsync(hash, keys, values, flags);

    public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockExtendAsync(key, value, expiry, flags);

    public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockQueryAsync(key, flags);

    public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockReleaseAsync(key, value, flags);

    public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
        => GetDatabase().LockTakeAsync(key, value, expiry, flags);

    public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SortAsync(key, skip, take, order, sortType, by, get, flags);

    public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().SortAndStoreAsync(destination, key, skip, take, order, sortType, by, get, flags);
}
