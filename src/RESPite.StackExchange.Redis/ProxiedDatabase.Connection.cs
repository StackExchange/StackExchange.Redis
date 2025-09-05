using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Connection and core methods
    public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public TimeSpan Ping(CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IBatch CreateBatch(object? asyncState = null) =>
        throw new NotImplementedException();

    public ITransaction CreateTransaction(object? asyncState = null) =>
        throw new NotImplementedException();

    // Key migration
    public Task KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Debug
    public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
