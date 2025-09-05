using System.Net;
using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Connection and core methods
    public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) =>
        Context(flags).Send("ping"u8, DateTime.UtcNow, PingParser.Default).AsTask();

    public TimeSpan Ping(CommandFlags flags = CommandFlags.None) =>
        Context(flags).Send("ping"u8, DateTime.UtcNow, PingParser.Default).Wait(SyncTimeout);

    private sealed class PingParser : IRespParser<DateTime, TimeSpan>
    {
        public static readonly PingParser Default = new();
        private PingParser() { }
        public TimeSpan Parse(in DateTime state, ref RespReader reader) => DateTime.UtcNow - state;
    }
    public Task<EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IBatch CreateBatch(object? asyncState = null) =>
        throw new NotImplementedException();

    public ITransaction CreateTransaction(object? asyncState = null) =>
        throw new NotImplementedException();

    // Key migration
    public Task KeyMigrateAsync(
        RedisKey key,
        EndPoint toServer,
        int toDatabase = 0,
        int timeoutMilliseconds = 0,
        MigrateOptions migrateOptions = MigrateOptions.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void KeyMigrate(
        RedisKey key,
        EndPoint toServer,
        int toDatabase = 0,
        int timeoutMilliseconds = 0,
        MigrateOptions migrateOptions = MigrateOptions.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Debug
    [RespCommand("debug")]
    public partial RedisValue DebugObject([RespPrefix("object")] RedisKey key, CommandFlags flags = CommandFlags.None);
}
