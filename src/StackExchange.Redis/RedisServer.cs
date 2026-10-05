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
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

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
        private const CommandFlags NodeLocalRead = CommandFlags.CommandRetryReadOnly | CommandFlagsInternal.CommandServerSpecific,
                                   NodeLocalAdmin = CommandFlags.CommandRetryServerAdmin | CommandFlagsInternal.CommandServerSpecific;

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
        /// <b>The core's socket for this endpoint</b>, the same one the database commands use, so that a
        /// server command and the database commands it is meant to describe are ordered by one connection
        /// rather than racing two - see design notes D2.4, and 9b-xi for what that was costing.
        /// <para>
        /// <c>NewCore</c> rather than <c>NewCoreIfCreated</c>, and the difference is load-bearing because
        /// the context this feeds is MEMOISED. Asking "if created" meant an <c>IServer</c> touched before
        /// anything else - which <c>GetServer(...).Ping()</c> is, in test after test - got the v3
        /// executor and kept it for the life of the object, so half the point of D2.4 came and went
        /// according to call order.
        /// </para>
        /// </remarks>
        private RespExecutorBase ServerExecutor()
            => multiplexer.NewCore.ServerExecutor(server.EndPoint);

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Overridden so that a server's PING goes where the server's other commands go.</b> The v3
        /// <see cref="RedisBase.Ping"/> built a <c>Message</c> and sent it down the v3 pipeline, which while
        /// both cores existed was a different socket from the one this <c>IServer</c> was otherwise on - so
        /// "ping the server to bring it up", which is what half the suite opens with, brought up the wrong
        /// connection and left the interesting one still un-dialled. That is visible as a
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
        /// The v3 members passed <c>defaultValue: string.Empty</c> for exactly this: a server with
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
        /// The core's connection: while v3's bridge and the new core coexisted this had to ask both, because
        /// the bridge alone reported "not connected" about a server that had just replied.
        /// </remarks>
        public bool IsConnected
            => server.IsConnected;

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
            Wait(Context.Diagnostics.ClientKillAddress(Format.ToString(endpoint).AsRedisValue(), flags));
        }

        public Task ClientKillAsync(EndPoint endpoint, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.ClientKillAddress(Format.ToString(endpoint).AsRedisValue(), flags).AsTask(asyncState, flags);

        public long ClientKill(long? id = null, ClientType? clientType = null, EndPoint? endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.ClientKillCount(ClientKillArgs(endpoint, id, clientType, skipMe), flags));

        public Task<long> ClientKillAsync(long? id = null, ClientType? clientType = null, EndPoint? endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.ClientKillCount(ClientKillArgs(endpoint, id, clientType, skipMe), flags).AsTask(asyncState, flags);

        public long ClientKill(ClientKillFilter filter, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.ClientKillCount(filter.ToArray(Features.ReplicaCommands), flags));

        public Task<long> ClientKillAsync(ClientKillFilter filter, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.ClientKillCount(filter.ToArray(Features.ReplicaCommands), flags).AsTask(asyncState, flags);

        /// <summary>Render the loose-argument spelling of <c>CLIENT KILL</c> through the filter that owns the wire form.</summary>
        /// <remarks>
        /// The four-argument overload is the filter with four of its fields set; rendering it any other way
        /// would be a second copy of the same encoding, free to disagree with the first about - say - whether
        /// a replica is <c>replica</c> or <c>slave</c> on this server.
        /// </remarks>
        private RedisValue[] ClientKillArgs(EndPoint? endpoint, long? id, ClientType? clientType, bool? skipMe)
            => new ClientKillFilter().WithId(id).WithClientType(clientType).WithEndpoint(endpoint).WithSkipMe(skipMe)
                .ToArray(Features.ReplicaCommands);

        public ClientInfo[] ClientList(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.ClientListArray(flags));

        public Task<ClientInfo[]> ClientListAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.ClientListArray(flags).AsTask(asyncState, flags);

        public ClusterConfiguration? ClusterNodes(CommandFlags flags = CommandFlags.None)
            => Wait(ClusterNodesCore(flags));

        public Task<ClusterConfiguration?> ClusterNodesAsync(CommandFlags flags = CommandFlags.None)
            => ClusterNodesCore(flags).AsTask(asyncState, flags);

        public string? ClusterNodesRaw(CommandFlags flags = CommandFlags.None)
            => Wait(ClusterNodesRawCore(flags));

        public Task<string?> ClusterNodesRawAsync(CommandFlags flags = CommandFlags.None)
            => ClusterNodesRawCore(flags).AsTask(asyncState, flags);

        private async ValueTask<ClusterConfiguration?> ClusterNodesCore(CommandFlags flags)
            => ParseClusterNodes(await Context.Diagnostics.ClusterNodesRaw(flags).ConfigureAwait(false), demand: true);

        private async ValueTask<string?> ClusterNodesRawCore(CommandFlags flags)
        {
            var nodes = await Context.Diagnostics.ClusterNodesRaw(flags).ConfigureAwait(false);
            ParseClusterNodes(nodes, demand: false);
            return nodes;
        }

        /// <summary>
        /// Turn a <c>CLUSTER NODES</c> reply into the configuration, and record it against this server - the
        /// side-effect the v3 processor had, which keeps the selector's view current whenever anyone asks.
        /// </summary>
        /// <param name="nodes">The reply text.</param>
        /// <param name="demand">
        /// Whether a reply that cannot be parsed is the caller's problem. The raw overloads return the text
        /// whatever it says, so for them recording is best-effort, as it always was.
        /// </param>
        private ClusterConfiguration? ParseClusterNodes(string? nodes, bool demand)
        {
            if (string.IsNullOrWhiteSpace(nodes)) return null;
            try
            {
                var config = new ClusterConfiguration(multiplexer.ServerSelectionStrategy, nodes!, server.EndPoint);
                server.SetClusterConfiguration(config);
                return config;
            }
            catch when (!demand)
            {
                return null;
            }
        }

        public KeyValuePair<string, string>[] ConfigGet(RedisValue pattern = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Config.GetArray(pattern, flags));

        public Task<KeyValuePair<string, string>[]> ConfigGetAsync(RedisValue pattern = default, CommandFlags flags = CommandFlags.None)
            => Context.Config.GetArray(pattern, flags).AsTask(asyncState, flags);

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
            Wait(Context.Config.SetAsync(setting, value, flags));
            RelearnSetting(setting);
        }

        public Task ConfigSetAsync(RedisValue setting, RedisValue value, CommandFlags flags = CommandFlags.None)
        {
            var task = Context.Config.SetAsync(setting, value, flags).AsTask(asyncState, flags);
            RelearnSetting(setting);
            return task;
        }

        /// <summary>Read back a setting the caller just changed, so the client's model of it is current.</summary>
        /// <param name="setting">The setting that was changed.</param>
        /// <remarks>
        /// <para>
        /// <b>The reply is not the point</b>: <c>RespHandshake.ApplySetting</c> is - the same rule discovery
        /// uses at connect, which is how <c>databases</c>, <c>timeout</c> and <c>replica-read-only</c> stay
        /// true after a caller changes them. Fire-and-forget, as the v3 read-back was: it is sent after
        /// the <c>SET</c> on the same path, so it observes it, and nobody waits on it.
        /// </para>
        /// <para>
        /// Only for settings the model holds; anything else would be a round trip to learn nothing.
        /// </para>
        /// </remarks>
        private void RelearnSetting(RedisValue setting)
        {
            var name = (string?)setting;
            if (name is not ("timeout" or "databases" or "replica-read-only" or "slave-read-only")) return;
            _ = RelearnSettingAsync(name);
        }

        private async Task RelearnSettingAsync(string name)
        {
            try
            {
                foreach (var pair in await Context.Config.GetArray(name, CommandFlags.None).ConfigureAwait(false))
                {
                    RespHandshake.ApplySetting(server, pair.Key, pair.Value);
                }
            }
            catch (Exception ex)
            {
                // the change itself succeeded or failed on its own task; this only keeps the model current
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        public long CommandCount(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.CommandCountAsync(flags));

        public Task<long> CommandCountAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.CommandCountAsync(flags).AsTask(asyncState, flags);

        public RedisKey[] CommandGetKeys(RedisValue[] command, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.CommandGetKeysArray(command, flags));

        public Task<RedisKey[]> CommandGetKeysAsync(RedisValue[] command, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.CommandGetKeysArray(command, flags).AsTask(asyncState, flags);

        public string[] CommandList(RedisValue? moduleName = null, RedisValue? category = null, RedisValue? pattern = null, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.CommandListArray(moduleName, category, pattern, flags));

        public Task<string[]> CommandListAsync(RedisValue? moduleName = null, RedisValue? category = null, RedisValue? pattern = null, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.CommandListArray(moduleName, category, pattern, flags).AsTask(asyncState, flags);

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

        /// <summary><c>SCAN</c> over this server's keys, or <c>KEYS</c> in one reply where <c>SCAN</c> is unavailable.</summary>
        /// <remarks>
        /// Both shapes are one <see cref="RespScanEnumerable{T}"/>, which is also the <see cref="IScanningCursor"/> callers
        /// can cast to: <c>KEYS</c> is simply a single page at cursor zero. The <c>SCAN</c> spelling is the one the
        /// v3 cursor used - no <c>MATCH</c> for "everything", no <c>COUNT</c> for the server's default page size.
        /// </remarks>
        private RespScanEnumerable<RedisKey> KeysAsync(int database, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            database = multiplexer.ApplyDefaultDatabase(database);
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (CursorUtils.IsNil(pattern)) pattern = RedisLiterals.Wildcard;

            // The database is explicit: a server context carries none.
            var context = Context.Raw.WithDatabase(database);
            if (multiplexer.CommandMap.IsAvailable(RedisCommand.SCAN) && server.GetFeatures().Scan)
            {
                int? count = pageSize == CursorUtils.DefaultRedisPageSize ? null : pageSize;
                return new RespScanEnumerable<RedisKey>(
                    (position, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var frame = KeysScanFrame(context, position, pattern, count);
                        return context.SendAsync(ref frame, flags.WithScanCursorCategory(position), KeyScanHandler, default);
                    },
                    position =>
                    {
                        var frame = KeysScanFrame(context, position, pattern, count);
                        return context.Send(ref frame, flags.WithScanCursorCategory(position), KeyScanHandler, default);
                    },
                    cursor,
                    pageSize,
                    pageOffset,
                    default);
            }

            if (cursor != 0) throw ExceptionFactory.NoCursor(RedisCommand.KEYS);

            // KEYS is the no-SCAN fallback: every key in one reply, served as a single page at cursor zero
            var pending = new RespKeys(context).MatchingArray(pattern, flags).AsTask(asyncState, flags);
            return new RespScanEnumerable<RedisKey>(
                async (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return OnePage(await pending.ForAwait());
                },
                _ => OnePage(TransitionalSync.Wait(new ValueTask<RedisKey[]>(pending), multiplexer, null)),
                0,
                int.MaxValue,
                pageOffset,
                default);

            static RespScanPage<RedisKey> OnePage(RedisKey[]? keys)
            {
                keys ??= [];
                var lease = ReadOnlyLease<RedisKey>.Rent(keys.Length, null, out var target);
                keys.AsSpan().CopyTo(target);
                return new RespScanPage<RedisKey>(0, lease);
            }
        }

        /// <summary><c>SCAN cursor [MATCH pattern] [COUNT n]</c>, omitting <c>MATCH</c> for "everything".</summary>
        private static RespRequestFrame KeysScanFrame(RespContext context, long cursor, RedisValue pattern, int? count)
        {
            var match = CursorUtils.IsNil(pattern) ? RedisValue.Null : pattern;
            return context.Render(
                $"{RedisCommand.SCAN}{cursor}{RespLiterals.Match.When(match.HasValue)}{new OptionalValue(match)}{RespLiterals.Count.When(count)}{count}");
        }

        private static readonly RespScanPageHandler<RedisKey> KeyScanHandler = new(static (ref RespReader r) => r.ReadRedisKey());

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

        /// <summary>ROLE, per <see cref="IServer.Role(CommandFlags)"/>.</summary>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>The null-suppression preserves v3 behaviour rather than hiding a change.</b> The
        /// declared return is non-nullable and always could be null in practice: the v3 processor
        /// answered <c>null</c> for a reply it could not read - <c>SetResult(message, null!)</c> - and callers
        /// know it, <c>SentinelBase</c> reaching for <c>Role()?.Value</c>. Substituting
        /// <c>Role.Null</c> here would honour the signature and change the answer, which is a decision for
        /// whoever owns the signature, not for a port.
        /// </remarks>
        public Role Role(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.RoleAsync(flags))!;

        /// <inheritdoc cref="Role" path="/remarks"/>
        /// <param name="flags">Command flags.</param>
        public Task<Role> RoleAsync(CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.RoleAsync(flags).AsTask(asyncState, flags)!;

        public void Save(SaveType type, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Diagnostics.SaveAsync(type, flags));

        public Task SaveAsync(SaveType type, CommandFlags flags = CommandFlags.None)
            => Context.Diagnostics.SaveAsync(type, flags).AsTask(asyncState, flags);

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
            => Wait(ScriptLoadCore(script, flags));

        public Task<byte[]> ScriptLoadAsync(string script, CommandFlags flags = CommandFlags.None)
            => ScriptLoadCore(script, flags).AsTask(asyncState, flags);

        /// <summary>Load a script, and record it as loaded on this server - the v3 processor's side-effect.</summary>
        /// <remarks>
        /// The record is what lets a later <c>EVALSHA</c> skip the body here; see <c>ScriptLoadGate</c>,
        /// which keeps the same belief, keyed the same way, for the context's own evaluate path.
        /// </remarks>
        private async ValueTask<byte[]> ScriptLoadCore(string script, CommandFlags flags)
        {
            if (script is null) throw new ArgumentNullException(nameof(script));
            var hex = await Context.Scripts.LoadHex(script, flags).ConfigureAwait(false);
            if (Scripts.Sha1Bytes(hex) is not { } hash)
            {
                if ((flags & CommandFlags.FireAndForget) != 0) return Array.Empty<byte>();
                throw new RESPite.RespException("Unexpected SCRIPT LOAD reply.");
            }

            server.AddScript(script, System.Text.Encoding.ASCII.GetBytes(hex!));
            return hash;
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
            try
            {
                Wait(Context.Diagnostics.ShutdownAsync(shutdownMode, flags));
            }
            catch (RedisConnectionException ex) when (ex.FailureType == ConnectionFailureType.SocketClosed || ex.FailureType == ConnectionFailureType.SocketFailure)
            {
                // that's fine: a server that obeyed has no socket left to answer on
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

        /// <summary>GET against a named database on this server.</summary>
        /// <param name="db">The database to read from.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>The database is explicit because a server context carries none</b> - that is the whole point
        /// of one - so this is the ordinary string read through a context moved to the database the caller
        /// named, which is also what makes the <c>SELECT</c> happen where it should.
        /// </remarks>
        public RedisValue StringGet(int db, RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(new RespStrings(Context.Raw.WithDatabase(db)).GetAsync(key, flags));

        /// <inheritdoc cref="StringGet" />
        /// <param name="db">The database to read from.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public Task<RedisValue> StringGetAsync(int db, RedisKey key, CommandFlags flags = CommandFlags.None)
            => new RespStrings(Context.Raw.WithDatabase(db)).GetAsync(key, flags).AsTask(asyncState, flags);

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

        /// <summary>The two arguments of <c>REPLICAOF</c>: a primary's host and port, or <c>NO ONE</c>.</summary>
        private static void GetReplicaOfArgs(EndPoint? primaryEndpoint, out RedisValue host, out RedisValue port)
        {
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

            Wait(ReplicaOfCore(master, flags));
        }

        Task IServer.SlaveOfAsync(EndPoint master, CommandFlags flags) => ReplicaOfAsync(master, flags);

        public Task ReplicaOfAsync(EndPoint? master, CommandFlags flags = CommandFlags.None)
        {
            if (master == server.EndPoint)
            {
                throw new ArgumentException("Cannot replicate to self");
            }

            return ReplicaOfCore(master, flags).AsTask(asyncState, flags);
        }

        /// <summary>Change what this server replicates, with the bookkeeping either side of it.</summary>
        /// <remarks>
        /// <para>
        /// Before: a primary gives up its tie-breaker vote, so it stops claiming the role while replication
        /// settles - best-effort, and never on a replica, which could not accept the write anyway.
        /// </para>
        /// <para>
        /// After: a broadcast on the configuration channel, so every client listening re-reads the topology
        /// instead of discovering the change one failed write at a time. Also best-effort.
        /// </para>
        /// </remarks>
        private async ValueTask ReplicaOfCore(EndPoint? primary, CommandFlags flags)
        {
            var raw = Context.Raw;
            const CommandFlags Housekeeping = CommandFlags.FireAndForget | CommandFlags.NoRedirect;

            if (!server.IsReplica
                && multiplexer.RawConfig.TryGetTieBreaker(out var tieBreaker)
                && multiplexer.CommandMap.IsAvailable(RedisCommand.DEL))
            {
                try
                {
                    await new RespKeys(raw.WithDatabase(0)).DeleteAsync(tieBreaker, Housekeeping).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message); // we aren't depending on it
                }
            }

            GetReplicaOfArgs(primary, out var host, out var port);
            var command = server.GetFeatures().ReplicaCommands ? RedisCommand.REPLICAOF : RedisCommand.SLAVEOF;
            await raw.SendAsync($"{command}{host}{port}", flags).ConfigureAwait(false);

            if (multiplexer.ConfigurationChangedChannel is { } channel
                && multiplexer.CommandMap.IsAvailable(RedisCommand.PUBLISH))
            {
                // with the prefix the channel is SUBSCRIBED with, which the server context deliberately does not carry
                var broadcast = new RespPubSub(raw.AppendChannelPrefix(multiplexer.RawConfig.ChannelPrefix));
                await broadcast.PublishAsync(RedisChannel.Literal(channel), RedisLiterals.Wildcard, Housekeeping).ConfigureAwait(false);
            }
        }

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

        // SENTINEL: on the context, like every other server command, so that a sentinel is asked over the
        // connection the core holds to it - see SentinelCommands for the replies' shapes.
        public EndPoint? SentinelGetMasterAddressByName(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelPrimaryAddress(serviceName, flags));

        public Task<EndPoint?> SentinelGetMasterAddressByNameAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelPrimaryAddress(serviceName, flags).AsTask(asyncState, flags);

        public EndPoint[] SentinelGetSentinelAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelSentinelAddresses(serviceName, flags)) ?? [];

        public Task<EndPoint[]> SentinelGetSentinelAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelSentinelAddresses(serviceName, flags).AsTask(asyncState, flags);

        public EndPoint[] SentinelGetReplicaAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelReplicaAddresses(serviceName, Features.ReplicaCommands, flags)) ?? [];

        public Task<EndPoint[]> SentinelGetReplicaAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelReplicaAddresses(serviceName, Features.ReplicaCommands, flags).AsTask(asyncState, flags);

        public KeyValuePair<string, string>[] SentinelMaster(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelPrimary(serviceName, flags)) ?? [];

        public Task<KeyValuePair<string, string>[]> SentinelMasterAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelPrimary(serviceName, flags).AsTask(asyncState, flags);

        public void SentinelFailover(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelFailover(serviceName, flags));

        public Task SentinelFailoverAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelFailover(serviceName, flags).AsTask(asyncState, flags);

        public KeyValuePair<string, string>[][] SentinelMasters(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelPrimaries(flags)) ?? [];

        public Task<KeyValuePair<string, string>[][]> SentinelMastersAsync(CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelPrimaries(flags).AsTask(asyncState, flags);

        // For previous compat only
        KeyValuePair<string, string>[][] IServer.SentinelSlaves(string serviceName, CommandFlags flags)
            => SentinelReplicas(serviceName, flags);

        public KeyValuePair<string, string>[][] SentinelReplicas(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelReplicas(serviceName, Features.ReplicaCommands, flags)) ?? [];

        // For previous compat only
        Task<KeyValuePair<string, string>[][]> IServer.SentinelSlavesAsync(string serviceName, CommandFlags flags)
            => SentinelReplicasAsync(serviceName, flags);

        public Task<KeyValuePair<string, string>[][]> SentinelReplicasAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelReplicas(serviceName, Features.ReplicaCommands, flags).AsTask(asyncState, flags);

        public KeyValuePair<string, string>[][] SentinelSentinels(string serviceName, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Raw.SentinelSentinels(serviceName, flags)) ?? [];

        public Task<KeyValuePair<string, string>[][]> SentinelSentinelsAsync(string serviceName, CommandFlags flags = CommandFlags.None)
            => Context.Raw.SentinelSentinels(serviceName, flags).AsTask(asyncState, flags);

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
                && CommandFlagsInternal.RequiresDatabase(known)
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
        /// not a path anybody pipelines. The v3 spelling built a <c>RedisValue[]</c> here too, with
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
