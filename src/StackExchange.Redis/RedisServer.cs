using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RESPite.Messages;

namespace StackExchange.Redis
{
    internal sealed partial class RedisServer : RedisBase, IServer
    {
        /// <inheritdoc/>
        public RespServerContext Context => new(GetContext());

        /// <inheritdoc/>
        RespContext IRespTarget.Context => GetContext();
        // Several server commands are a single RedisCommand covering wildly different verbs (CLIENT, CLUSTER,
        // CONFIG, SCRIPT, SLOWLOG, LATENCY, MEMORY), so the whole-command default has to assume the most
        // side-effecting subcommand. Where we know the subcommand we can be accurate instead. Everything here
        // is inherently node-scoped: the answer (or the effect) belongs to the server we asked.
        private const CommandFlags NodeLocalRead = CommandFlags.CommandRetryReadOnly | Message.CommandServerSpecific,
                                   NodeLocalAdmin = CommandFlags.CommandRetryServerAdmin | Message.CommandServerSpecific;

        private readonly ServerEndPoint server;

        internal RedisServer(ServerEndPoint server, object? asyncState) : base(server.Multiplexer, asyncState)
        {
            this.server = server; // definitely can't be null because .Multiplexer in base call
        }

        private RespContext? _context;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>No database.</b> A server is not database-scoped, so the context carries <c>-1</c> and a
        /// command that does need one fails loudly at construction ("A database is required for ...")
        /// rather than silently running against database 0. <c>IServer</c>'s own database-scoped members
        /// take the number explicitly, which is the model a server group should follow.
        /// </para>
        /// <para>
        /// <b>No client-side cache</b>, and not as an oversight: invalidation is reported by key, and
        /// server commands are keyless, so nothing could ever invalidate a cached <c>INFO</c>. The cache
        /// already refuses keyless requests for exactly that reason - attaching it here would be a no-op
        /// dressed as a feature.
        /// </para>
        /// <para>
        /// <b>No executor of its own is needed.</b> <c>ExecuteAsync</c> below injects this endpoint into
        /// every message, so a frame routed through it is pinned to this server for free - the same
        /// inheritance that makes a batch's context queue rather than send.
        /// </para>
        /// </remarks>
        protected override RespContext GetContext()
            => _context ??= new RespContext(
                multiplexer.CommandMap,
                database: -1,
                serverType: server.ServerType)
                .WithExecutor(ServerExecutor())
                .WithScriptCache(multiplexer.ScriptCache)
                .WithServices(new ServerFeatureProbe(this));

        /// <summary>What finally writes this server's commands.</summary>
        /// <remarks>
        /// <b>This core's socket for this endpoint under the engine flag</b>, so that a server command and
        /// the database commands it is meant to describe are ordered by one connection rather than racing
        /// two - see design notes D2.4, and 9b-xi for what that was costing.
        /// <para>
        /// <c>NewCore</c> rather than <c>NewCoreIfCreated</c>, and the difference is load-bearing because
        /// the context this feeds is MEMOISED. Asking "if created" meant an <c>IServer</c> touched before
        /// anything else - which <c>GetServer(...).Ping()</c> is, in test after test - got the shipped
        /// executor and kept it for the life of the object, so half the point of D2.4 came and went
        /// according to call order. The flag check is what keeps the shipped path from creating a core it
        /// does not want.
        /// </para>
        /// </remarks>
        private RespExecutorBase ServerExecutor()
            => ConnectionMultiplexer.NewCoreEngine
                ? multiplexer.NewCore.ServerExecutor(server.EndPoint)
                : new RespMessageExecutor(this, -1);

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Overridden so that a server's PING goes where the server's other commands go.</b>
        /// <see cref="RedisBase.Ping"/> builds a <c>Message</c> and sends it down the shipped pipeline,
        /// which under the engine flag is a different socket from the one this <c>IServer</c> is otherwise
        /// on - so "ping the server to bring it up", which is what half the suite opens with, brought up
        /// the wrong connection and left the interesting one still un-dialled. That is visible as a
        /// connection count: <c>MovedUnitTests</c> counts sockets before and after a redirect.
        /// </remarks>
        public override TimeSpan Ping(CommandFlags flags = CommandFlags.None) => Wait(Context.PingMeasureAsync(flags));

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="Ping" path="/remarks"/></remarks>
        public override Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
            => Context.PingMeasureAsync(flags).AsTask(asyncState, flags);

        /// <summary>The non-null spelling these members promise.</summary>
        /// <param name="pending">The reply, which the server may omit.</param>
        /// <remarks>
        /// The shipped members pass <c>defaultValue: string.Empty</c> for exactly this: a server with
        /// nothing to say answers null, and <c>IServer</c> declares a non-null string. Kept rather than
        /// tightened, because callers have been reading <c>.Length</c> on it for years.
        /// </remarks>
        private static async ValueTask<string> OrEmpty(ValueTask<string?> pending)
            => await pending.ConfigureAwait(false) ?? string.Empty;

        /// <summary>Block on an operation of the new surface, for the synchronous half of a member.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The operation to wait for.</param>
        /// <remarks>
        /// <inheritdoc cref="TransitionalSync" path="/remarks/para[2]"/>
        /// </remarks>
        private T Wait<T>(ValueTask<T> pending) => TransitionalSync.Wait(pending, multiplexer, Context.Raw.Executor);

        /// <inheritdoc cref="Wait{T}(ValueTask{T})"/>
        /// <param name="pending">The operation to wait for.</param>
        private void Wait(ValueTask pending) => TransitionalSync.Wait(pending, multiplexer);

        int IServer.DatabaseCount => server.Databases;

        public ClusterConfiguration? ClusterConfiguration => server.ClusterConfiguration;

        public EndPoint EndPoint => server.EndPoint;

        public RedisFeatures Features => server.GetFeatures();

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Either core's connection counts, because the caller is asking about the client.</b> Under the
        /// engine flag the socket carrying this endpoint's commands is the new core's, so answering from the
        /// shipped bridge alone reported "not connected" about a server that had just replied. Deliberately
        /// only on this public surface: <c>ServerEndPoint.IsConnected</c> is also what the shipped selector
        /// routes by, and that has to keep meaning "this bridge is up".
        /// </remarks>
        public bool IsConnected
            => server.IsConnected
                || (ConnectionMultiplexer.NewCoreEngine && multiplexer.NewCoreIfCreated?.IsConnected(server.EndPoint) == true);

        bool IServer.IsSlave => IsReplica;
        public bool IsReplica => server.IsReplica;

        public RedisProtocol Protocol => server.Protocol ?? (multiplexer.RawConfig.TryResp3() ? RedisProtocol.Resp3 : RedisProtocol.Resp2);

        bool IServer.AllowSlaveWrites
        {
            get => AllowReplicaWrites;
            set => AllowReplicaWrites = value;
        }
        public bool AllowReplicaWrites
        {
            get => server.AllowReplicaWrites;
            set => server.AllowReplicaWrites = value;
        }

        public ServerType ServerType => server.ServerType;

        public ProductVariant GetProductVariant(out string version) => server.GetProductVariant(out version);

        public Version Version => server.Version;

        public RedisKey InventKey(RedisKey prefix = default)
        {
            var guid = Guid.NewGuid();
            if (server.ServerType is ServerType.Cluster)
            {
                var slot = server.GetServableSlot();
                if (slot is null) return RedisKey.Null;
                return ServerSelectionStrategy.CreateKeyForSlot(slot.Value, guid.ToString()).Prepend(prefix);
            }
            return prefix.Append(guid.ToString());
        }

        public void ClientKill(EndPoint endpoint, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.CLIENT, RedisLiterals.KILL, Format.ToString(endpoint).AsRedisValue());
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ClientKillAsync(EndPoint endpoint, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.CLIENT, RedisLiterals.KILL, Format.ToString(endpoint).AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public long ClientKill(long? id = null, ClientType? clientType = null, EndPoint? endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClientKillMessage(endpoint, id, clientType, skipMe, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ClientKillAsync(long? id = null, ClientType? clientType = null, EndPoint? endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClientKillMessage(endpoint, id, clientType, skipMe, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long ClientKill(ClientKillFilter filter, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.CLIENT, filter.ToList(Features.ReplicaCommands));
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ClientKillAsync(ClientKillFilter filter, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.CLIENT, filter.ToList(Features.ReplicaCommands));
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        private Message GetClientKillMessage(EndPoint? endpoint, long? id, ClientType? clientType, bool? skipMe, CommandFlags flags)
        {
            var args = new ClientKillFilter().WithId(id).WithClientType(clientType).WithEndpoint(endpoint).WithSkipMe(skipMe).ToList(Features.ReplicaCommands);
            return Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.CLIENT, args);
        }

        public ClientInfo[] ClientList(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.LIST);
            return ExecuteSync(msg, ClientInfo.Processor, defaultValue: Array.Empty<ClientInfo>());
        }

        public Task<ClientInfo[]> ClientListAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.LIST);
            return ExecuteAsync(msg, ClientInfo.Processor, defaultValue: Array.Empty<ClientInfo>());
        }

        public ClusterConfiguration? ClusterNodes(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClusterNodesMessage(flags);
            return ExecuteSync(msg, ResultProcessor.ClusterNodes);
        }

        public Task<ClusterConfiguration?> ClusterNodesAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClusterNodesMessage(flags);
            return ExecuteAsync(msg, ResultProcessor.ClusterNodes);
        }

        public string? ClusterNodesRaw(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClusterNodesMessage(flags);
            return ExecuteSync(msg, ResultProcessor.ClusterNodesRaw);
        }

        public Task<string?> ClusterNodesRawAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClusterNodesMessage(flags);
            return ExecuteAsync(msg, ResultProcessor.ClusterNodesRaw);
        }

        /// <summary>
        /// CLUSTER NODES only reads topology, despite CLUSTER as a whole defaulting to server-admin; it stays
        /// node-scoped because the answer is that node's view of the cluster.
        /// </summary>
        /// <remarks>
        /// The single spelling of this: the topology probes in <c>ServerEndPoint.AutoConfigureAsync</c> and
        /// <c>ConnectionMultiplexer.GetEndpointsFromClusterNodes</c> come through here too, so the category
        /// cannot drift between the three places we ask the same question.
        /// </remarks>
        internal static Message GetClusterNodesMessage(CommandFlags flags)
            => Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.CLUSTER, RedisLiterals.NODES);

        /// <summary>
        /// As <see cref="GetClusterNodesMessage"/>, for the <c>CLUSTER SLOTS</c> view of the same topology:
        /// likewise asked both by the public API and by the autoconfigure probe, and likewise a node-local
        /// read - it reports what the answering node believes, so it is safe to replay against that node.
        /// </summary>
        internal static Message GetClusterSlotsMessage(CommandFlags flags)
            => Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.CLUSTER, RedisLiterals.SLOTS);

        public KeyValuePair<string, string>[] ConfigGet(RedisValue pattern = default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetConfigGetMessage(pattern, flags);
            return ExecuteSync(msg, ResultProcessor.StringPairInterleaved, defaultValue: Array.Empty<KeyValuePair<string, string>>());
        }

        public Task<KeyValuePair<string, string>[]> ConfigGetAsync(RedisValue pattern = default, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetConfigGetMessage(pattern, flags);
            return ExecuteAsync(msg, ResultProcessor.StringPairInterleaved, defaultValue: Array.Empty<KeyValuePair<string, string>>());
        }

        internal static Message GetConfigGetMessage(RedisValue pattern, CommandFlags flags)
        {
            if (pattern.IsNullOrEmpty) pattern = RedisLiterals.Wildcard;

            // CONFIG as a whole is server-admin, but CONFIG GET is safe metadata
            return Message.Create(-1, flags.WithRetryCategory(CommandFlags.CommandRetryConnection | Message.CommandServerSpecific), RedisCommand.CONFIG, RedisLiterals.GET, pattern);
        }

        public void ConfigResetStatistics(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Config.ResetStatisticsAsync(flags));

        public Task ConfigResetStatisticsAsync(CommandFlags flags = CommandFlags.None)
            => Context.Config.ResetStatisticsAsync(flags).AsTask(asyncState, flags);

        public void ConfigRewrite(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Config.RewriteAsync(flags));

        public Task ConfigRewriteAsync(CommandFlags flags = CommandFlags.None)
            => Context.Config.RewriteAsync(flags).AsTask(asyncState, flags);

        public void ConfigSet(RedisValue setting, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.SET, setting, value);
            ExecuteSync(msg, ResultProcessor.DemandOK);
            ExecuteSync(Message.Create(-1, flags | CommandFlags.FireAndForget, RedisCommand.CONFIG, RedisLiterals.GET, setting), ResultProcessor.AutoConfigure);
        }

        public Task ConfigSetAsync(RedisValue setting, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.SET, setting, value);
            var task = ExecuteAsync(msg, ResultProcessor.DemandOK);
            ExecuteSync(Message.Create(-1, flags | CommandFlags.FireAndForget, RedisCommand.CONFIG, RedisLiterals.GET, setting), ResultProcessor.AutoConfigure);
            return task;
        }

        public long CommandCount(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.CommandCountAsync(flags));

        public Task<long> CommandCountAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.CommandCountAsync(flags).AsTask(asyncState, flags);

        public RedisKey[] CommandGetKeys(RedisValue[] command, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.COMMAND, AddValueToArray(RedisLiterals.GETKEYS, command));
            return ExecuteSync(msg, ResultProcessor.RedisKeyArray, defaultValue: Array.Empty<RedisKey>());
        }

        public Task<RedisKey[]> CommandGetKeysAsync(RedisValue[] command, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.COMMAND, AddValueToArray(RedisLiterals.GETKEYS, command));
            return ExecuteAsync(msg, ResultProcessor.RedisKeyArray, defaultValue: Array.Empty<RedisKey>());
        }

        public string[] CommandList(RedisValue? moduleName = null, RedisValue? category = null, RedisValue? pattern = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetCommandListMessage(moduleName, category, pattern, flags);
            return ExecuteSync(msg, ResultProcessor.StringArray, defaultValue: Array.Empty<string>());
        }

        public Task<string[]> CommandListAsync(RedisValue? moduleName = null, RedisValue? category = null, RedisValue? pattern = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetCommandListMessage(moduleName, category, pattern, flags);
            return ExecuteAsync(msg, ResultProcessor.StringArray, defaultValue: Array.Empty<string>());
        }

        private Message GetCommandListMessage(RedisValue? moduleName = null, RedisValue? category = null, RedisValue? pattern = null, CommandFlags flags = CommandFlags.None)
        {
            if (moduleName == null && category == null && pattern == null)
            {
                return Message.Create(-1, flags, RedisCommand.COMMAND, RedisLiterals.LIST);
            }
            else if (moduleName != null && category == null && pattern == null)
            {
                return Message.Create(-1, flags, RedisCommand.COMMAND, MakeArray(RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.MODULE, (RedisValue)moduleName));
            }
            else if (moduleName == null && category != null && pattern == null)
            {
                return Message.Create(-1, flags, RedisCommand.COMMAND, MakeArray(RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.ACLCAT, (RedisValue)category));
            }
            else if (moduleName == null && category == null && pattern != null)
            {
                return Message.Create(-1, flags, RedisCommand.COMMAND, MakeArray(RedisLiterals.LIST, RedisLiterals.FILTERBY, RedisLiterals.PATTERN, (RedisValue)pattern));
            }
            else
            {
                throw new ArgumentException("More then one filter is not allowed");
            }
        }

        private RedisValue[] AddValueToArray(RedisValue val, RedisValue[] arr)
        {
            var result = new RedisValue[arr.Length + 1];
            var i = 0;
            result[i++] = val;
            foreach (var item in arr) result[i++] = item;
            return result;
        }

        private RedisValue[] MakeArray(params RedisValue[] redisValues) => redisValues;

        public long DatabaseSize(int database = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keyspace.CountAsync(multiplexer.ApplyDefaultDatabase(database), flags));

        public Task<long> DatabaseSizeAsync(int database = -1, CommandFlags flags = CommandFlags.None)
            => Context.Keyspace.CountAsync(multiplexer.ApplyDefaultDatabase(database), flags).AsTask(asyncState, flags);

        public RedisValue Echo(RedisValue message, CommandFlags flags)
            => Wait(Context.Diagnostics.EchoAsync(message, flags));

        public Task<RedisValue> EchoAsync(RedisValue message, CommandFlags flags)
            => Context.Diagnostics.EchoAsync(message, flags).AsTask(asyncState, flags);

        public void FlushAllDatabases(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keyspace.FlushAllAsync(flags));

        public Task FlushAllDatabasesAsync(CommandFlags flags = CommandFlags.None)
            => Context.Keyspace.FlushAllAsync(flags).AsTask(asyncState, flags);

        public void FlushDatabase(int database = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keyspace.FlushAsync(multiplexer.ApplyDefaultDatabase(database), flags));

        public Task FlushDatabaseAsync(int database = -1, CommandFlags flags = CommandFlags.None)
            => Context.Keyspace.FlushAsync(multiplexer.ApplyDefaultDatabase(database), flags).AsTask(asyncState, flags);

        public ServerCounters GetCounters() => server.GetCounters();

        private static IGrouping<string, KeyValuePair<string, string>>[] InfoDefault =>
            Array.Empty<IGrouping<string, KeyValuePair<string, string>>>();

        /// <inheritdoc/>
        /// <remarks>
        /// <b>On the context surface</b>, which for a server means this endpoint's own connection - see
        /// D2.4. <c>INFO</c> is node-local by nature: the answer describes the server that was asked, so
        /// sending it anywhere else answers a different question.
        /// </remarks>
        public IGrouping<string, KeyValuePair<string, string>>[] Info(RedisValue section = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.InfoAsync(section, flags));

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="Info" path="/remarks"/></remarks>
        public Task<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(RedisValue section = default, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.InfoAsync(section, flags).AsTask(asyncState, flags);

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="Info" path="/remarks"/></remarks>
        public string? InfoRaw(RedisValue section = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.InfoRawAsync(section, flags));

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="Info" path="/remarks"/></remarks>
        public Task<string?> InfoRawAsync(RedisValue section = default, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.InfoRawAsync(section, flags).AsTask(asyncState, flags);

        IEnumerable<RedisKey> IServer.Keys(int database, RedisValue pattern, int pageSize, CommandFlags flags)
            => KeysAsync(database, pattern, pageSize, CursorUtils.Origin, 0, flags);

        IEnumerable<RedisKey> IServer.Keys(int database, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => KeysAsync(database, pattern, pageSize, cursor, pageOffset, flags);

        IAsyncEnumerable<RedisKey> IServer.KeysAsync(int database, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => KeysAsync(database, pattern, pageSize, cursor, pageOffset, flags);

        private CursorEnumerable<RedisKey> KeysAsync(int database, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            database = multiplexer.ApplyDefaultDatabase(database);
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (CursorUtils.IsNil(pattern)) pattern = RedisLiterals.Wildcard;

            if (multiplexer.CommandMap.IsAvailable(RedisCommand.SCAN))
            {
                var features = server.GetFeatures();

                if (features.Scan) return new KeysScanEnumerable(this, database, pattern, pageSize, cursor, pageOffset, flags);
            }

            if (cursor != 0) throw ExceptionFactory.NoCursor(RedisCommand.KEYS);
            Message msg = Message.Create(database, flags, RedisCommand.KEYS, pattern);
            return CursorEnumerable<RedisKey>.From(this, server, ExecuteAsync(msg, ResultProcessor.RedisKeyArray, defaultValue: Array.Empty<RedisKey>()), pageOffset);
        }

        public DateTime LastSave(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.LastSaveAsync(flags));

        public Task<DateTime> LastSaveAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.LastSaveAsync(flags).AsTask(asyncState, flags);

        public void MakeMaster(ReplicationChangeOptions options, TextWriter? log = null)
        {
            // Do you believe in magic?
            multiplexer.MakePrimaryAsync(server, options, log).Wait(60000);
        }

        public async Task MakePrimaryAsync(ReplicationChangeOptions options, TextWriter? log = null)
        {
            await multiplexer.MakePrimaryAsync(server, options, log).ForAwait();
        }

        public Role Role(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.ROLE);
            return ExecuteSync(msg, ResultProcessor.Role, defaultValue: Redis.Role.Null);
        }

        public Task<Role> RoleAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.ROLE);
            return ExecuteAsync(msg, ResultProcessor.Role, defaultValue: Redis.Role.Null);
        }

        public void Save(SaveType type, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSaveMessage(type, flags);
            ExecuteSync(msg, GetSaveResultProcessor(type));
        }

        public Task SaveAsync(SaveType type, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetSaveMessage(type, flags);
            return ExecuteAsync(msg, GetSaveResultProcessor(type));
        }

        public bool ScriptExists(string script, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Scripts.ExistsAsync(ScriptHash.Hash(script), flags));

        public bool ScriptExists(byte[] sha1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Scripts.ExistsAsync(ScriptHash.Encode(sha1), flags));

        public Task<bool> ScriptExistsAsync(string script, CommandFlags flags = CommandFlags.None)
            => Context.Scripts.ExistsAsync(ScriptHash.Hash(script), flags).AsTask(asyncState, flags);

        public Task<bool> ScriptExistsAsync(byte[] sha1, CommandFlags flags = CommandFlags.None)
            => Context.Scripts.ExistsAsync(ScriptHash.Encode(sha1), flags).AsTask(asyncState, flags);

        public void ScriptFlush(CommandFlags flags = CommandFlags.None)
        {
            DemandAdminForScriptFlush();
            Wait(Context.Scripts.FlushAsync(flags));
        }

        public Task ScriptFlushAsync(CommandFlags flags = CommandFlags.None)
        {
            DemandAdminForScriptFlush();
            return Context.Scripts.FlushAsync(flags).AsTask(asyncState, flags);
        }

        /// <summary>Refuse a script flush unless admin mode is enabled.</summary>
        /// <remarks>
        /// <b>Raised here rather than left to the general gate</b>, and it was so before this moved: the
        /// whole-command gate sees <c>SCRIPT</c>, which covers <c>EXISTS</c> and <c>LOAD</c> as well, and
        /// those are not admin. So the one sub-command that is says so itself.
        /// </remarks>
        private void DemandAdminForScriptFlush()
        {
            if (!multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(multiplexer.RawConfig.IncludeDetailInExceptions, RedisCommand.SCRIPT, null, server);
            }
        }

        public byte[] ScriptLoad(string script, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ScriptLoadMessage(flags, script);
            return ExecuteSync(msg, ResultProcessor.ScriptLoad, defaultValue: Array.Empty<byte>()); // Note: default isn't used on failure - we'll throw
        }

        public Task<byte[]> ScriptLoadAsync(string script, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ScriptLoadMessage(flags, script);
            return ExecuteAsync(msg, ResultProcessor.ScriptLoad, defaultValue: Array.Empty<byte>()); // Note: default isn't used on failure - we'll throw
        }

        public LoadedLuaScript ScriptLoad(LuaScript script, CommandFlags flags = CommandFlags.None)
        {
            return script.Load(this, flags);
        }

        public Task<LoadedLuaScript> ScriptLoadAsync(LuaScript script, CommandFlags flags = CommandFlags.None)
        {
            return script.LoadAsync(this, flags);
        }

        public void Shutdown(ShutdownMode shutdownMode = ShutdownMode.Default, CommandFlags flags = CommandFlags.None)
        {
            Message msg = shutdownMode switch
            {
                ShutdownMode.Default => Message.Create(-1, flags, RedisCommand.SHUTDOWN),
                ShutdownMode.Always => Message.Create(-1, flags, RedisCommand.SHUTDOWN, RedisLiterals.SAVE),
                ShutdownMode.Never => Message.Create(-1, flags, RedisCommand.SHUTDOWN, RedisLiterals.NOSAVE),
                _ => throw new ArgumentOutOfRangeException(nameof(shutdownMode)),
            };
            try
            {
                ExecuteSync(msg, ResultProcessor.DemandOK);
            }
            catch (RedisConnectionException ex) when (ex.FailureType == ConnectionFailureType.SocketClosed || ex.FailureType == ConnectionFailureType.SocketFailure)
            {
                // that's fine
                return;
            }
        }

        public CommandTrace[] SlowlogGet(int count = 0, CommandFlags flags = CommandFlags.None)
        {
            return Wait(Context.Diagnostics.SlowLogAsync(count, flags));
        }

        public Task<CommandTrace[]> SlowlogGetAsync(int count = 0, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.SlowLogAsync(count, flags).AsTask(asyncState, flags);

        public void SlowlogReset(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.ResetSlowLogAsync(flags));

        public Task SlowlogResetAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.ResetSlowLogAsync(flags).AsTask(asyncState, flags);

        public RedisValue StringGet(int db, RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(db, flags, RedisCommand.GET, key);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> StringGetAsync(int db, RedisKey key, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(db, flags, RedisCommand.GET, key);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public RedisChannel[] SubscriptionChannels(RedisChannel pattern = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.PubSub.ChannelsAsync(pattern, flags));

        public Task<RedisChannel[]> SubscriptionChannelsAsync(RedisChannel pattern = default, CommandFlags flags = CommandFlags.None)
            => Context.PubSub.ChannelsAsync(pattern, flags).AsTask(asyncState, flags);

        public long SubscriptionPatternCount(CommandFlags flags = CommandFlags.None)
            => Wait(Context.PubSub.PatternCountAsync(flags));

        public Task<long> SubscriptionPatternCountAsync(CommandFlags flags = CommandFlags.None)
            => Context.PubSub.PatternCountAsync(flags).AsTask(asyncState, flags);

        public long SubscriptionSubscriberCount(RedisChannel channel, CommandFlags flags = CommandFlags.None)
            => Wait(Context.PubSub.SubscriberCountAsync(channel, flags));

        public Task<long> SubscriptionSubscriberCountAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
            => Context.PubSub.SubscriberCountAsync(channel, flags).AsTask(asyncState, flags);

        public void SwapDatabases(int first, int second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keyspace.SwapAsync(first, second, flags));

        public Task SwapDatabasesAsync(int first, int second, CommandFlags flags = CommandFlags.None)
            => Context.Keyspace.SwapAsync(first, second, flags).AsTask(asyncState, flags);

        public DateTime Time(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.TimeAsync(flags));

        public Task<DateTime> TimeAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.TimeAsync(flags).AsTask(asyncState, flags);

        internal static Message CreateReplicaOfMessage(ServerEndPoint sendMessageTo, EndPoint? primaryEndpoint, CommandFlags flags = CommandFlags.None)
        {
            RedisValue host, port;
            if (primaryEndpoint == null)
            {
                host = RedisLiterals.NO;
                port = RedisLiterals.ONE;
            }
            else
            {
                if (Format.TryGetHostPort(primaryEndpoint, out string? hostRaw, out int? portRaw))
                {
                    host = hostRaw.AsRedisValue();
                    port = portRaw;
                }
                else
                {
                    throw new NotSupportedException("Unknown endpoint type: " + primaryEndpoint.GetType().Name);
                }
            }
            return Message.Create(-1, flags, sendMessageTo.GetFeatures().ReplicaCommands ? RedisCommand.REPLICAOF : RedisCommand.SLAVEOF, host, port);
        }

        private Message? GetTiebreakerRemovalMessage()
        {
            var configuration = multiplexer.RawConfig;

            if (configuration.TryGetTieBreaker(out var tieBreakerKey) && multiplexer.CommandMap.IsAvailable(RedisCommand.DEL))
            {
                var msg = Message.Create(0, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.DEL, tieBreakerKey);
                msg.SetInternalCall();
                return msg;
            }
            return null;
        }

        private Message? GetConfigChangeMessage()
        {
            // attempt to broadcast a reconfigure message to anybody listening to this server
            var channel = multiplexer.ConfigurationChangedChannel;
            if (channel != null && multiplexer.CommandMap.IsAvailable(RedisCommand.PUBLISH))
            {
                var msg = Message.Create(-1, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.PUBLISH, (RedisValue)channel, RedisLiterals.Wildcard);
                msg.SetInternalCall();
                return msg;
            }
            return null;
        }

        internal override Task<T> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, T defaultValue, ServerEndPoint? server = null)
        {
            // inject our expected server automatically
            server ??= this.server;
            FixFlags(message, server);
            if (!server.IsConnected)
            {
                if (message == null) return CompletedTask<T>.FromDefault(defaultValue, asyncState);
                if (message.IsFireAndForget) return CompletedTask<T>.FromDefault(defaultValue, null); // F+F explicitly does not get async-state

                // After the "don't care" cases above, if we can't queue then it's time to error - otherwise call through to queuing.
                if (!multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    // no need to deny exec-sync here; will be complete before they see if
                    var tcs = TaskSource.Create<T>(asyncState);
                    ConnectionMultiplexer.ThrowFailed(tcs, ExceptionFactory.NoConnectionAvailable(multiplexer, message, server));
                    return tcs.Task;
                }
            }
            return base.ExecuteAsync(message, processor, defaultValue, server);
        }

        internal override Task<T?> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null) where T : default
        {
            // inject our expected server automatically
            server ??= this.server;
            FixFlags(message, server);
            if (!server.IsConnected)
            {
                if (message == null) return CompletedTask<T>.Default(asyncState);
                if (message.IsFireAndForget) return CompletedTask<T>.Default(null); // F+F explicitly does not get async-state

                // After the "don't care" cases above, if we can't queue then it's time to error - otherwise call through to queuing.
                if (!multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    // no need to deny exec-sync here; will be complete before they see if
                    var tcs = TaskSource.Create<T?>(asyncState);
                    ConnectionMultiplexer.ThrowFailed(tcs, ExceptionFactory.NoConnectionAvailable(multiplexer, message, server));
                    return tcs.Task;
                }
            }
            return base.ExecuteAsync(message, processor, server);
        }

        [return: NotNullIfNotNull("defaultValue")]
        internal override T? ExecuteSync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null, T? defaultValue = default) where T : default
        {
            // inject our expected server automatically
            if (server == null) server = this.server;
            FixFlags(message, server);
            if (!server.IsConnected)
            {
                if (message == null || message.IsFireAndForget) return defaultValue;

                // After the "don't care" cases above, if we can't queue then it's time to error - otherwise call through to queuing.
                if (!multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    throw ExceptionFactory.NoConnectionAvailable(multiplexer, message, server);
                }
            }
            return base.ExecuteSync<T>(message, processor, server, defaultValue);
        }

        internal override RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, RedisCommand command, out ServerEndPoint server)
        {
            server = this.server;
            return server.GetFeatures();
        }

        void IServer.SlaveOf(EndPoint master, CommandFlags flags) => ReplicaOf(master, flags);

        public void ReplicaOf(EndPoint master, CommandFlags flags = CommandFlags.None)
        {
            if (master == server.EndPoint)
            {
                throw new ArgumentException("Cannot replicate to self");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            // attempt to cease having an opinion on the master; will resume that when replication completes
            // (note that this may fail; we aren't depending on it)
            if (GetTiebreakerRemovalMessage() is Message tieBreakerRemoval)
            {
                tieBreakerRemoval.SetSource(ResultProcessor.Boolean, null);
                server.GetBridge(tieBreakerRemoval)?.TryWriteSync(tieBreakerRemoval, server.IsReplica);
            }

            var replicaOfMsg = CreateReplicaOfMessage(server, master, flags);
            ExecuteSync(replicaOfMsg, ResultProcessor.DemandOK);

            // attempt to broadcast a reconfigure message to anybody listening to this server
            if (GetConfigChangeMessage() is Message configChangeMessage)
            {
                configChangeMessage.SetSource(ResultProcessor.Int64, null);
                server.GetBridge(configChangeMessage)?.TryWriteSync(configChangeMessage, server.IsReplica);
            }
#pragma warning restore CS0618
        }

        Task IServer.SlaveOfAsync(EndPoint master, CommandFlags flags) => ReplicaOfAsync(master, flags);

        public async Task ReplicaOfAsync(EndPoint? master, CommandFlags flags = CommandFlags.None)
        {
            if (master == server.EndPoint)
            {
                throw new ArgumentException("Cannot replicate to self");
            }

            // Attempt to cease having an opinion on the primary - will resume that when replication completes
            // (note that this may fail - we aren't depending on it)
            if (GetTiebreakerRemovalMessage() is Message tieBreakerRemoval && !server.IsReplica)
            {
                try
                {
                    await server.WriteDirectAsync(tieBreakerRemoval, ResultProcessor.Boolean).ForAwait();
                }
                catch { }
            }

            var msg = CreateReplicaOfMessage(server, master, flags);
            await ExecuteAsync(msg, ResultProcessor.DemandOK).ForAwait();

            // attempt to broadcast a reconfigure message to anybody listening to this server
            if (GetConfigChangeMessage() is Message configChangeMessage)
            {
                await server.WriteDirectAsync(configChangeMessage, ResultProcessor.Int64).ForAwait();
            }
        }

        private static void FixFlags(Message? message, ServerEndPoint server)
        {
            if (message is null)
            {
                return;
            }

            // since the server is specified explicitly, we don't want defaults
            // to make the "non-preferred-endpoint" counters look artificially
            // inflated; note we only change *prefer* options
            switch (Message.GetPrimaryReplicaFlags(message.Flags))
            {
                case CommandFlags.PreferMaster:
                    if (server.IsReplica) message.SetPreferReplica();
                    break;
                case CommandFlags.PreferReplica:
                    if (!server.IsReplica) message.SetPreferPrimary();
                    break;
            }
        }

        private static Message GetSaveMessage(SaveType type, CommandFlags flags = CommandFlags.None) => type switch
        {
            SaveType.BackgroundRewriteAppendOnlyFile => Message.Create(-1, flags, RedisCommand.BGREWRITEAOF),
            SaveType.BackgroundSave => Message.Create(-1, flags, RedisCommand.BGSAVE),
#pragma warning disable CS0618 // Type or member is obsolete
            SaveType.ForegroundSave => Message.Create(-1, flags, RedisCommand.SAVE),
#pragma warning restore CS0618
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        private static ResultProcessor<bool> GetSaveResultProcessor(SaveType type) => type switch
        {
            SaveType.BackgroundRewriteAppendOnlyFile => ResultProcessor.BackgroundSaveAOFStarted,
            SaveType.BackgroundSave => ResultProcessor.BackgroundSaveStarted,
#pragma warning disable CS0618 // Type or member is obsolete
            SaveType.ForegroundSave => ResultProcessor.DemandOK,
#pragma warning restore CS0618
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        private static class ScriptHash
        {
            public static RedisValue Encode(byte[] value)
            {
                const string hex = "0123456789abcdef";
                if (value == null)
                {
                    return default;
                }
                var result = new byte[value.Length * 2];
                int offset = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    int val = value[i];
                    result[offset++] = (byte)hex[val >> 4];
                    result[offset++] = (byte)hex[val & 15];
                }
                return result;
            }

            public static RedisValue Hash(string value)
            {
                if (value is null) return default;
                using (var sha1 = SHA1.Create())
                {
                    var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
                    return Encode(bytes);
                }
            }
        }

        private sealed class KeysScanEnumerable : CursorEnumerable<RedisKey>
        {
            private readonly RedisValue pattern;

            public KeysScanEnumerable(RedisServer server, int db, in RedisValue pattern, int pageSize, in RedisValue cursor, int pageOffset, CommandFlags flags)
                : base(server, server.server, db, pageSize, cursor, pageOffset, flags)
            {
                this.pattern = pattern;
            }

            private protected override Message CreateMessage(in RedisValue cursor)
            {
                var flags = this.flags.WithScanCursorCategory(cursor);
                if (CursorUtils.IsNil(pattern))
                {
                    if (pageSize == CursorUtils.DefaultRedisPageSize)
                    {
                        return Message.Create(db, flags, RedisCommand.SCAN, cursor);
                    }
                    else
                    {
                        return Message.Create(db, flags, RedisCommand.SCAN, cursor, RedisLiterals.COUNT, pageSize);
                    }
                }
                else
                {
                    if (pageSize == CursorUtils.DefaultRedisPageSize)
                    {
                        return Message.Create(db, flags, RedisCommand.SCAN, cursor, RedisLiterals.MATCH, pattern);
                    }
                    else
                    {
                        return Message.Create(db, flags, RedisCommand.SCAN, cursor, RedisLiterals.MATCH, pattern, RedisLiterals.COUNT, pageSize);
                    }
                }
            }

            private protected override ResultProcessor<ScanResult> Processor => processor;

            public static readonly ResultProcessor<ScanResult> processor = new ScanResultProcessor();
            private sealed class ScanResultProcessor : ResultProcessor<ScanResult>
            {
                protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
                {
                    if (reader.IsAggregate && reader.AggregateLengthIs(2))
                    {
                        // SCAN returns [cursor, [keys...]]
                        var iter = reader.AggregateChildren();
                        if (!iter.MoveNext()) return false;
                        var cursor = iter.Value.ReadRedisValue();

                        if (iter.MoveNext() && iter.Value.IsAggregate)
                        {
                            RedisKey[] keys;
                            int count;
                            if (iter.Value.IsNull || iter.Value.AggregateLengthIs(0))
                            {
                                keys = Array.Empty<RedisKey>();
                                count = 0;
                            }
                            else
                            {
                                count = iter.Value.AggregateLength();
                                keys = ArrayPool<RedisKey>.Shared.Rent(count);
                                var keysIter = iter.Value.AggregateChildren();
                                for (int i = 0; i < count; i++)
                                {
                                    keysIter.DemandNext();
                                    keys[i] = keysIter.Value.ReadRedisKey();
                                }
                            }
                            var keysResult = new ScanResult(cursor, keys, count, true);
                            SetResult(message, keysResult);
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        public EndPoint? SentinelGetMasterAddressByName(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.GETMASTERADDRBYNAME, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.SentinelPrimaryEndpoint);
        }

        public Task<EndPoint?> SentinelGetMasterAddressByNameAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.GETMASTERADDRBYNAME, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.SentinelPrimaryEndpoint);
        }

        public EndPoint[] SentinelGetSentinelAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.SENTINELS, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.SentinelAddressesEndPoints, defaultValue: Array.Empty<EndPoint>());
        }

        public Task<EndPoint[]> SentinelGetSentinelAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.SENTINELS, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.SentinelAddressesEndPoints, defaultValue: Array.Empty<EndPoint>());
        }

        public EndPoint[] SentinelGetReplicaAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, Features.ReplicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.SentinelAddressesEndPoints, defaultValue: Array.Empty<EndPoint>());
        }

        public Task<EndPoint[]> SentinelGetReplicaAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, Features.ReplicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.SentinelAddressesEndPoints, defaultValue: Array.Empty<EndPoint>());
        }

        public KeyValuePair<string, string>[] SentinelMaster(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.MASTER, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.StringPairInterleaved, defaultValue: Array.Empty<KeyValuePair<string, string>>());
        }

        public Task<KeyValuePair<string, string>[]> SentinelMasterAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.MASTER, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.StringPairInterleaved, defaultValue: Array.Empty<KeyValuePair<string, string>>());
        }

        public void SentinelFailover(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.FAILOVER, serviceName.AsRedisValue());
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task SentinelFailoverAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.FAILOVER, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public KeyValuePair<string, string>[][] SentinelMasters(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.MASTERS);
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        public Task<KeyValuePair<string, string>[][]> SentinelMastersAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.MASTERS);
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        // For previous compat only
        KeyValuePair<string, string>[][] IServer.SentinelSlaves(string serviceName, CommandFlags flags)
            => SentinelReplicas(serviceName, flags);

        public KeyValuePair<string, string>[][] SentinelReplicas(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, Features.ReplicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        // For previous compat only
        Task<KeyValuePair<string, string>[][]> IServer.SentinelSlavesAsync(string serviceName, CommandFlags flags)
            => SentinelReplicasAsync(serviceName, flags);

        public Task<KeyValuePair<string, string>[][]> SentinelReplicasAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, Features.ReplicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        public KeyValuePair<string, string>[][] SentinelSentinels(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.SENTINELS, serviceName.AsRedisValue());
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        public Task<KeyValuePair<string, string>[][]> SentinelSentinelsAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags.WithRetryCategory(NodeLocalRead), RedisCommand.SENTINEL, RedisLiterals.SENTINELS, serviceName.AsRedisValue());
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays, defaultValue: Array.Empty<KeyValuePair<string, string>[]>());
        }

        public RedisResult Execute(string command, params object[] args) => Execute(command, args, CommandFlags.None);

        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => Wait(RespAdHoc.ExecuteAsync(AdHocContext(DatabaseForAdHoc(command)), command, args, flags));

        public Task<RedisResult> ExecuteAsync(string command, params object[] args) => ExecuteAsync(command, args, CommandFlags.None);

        public Task<RedisResult> ExecuteAsync(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => RespAdHoc.ExecuteAsync(AdHocContext(DatabaseForAdHoc(command)), command, args, flags).AsTask(asyncState, flags);

        /// <summary>
        /// The database an ad-hoc command should run against when the caller named a server but no database.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A command that needs one gets the configured default, exactly as the database-taking overload of
        /// <see cref="Execute(int?, string, ICollection{object}, CommandFlags)"/> does for a null database, and
        /// as <see cref="DatabaseSize(int, CommandFlags)"/> and its siblings already do. Anything else is left
        /// alone at -1, so no <c>SELECT</c> is emitted and the connection's current database is undisturbed.
        /// </para>
        /// <para>
        /// Without this these commands are simply refused: ad-hoc commands used to travel as
        /// <see cref="RedisCommand.UNKNOWN"/> and so skipped the database assertion entirely, until they
        /// started being recognised for the sake of <see cref="CommandMap"/> aliasing. The sibling path from
        /// <c>IDatabase</c> got a matching allowance at the time (it strips a database that is not needed);
        /// this is the other half of it.
        /// </para>
        /// </remarks>
        private int DatabaseForAdHoc(string command)
            => RedisCommandMetadata.TryParseCI(command, out var known)
                && known is not RedisCommand.UNKNOWN
                && Message.RequiresDatabase(known)
                    ? multiplexer.ApplyDefaultDatabase(-1)
                    : -1;

        public RedisResult Execute(int? database, string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => Wait(RespAdHoc.ExecuteAsync(AdHocContext(multiplexer.ApplyDefaultDatabase(database ?? -1)), command, args, flags));

        public Task<RedisResult> ExecuteAsync(int? database, string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => RespAdHoc.ExecuteAsync(AdHocContext(multiplexer.ApplyDefaultDatabase(database ?? -1)), command, args, flags)
                .AsTask(asyncState, flags);

        /// <summary>This server's context, moved to a database when the command needs one.</summary>
        /// <param name="database">The database, or -1 for none.</param>
        /// <remarks>
        /// <b>-1 must not go through <c>WithDatabase</c>.</b> A server context carries no database on
        /// purpose, and moving it to "none" is not the same as leaving it alone - the latter is what keeps
        /// a keyless ad-hoc command from disturbing the connection's current database with a SELECT.
        /// </remarks>
        private RespContext AdHocContext(int database)
            => database < 0 ? Context.Raw : Context.Raw.WithDatabase(database);

        /// <summary>
        /// For testing only: Check if the server can simulate connection failure.
        /// </summary>
        internal bool CanSimulateConnectionFailure => server.CanSimulateConnectionFailure;

        /// <summary>
        /// For testing only.
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType) => server.SimulateConnectionFailure(failureType);

        public Task<string> LatencyDoctorAsync(CommandFlags flags = CommandFlags.None)
            => OrEmpty(Context.Diagnostics.LatencyDoctorAsync(flags)).AsTask(asyncState, flags);

        public string LatencyDoctor(CommandFlags flags = CommandFlags.None)
            => Wait(OrEmpty(Context.Diagnostics.LatencyDoctorAsync(flags)));

        /// <summary>The event names as the span the command group takes.</summary>
        /// <param name="eventNames">The caller's names; null or empty means every event.</param>
        /// <remarks>
        /// One allocation on an administrative command that resets a server's latency history, which is
        /// not a path anybody pipelines. The shipped spelling built a <c>RedisValue[]</c> here too, with
        /// the subcommand prepended into it; the group owns the subcommand now.
        /// </remarks>
        private static RedisValue[] LatencyEventNames(string[]? eventNames)
        {
            if (eventNames is null || eventNames.Length == 0) return Array.Empty<RedisValue>();

            var arr = new RedisValue[eventNames.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = eventNames[i].AsRedisValue();
            return arr;
        }

        public Task<long> LatencyResetAsync(string[]? eventNames = null, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.LatencyResetAsync(LatencyEventNames(eventNames), flags).AsTask(asyncState, flags);

        public long LatencyReset(string[]? eventNames = null, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.LatencyResetAsync(LatencyEventNames(eventNames), flags));

        public Task<LatencyHistoryEntry[]> LatencyHistoryAsync(string eventName, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.LatencyHistoryAsync(eventName.AsRedisValue(), flags).AsTask(asyncState, flags);

        public LatencyHistoryEntry[] LatencyHistory(string eventName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.LatencyHistoryAsync(eventName.AsRedisValue(), flags));

        public Task<LatencyLatestEntry[]> LatencyLatestAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.LatencyLatestAsync(flags).AsTask(asyncState, flags);

        public LatencyLatestEntry[] LatencyLatest(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.LatencyLatestAsync(flags));

        public Task<string> MemoryDoctorAsync(CommandFlags flags = CommandFlags.None)
            => OrEmpty(Context.Diagnostics.MemoryDoctorAsync(flags)).AsTask(asyncState, flags);

        public string MemoryDoctor(CommandFlags flags = CommandFlags.None)
            => Wait(OrEmpty(Context.Diagnostics.MemoryDoctorAsync(flags)));

        public Task MemoryPurgeAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.MemoryPurgeAsync(flags).AsTask(asyncState, flags);

        public void MemoryPurge(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.MemoryPurgeAsync(flags));

        internal static Message GetMemoryPurgeMessage(CommandFlags flags)
            => Message.Create(-1, flags.WithRetryCategory(NodeLocalAdmin), RedisCommand.MEMORY, RedisLiterals.PURGE);

        public Task<string?> MemoryAllocatorStatsAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.MemoryAllocatorStatsAsync(flags).AsTask(asyncState, flags);

        public string? MemoryAllocatorStats(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.MemoryAllocatorStatsAsync(flags));

        public Task<RedisResult> MemoryStatsAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.MemoryStatsAsync(flags).AsTask(asyncState, flags);

        public RedisResult MemoryStats(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.MemoryStatsAsync(flags));
    }
}
