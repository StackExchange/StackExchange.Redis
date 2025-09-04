using System.Net;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

/// <summary>
/// Implements IServer on top of a <see cref="Node"/>, which represents a fixed single connection
/// to a single redis instance. The connection exposed is the "interactive" connection.
/// </summary>
internal sealed class NodeServer(Node node) : IServer
{
    // deliberately not caching this - if the connection changes, we want to know about it
    internal ref readonly RespContext Context => ref node.Context;

    public IConnectionMultiplexer Multiplexer => node.Multiplexer;
    public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public bool TryWait(Task task) => node.Multiplexer.TryWait(task);

    public void Wait(Task task) => node.Multiplexer.Wait(task);

    public T Wait<T>(Task<T> task) => node.Multiplexer.Wait(task);

    public void WaitAll(params Task[] tasks) => node.Multiplexer.WaitAll(tasks);

    public TimeSpan Ping(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public ClusterConfiguration? ClusterConfiguration { get; }
    public EndPoint EndPoint => node.EndPoint;
    public RedisFeatures Features => new(Version);
    public bool IsConnected => node.IsConnected;
    public RedisProtocol Protocol { get; }
    public bool IsSlave { get; }
    public bool IsReplica { get; }
    public bool AllowSlaveWrites { get; set; }
    public bool AllowReplicaWrites { get; set; }
    public ServerType ServerType { get; }
    public Version Version => node.Version;
    public int DatabaseCount { get; }
    public void ClientKill(EndPoint endpoint, CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ClientKillAsync(EndPoint endpoint, CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public long ClientKill(
        long? id = null,
        ClientType? clientType = null,
        EndPoint? endpoint = null,
        bool skipMe = true,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> ClientKillAsync(
        long? id = null,
        ClientType? clientType = null,
        EndPoint? endpoint = null,
        bool skipMe = true,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long ClientKill(ClientKillFilter filter, CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> ClientKillAsync(
        ClientKillFilter filter,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public ClientInfo[] ClientList(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<ClientInfo[]> ClientListAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public ClusterConfiguration? ClusterNodes(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<ClusterConfiguration?> ClusterNodesAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string? ClusterNodesRaw(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string?> ClusterNodesRawAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[] ConfigGet(
        RedisValue pattern = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[]> ConfigGetAsync(
        RedisValue pattern = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void ConfigResetStatistics(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ConfigResetStatisticsAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void ConfigRewrite(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ConfigRewriteAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void ConfigSet(
        RedisValue setting,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ConfigSetAsync(
        RedisValue setting,
        RedisValue value,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public long CommandCount(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> CommandCountAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisKey[] CommandGetKeys(
        RedisValue[] command,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisKey[]> CommandGetKeysAsync(
        RedisValue[] command,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string[] CommandList(
        RedisValue? moduleName = null,
        RedisValue? category = null,
        RedisValue? pattern = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<string[]> CommandListAsync(
        RedisValue? moduleName = null,
        RedisValue? category = null,
        RedisValue? pattern = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long DatabaseSize(
        int database = -1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> DatabaseSizeAsync(
        int database = -1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisValue Echo(
        RedisValue message,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisValue> EchoAsync(
        RedisValue message,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisResult Execute(string command, params object[] args) => throw new NotImplementedException();

    public Task<RedisResult> ExecuteAsync(string command, params object[] args) => throw new NotImplementedException();

    public RedisResult Execute(
        string command,
        ICollection<object> args,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisResult> ExecuteAsync(
        string command,
        ICollection<object> args,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void FlushAllDatabases(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task FlushAllDatabasesAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void FlushDatabase(
        int database = -1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task FlushDatabaseAsync(
        int database = -1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public ServerCounters GetCounters() => throw new NotImplementedException();

    public IGrouping<string, KeyValuePair<string, string>>[] Info(
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string? InfoRaw(
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string?> InfoRawAsync(
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public IEnumerable<RedisKey> Keys(
        int database,
        RedisValue pattern,
        int pageSize,
        CommandFlags flags) => throw new NotImplementedException();

    public IEnumerable<RedisKey> Keys(
        int database = -1,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<RedisKey> KeysAsync(
        int database = -1,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public DateTime LastSave(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<DateTime> LastSaveAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void MakeMaster(
        ReplicationChangeOptions options,
        TextWriter? log = null) => throw new NotImplementedException();

    public Task MakePrimaryAsync(
        ReplicationChangeOptions options,
        TextWriter? log = null) => throw new NotImplementedException();

    public Role Role(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<Role> RoleAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void Save(
        SaveType type,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task SaveAsync(
        SaveType type,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public bool ScriptExists(
        string script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<bool> ScriptExistsAsync(
        string script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public bool ScriptExists(
        byte[] sha1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<bool> ScriptExistsAsync(
        byte[] sha1,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void ScriptFlush(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ScriptFlushAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public byte[] ScriptLoad(
        string script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<byte[]> ScriptLoadAsync(
        string script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public LoadedLuaScript ScriptLoad(
        LuaScript script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<LoadedLuaScript> ScriptLoadAsync(
        LuaScript script,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void Shutdown(
        ShutdownMode shutdownMode = ShutdownMode.Default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void SlaveOf(
        EndPoint master,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task SlaveOfAsync(
        EndPoint master,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void ReplicaOf(
        EndPoint master,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task ReplicaOfAsync(
        EndPoint master,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public CommandTrace[] SlowlogGet(
        int count = 0,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<CommandTrace[]> SlowlogGetAsync(
        int count = 0,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void SlowlogReset(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task SlowlogResetAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisChannel[] SubscriptionChannels(
        RedisChannel pattern = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisChannel[]> SubscriptionChannelsAsync(
        RedisChannel pattern = default,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public long SubscriptionPatternCount(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> SubscriptionPatternCountAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public long SubscriptionSubscriberCount(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> SubscriptionSubscriberCountAsync(
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void SwapDatabases(
        int first,
        int second,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task SwapDatabasesAsync(
        int first,
        int second,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public DateTime Time(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<DateTime> TimeAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string LatencyDoctor(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string> LatencyDoctorAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public long LatencyReset(
        string[]? eventNames = null,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> LatencyResetAsync(
        string[]? eventNames = null,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public LatencyHistoryEntry[] LatencyHistory(
        string eventName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<LatencyHistoryEntry[]> LatencyHistoryAsync(
        string eventName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public LatencyLatestEntry[] LatencyLatest(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<LatencyLatestEntry[]> LatencyLatestAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string MemoryDoctor(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string> MemoryDoctorAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void MemoryPurge(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task MemoryPurgeAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisResult MemoryStats(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisResult> MemoryStatsAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string? MemoryAllocatorStats(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string?> MemoryAllocatorStatsAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public EndPoint? SentinelGetMasterAddressByName(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<EndPoint?> SentinelGetMasterAddressByNameAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public EndPoint[] SentinelGetSentinelAddresses(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<EndPoint[]> SentinelGetSentinelAddressesAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public EndPoint[] SentinelGetReplicaAddresses(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<EndPoint[]> SentinelGetReplicaAddressesAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[] SentinelMaster(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[]> SentinelMasterAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[][] SentinelMasters(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[][]> SentinelMastersAsync(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[][] SentinelSlaves(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[][]> SentinelSlavesAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[][] SentinelReplicas(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[][]> SentinelReplicasAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public void SentinelFailover(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task SentinelFailoverAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public KeyValuePair<string, string>[][] SentinelSentinels(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<KeyValuePair<string, string>[][]> SentinelSentinelsAsync(
        string serviceName,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();
}
