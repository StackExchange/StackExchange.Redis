﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial.Arenas;
using static StackExchange.Redis.ConnectionMultiplexer;

#pragma warning disable RCS1231 // Make parameter ref read-only.

namespace StackExchange.Redis
{
    internal sealed class RedisServer : RedisBase, IServer
    {
        private readonly ServerEndPoint server;

        internal RedisServer(ConnectionMultiplexer multiplexer, ServerEndPoint server, object asyncState) : base(multiplexer, asyncState)
        {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
        }

        int IServer.DatabaseCount => server.Databases;

        public ClusterConfiguration ClusterConfiguration => server.ClusterConfiguration;

        public EndPoint EndPoint => server.EndPoint;

        public RedisFeatures Features => server.GetFeatures();

        public bool IsConnected => server.IsConnected;

        bool IServer.IsSlave => IsReplica;
        public bool IsReplica => server.IsReplica;

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

        public Version Version => server.Version;

        public void ClientKill(EndPoint endpoint, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.KILL, (RedisValue)Format.ToString(endpoint));
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ClientKillAsync(EndPoint endpoint, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.KILL, (RedisValue)Format.ToString(endpoint));
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public long ClientKill(long? id = null, ClientType? clientType = null, EndPoint endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClientKillMessage(endpoint, id, clientType, skipMe, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> ClientKillAsync(long? id = null, ClientType? clientType = null, EndPoint endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None)
        {
            var msg = GetClientKillMessage(endpoint, id, clientType, skipMe, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        private Message GetClientKillMessage(EndPoint endpoint, long? id, ClientType? clientType, bool skipMe, CommandFlags flags)
        {
            var parts = new List<RedisValue>(9)
            {
                RedisLiterals.KILL
            };
            if (id != null)
            {
                parts.Add(RedisLiterals.ID);
                parts.Add(id.Value);
            }
            if (clientType != null)
            {
                parts.Add(RedisLiterals.TYPE);
                switch (clientType.Value)
                {
                    case ClientType.Normal:
                        parts.Add(RedisLiterals.normal);
                        break;
                    case ClientType.Replica:
                        parts.Add(Features.ReplicaCommands ? RedisLiterals.replica : RedisLiterals.slave);
                        break;
                    case ClientType.PubSub:
                        parts.Add(RedisLiterals.pubsub);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(clientType));
                }
                parts.Add(id.Value);
            }
            if (endpoint != null)
            {
                parts.Add(RedisLiterals.ADDR);
                parts.Add((RedisValue)Format.ToString(endpoint));
            }
            if (!skipMe)
            {
                parts.Add(RedisLiterals.SKIPME);
                parts.Add(RedisLiterals.no);
            }
            return Message.Create(-1, flags, RedisCommand.CLIENT, parts);
        }

        public ClientInfo[] ClientList(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.LIST);
            return ExecuteSync(msg, ClientInfo.Processor);
        }

        public Task<ClientInfo[]> ClientListAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.LIST);
            return ExecuteAsync(msg, ClientInfo.Processor);
        }

        public ClusterConfiguration ClusterNodes(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
            return ExecuteSync(msg, ResultProcessor.ClusterNodes);
        }

        public Task<ClusterConfiguration> ClusterNodesAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
            return ExecuteAsync(msg, ResultProcessor.ClusterNodes);
        }

        public string ClusterNodesRaw(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
            return ExecuteSync(msg, ResultProcessor.ClusterNodesRaw);
        }

        public Task<string> ClusterNodesRawAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
            return ExecuteAsync(msg, ResultProcessor.ClusterNodesRaw);
        }

        public KeyValuePair<string, string>[] ConfigGet(RedisValue pattern = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            if (pattern.IsNullOrEmpty) pattern = RedisLiterals.Wildcard;
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, pattern);
            return ExecuteSync(msg, ResultProcessor.StringPairInterleaved);
        }

        public Task<KeyValuePair<string, string>[]> ConfigGetAsync(RedisValue pattern = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            if (pattern.IsNullOrEmpty) pattern = RedisLiterals.Wildcard;
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, pattern);
            return ExecuteAsync(msg, ResultProcessor.StringPairInterleaved);
        }

        public void ConfigResetStatistics(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.RESETSTAT);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ConfigResetStatisticsAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.RESETSTAT);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public void ConfigRewrite(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.REWRITE);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ConfigRewriteAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.REWRITE);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

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

        public long DatabaseSize(int database = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(multiplexer.ApplyDefaultDatabase(database), flags, RedisCommand.DBSIZE);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> DatabaseSizeAsync(int database = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(multiplexer.ApplyDefaultDatabase(database), flags, RedisCommand.DBSIZE);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public RedisValue Echo(RedisValue message, CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.ECHO, message);
            return ExecuteSync(msg, ResultProcessor.RedisValue);
        }

        public Task<RedisValue> EchoAsync(RedisValue message, CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.ECHO, message);
            return ExecuteAsync(msg, ResultProcessor.RedisValue);
        }

        public void FlushAllDatabases(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.FLUSHALL);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task FlushAllDatabasesAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.FLUSHALL);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public void FlushDatabase(int database = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(multiplexer.ApplyDefaultDatabase(database), flags, RedisCommand.FLUSHDB);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task FlushDatabaseAsync(int database = -1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(multiplexer.ApplyDefaultDatabase(database), flags, RedisCommand.FLUSHDB);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public ServerCounters GetCounters() => server.GetCounters();

        public IGrouping<string, KeyValuePair<string, string>>[] Info(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            var msg = section.IsNullOrEmpty
                ? Message.Create(-1, flags, RedisCommand.INFO)
                : Message.Create(-1, flags, RedisCommand.INFO, section);

            return ExecuteSync(msg, ResultProcessor.Info);
        }

        public Task<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            var msg = section.IsNullOrEmpty
                ? Message.Create(-1, flags, RedisCommand.INFO)
                : Message.Create(-1, flags, RedisCommand.INFO, section);

            return ExecuteAsync(msg, ResultProcessor.Info);
        }

        public string InfoRaw(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            var msg = section.IsNullOrEmpty
                ? Message.Create(-1, flags, RedisCommand.INFO)
                : Message.Create(-1, flags, RedisCommand.INFO, section);

            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<string> InfoRawAsync(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None)
        {
            var msg = section.IsNullOrEmpty
                ? Message.Create(-1, flags, RedisCommand.INFO)
                : Message.Create(-1, flags, RedisCommand.INFO, section);

            return ExecuteAsync(msg, ResultProcessor.String);
        }

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
            return CursorEnumerable<RedisKey>.From(this, server, ExecuteAsync(msg, ResultProcessor.RedisKeyArray), pageOffset);
        }

        public DateTime LastSave(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LASTSAVE);
            return ExecuteSync(msg, ResultProcessor.DateTime);
        }

        public Task<DateTime> LastSaveAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LASTSAVE);
            return ExecuteAsync(msg, ResultProcessor.DateTime);
        }

        public void MakeMaster(ReplicationChangeOptions options, TextWriter log = null)
        {
            using (var proxy = LogProxy.TryCreate(log))
            {
                multiplexer.MakeMaster(server, options, proxy);
            }
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
        {
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.EXISTS, ScriptHash.Hash(script));
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public bool ScriptExists(byte[] sha1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.EXISTS, ScriptHash.Encode(sha1));
            return ExecuteSync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> ScriptExistsAsync(string script, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.EXISTS, ScriptHash.Hash(script));
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public Task<bool> ScriptExistsAsync(byte[] sha1, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.EXISTS, ScriptHash.Encode(sha1));
            return ExecuteAsync(msg, ResultProcessor.Boolean);
        }

        public void ScriptFlush(CommandFlags flags = CommandFlags.None)
        {
            if (!multiplexer.RawConfig.AllowAdmin) throw ExceptionFactory.AdminModeNotEnabled(multiplexer.IncludeDetailInExceptions, RedisCommand.SCRIPT, null, server);
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.FLUSH);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task ScriptFlushAsync(CommandFlags flags = CommandFlags.None)
        {
            if (!multiplexer.RawConfig.AllowAdmin) throw ExceptionFactory.AdminModeNotEnabled(multiplexer.IncludeDetailInExceptions, RedisCommand.SCRIPT, null, server);
            var msg = Message.Create(-1, flags, RedisCommand.SCRIPT, RedisLiterals.FLUSH);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public byte[] ScriptLoad(string script, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ScriptLoadMessage(flags, script);
            return ExecuteSync(msg, ResultProcessor.ScriptLoad);
        }

        public Task<byte[]> ScriptLoadAsync(string script, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ScriptLoadMessage(flags, script);
            return ExecuteAsync(msg, ResultProcessor.ScriptLoad);
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
            Message msg;
            switch (shutdownMode)
            {
                case ShutdownMode.Default:
                    msg = Message.Create(-1, flags, RedisCommand.SHUTDOWN);
                    break;
                case ShutdownMode.Always:
                    msg = Message.Create(-1, flags, RedisCommand.SHUTDOWN, RedisLiterals.SAVE);
                    break;
                case ShutdownMode.Never:
                    msg = Message.Create(-1, flags, RedisCommand.SHUTDOWN, RedisLiterals.NOSAVE);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shutdownMode));
            }
            try
            {
                ExecuteSync(msg, ResultProcessor.DemandOK);
            }
            catch (RedisConnectionException ex)
            {
                switch (ex.FailureType)
                {
                    case ConnectionFailureType.SocketClosed:
                    case ConnectionFailureType.SocketFailure:
                        // that's fine
                        return;
                }
                throw; // otherwise, not something we were expecting
            }
        }

        public CommandTrace[] SlowlogGet(int count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = count > 0
                ? Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.GET, count)
                : Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.GET);

            return ExecuteSync(msg, CommandTrace.Processor);
        }

        public Task<CommandTrace[]> SlowlogGetAsync(int count = 0, CommandFlags flags = CommandFlags.None)
        {
            var msg = count > 0
                ? Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.GET, count)
                : Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.GET);

            return ExecuteAsync(msg, CommandTrace.Processor);
        }

        public void SlowlogReset(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.RESET);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task SlowlogResetAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SLOWLOG, RedisLiterals.RESET);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

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

        public RedisChannel[] SubscriptionChannels(RedisChannel pattern = default(RedisChannel), CommandFlags flags = CommandFlags.None)
        {
            var msg = pattern.IsNullOrEmpty ? Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.CHANNELS)
                : Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.CHANNELS, pattern);
            return ExecuteSync(msg, ResultProcessor.RedisChannelArrayLiteral);
        }

        public Task<RedisChannel[]> SubscriptionChannelsAsync(RedisChannel pattern = default(RedisChannel), CommandFlags flags = CommandFlags.None)
        {
            var msg = pattern.IsNullOrEmpty ? Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.CHANNELS)
                : Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.CHANNELS, pattern);
            return ExecuteAsync(msg, ResultProcessor.RedisChannelArrayLiteral);
        }

        public long SubscriptionPatternCount(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMPAT);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<long> SubscriptionPatternCountAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMPAT);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long SubscriptionSubscriberCount(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            return ExecuteSync(msg, ResultProcessor.PubSubNumSub);
        }

        public Task<long> SubscriptionSubscriberCountAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.PUBSUB, RedisLiterals.NUMSUB, channel);
            return ExecuteAsync(msg, ResultProcessor.PubSubNumSub);
        }

        public void SwapDatabases(int first, int second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SWAPDB, first, second);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task SwapDatabasesAsync(int first, int second, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SWAPDB, first, second);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public DateTime Time(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.TIME);
            return ExecuteSync(msg, ResultProcessor.DateTime);
        }

        public Task<DateTime> TimeAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.TIME);
            return ExecuteAsync(msg, ResultProcessor.DateTime);
        }

        internal static Message CreateReplicaOfMessage(ServerEndPoint sendMessageTo, EndPoint masterEndpoint, CommandFlags flags = CommandFlags.None)
        {
            RedisValue host, port;
            if (masterEndpoint == null)
            {
                host = "NO";
                port = "ONE";
            }
            else
            {
                if (Format.TryGetHostPort(masterEndpoint, out string hostRaw, out int portRaw))
                {
                    host = hostRaw;
                    port = portRaw;
                }
                else
                {
                    throw new NotSupportedException("Unknown endpoint type: " + masterEndpoint.GetType().Name);
                }
            }
            return Message.Create(-1, flags, sendMessageTo.GetFeatures().ReplicaCommands ? RedisCommand.REPLICAOF : RedisCommand.SLAVEOF, host, port);
        }

        internal override Task<T> ExecuteAsync<T>(Message message, ResultProcessor<T> processor, ServerEndPoint server = null)
        {   // inject our expected server automatically
            if (server == null) server = this.server;
            FixFlags(message, server);
            if (!server.IsConnected)
            {
                if (message == null) return CompletedTask<T>.Default(asyncState);
                if (message.IsFireAndForget) return CompletedTask<T>.Default(null); // F+F explicitly does not get async-state

                // no need to deny exec-sync here; will be complete before they see if
                var tcs = TaskSource.Create<T>(asyncState);
                ConnectionMultiplexer.ThrowFailed(tcs, ExceptionFactory.NoConnectionAvailable(multiplexer, message, server));
                return tcs.Task;
            }
            return base.ExecuteAsync<T>(message, processor, server);
        }

        internal override T ExecuteSync<T>(Message message, ResultProcessor<T> processor, ServerEndPoint server = null)
        {   // inject our expected server automatically
            if (server == null) server = this.server;
            FixFlags(message, server);
            if (!server.IsConnected)
            {
                if (message == null || message.IsFireAndForget) return default(T);
                throw ExceptionFactory.NoConnectionAvailable(multiplexer, message, server);
            }
            return base.ExecuteSync<T>(message, processor, server);
        }

        internal override RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, out ServerEndPoint server)
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
            // prepare the actual replicaof message (not sent yet)
            var replicaOfMsg = CreateReplicaOfMessage(server, master, flags);

            var configuration = multiplexer.RawConfig;

            // attempt to cease having an opinion on the master; will resume that when replication completes
            // (note that this may fail; we aren't depending on it)
            if (!string.IsNullOrWhiteSpace(configuration.TieBreaker)
                && multiplexer.CommandMap.IsAvailable(RedisCommand.DEL))
            {
                var del = Message.Create(0, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.DEL, (RedisKey)configuration.TieBreaker);
                del.SetInternalCall();
#pragma warning disable CS0618
                server.WriteDirectFireAndForgetSync(del, ResultProcessor.Boolean);
#pragma warning restore CS0618
            }
            ExecuteSync(replicaOfMsg, ResultProcessor.DemandOK);

            // attempt to broadcast a reconfigure message to anybody listening to this server
            var channel = multiplexer.ConfigurationChangedChannel;
            if (channel != null && multiplexer.CommandMap.IsAvailable(RedisCommand.PUBLISH))
            {
                var pub = Message.Create(-1, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.PUBLISH, (RedisValue)channel, RedisLiterals.Wildcard);
                pub.SetInternalCall();
#pragma warning disable CS0618
                server.WriteDirectFireAndForgetSync(pub, ResultProcessor.Int64);
#pragma warning restore CS0618
            }
        }

        Task IServer.SlaveOfAsync(EndPoint master, CommandFlags flags) => ReplicaOfAsync(master, flags);

        public Task ReplicaOfAsync(EndPoint master, CommandFlags flags = CommandFlags.None)
        {
            var msg = CreateReplicaOfMessage(server, master, flags);
            if (master == server.EndPoint)
            {
                throw new ArgumentException("Cannot replicate to self");
            }
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        private void FixFlags(Message message, ServerEndPoint server)
        {
            // since the server is specified explicitly, we don't want defaults
            // to make the "non-preferred-endpoint" counters look artificially
            // inflated; note we only change *prefer* options
            switch (Message.GetMasterReplicaFlags(message.Flags))
            {
                case CommandFlags.PreferMaster:
                    if (server.IsReplica) message.SetPreferReplica();
                    break;
                case CommandFlags.PreferReplica:
                    if (!server.IsReplica) message.SetPreferMaster();
                    break;
            }
        }

        private Message GetSaveMessage(SaveType type, CommandFlags flags = CommandFlags.None)
        {
            switch (type)
            {
                case SaveType.BackgroundRewriteAppendOnlyFile: return Message.Create(-1, flags, RedisCommand.BGREWRITEAOF);
                case SaveType.BackgroundSave: return Message.Create(-1, flags, RedisCommand.BGSAVE);
#pragma warning disable 0618
                case SaveType.ForegroundSave: return Message.Create(-1, flags, RedisCommand.SAVE);
#pragma warning restore 0618
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private ResultProcessor<bool> GetSaveResultProcessor(SaveType type)
        {
            switch (type)
            {
                case SaveType.BackgroundRewriteAppendOnlyFile: return ResultProcessor.DemandOK;
                case SaveType.BackgroundSave: return ResultProcessor.BackgroundSaveStarted;
#pragma warning disable 0618
                case SaveType.ForegroundSave: return ResultProcessor.DemandOK;
#pragma warning restore 0618
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static class ScriptHash
        {
            public static RedisValue Encode(byte[] value)
            {
                const string hex = "0123456789abcdef";
                if (value == null) return default(RedisValue);
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
                if (value == null) return default(RedisValue);
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
            private class ScanResultProcessor : ResultProcessor<ScanResult>
            {
                protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
                {
                    switch (result.Type)
                    {
                        case ResultType.MultiBulk:
                            var arr = result.GetItems();
                            RawResult inner;
                            if (arr.Length == 2 && (inner = arr[1]).Type == ResultType.MultiBulk)
                            {
                                var items = inner.GetItems();
                                RedisKey[] keys;
                                int count;
                                if (items.IsEmpty)
                                {
                                    keys = Array.Empty<RedisKey>();
                                    count = 0;
                                }
                                else
                                {
                                    count = (int)items.Length;
                                    keys = ArrayPool<RedisKey>.Shared.Rent(count);
                                    items.CopyTo(keys, (in RawResult r) => r.AsRedisKey());
                                }
                                var keysResult = new ScanResult(arr[0].AsRedisValue(), keys, count, true);
                                SetResult(message, keysResult);
                                return true;
                            }
                            break;
                    }
                    return false;
                }
            }
        }

        #region Sentinel

        public EndPoint SentinelGetMasterAddressByName(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.GETMASTERADDRBYNAME, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.SentinelMasterEndpoint);
        }

        public Task<EndPoint> SentinelGetMasterAddressByNameAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.GETMASTERADDRBYNAME, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.SentinelMasterEndpoint);
        }

        public EndPoint[] SentinelGetSentinelAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SENTINELS, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.SentinelAddressesEndPoints);
        }

        public Task<EndPoint[]> SentinelGetSentinelAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SENTINELS, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.SentinelAddressesEndPoints);
        }

        public EndPoint[] SentinelGetReplicaAddresses(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SLAVES, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.SentinelAddressesEndPoints);
        }

        public Task<EndPoint[]> SentinelGetReplicaAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SLAVES, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.SentinelAddressesEndPoints);
        }

        public KeyValuePair<string, string>[] SentinelMaster(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTER, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.StringPairInterleaved);
        }

        public Task<KeyValuePair<string, string>[]> SentinelMasterAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTER, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.StringPairInterleaved);
        }

        public void SentinelFailover(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.FAILOVER, (RedisValue)serviceName);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task SentinelFailoverAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.FAILOVER, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public KeyValuePair<string, string>[][] SentinelMasters(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTERS);
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        public Task<KeyValuePair<string, string>[][]> SentinelMastersAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTERS);
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        // For previous compat only
        KeyValuePair<string, string>[][] IServer.SentinelSlaves(string serviceName, CommandFlags flags)
            => SentinelReplicas(serviceName, flags);

        public KeyValuePair<string, string>[][] SentinelReplicas(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            // note: sentinel does not have "replicas" terminology at the current time
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SLAVES, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        // For previous compat only
        Task<KeyValuePair<string, string>[][]> IServer.SentinelSlavesAsync(string serviceName, CommandFlags flags)
            => SentinelReplicasAsync(serviceName, flags);

        public Task<KeyValuePair<string, string>[][]> SentinelReplicasAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            // note: sentinel does not have "replicas" terminology at the current time
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SLAVES, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        public KeyValuePair<string, string>[][] SentinelSentinels(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SENTINELS, (RedisValue)serviceName);
            return ExecuteSync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        public Task<KeyValuePair<string, string>[][]> SentinelSentinelsAsync(string serviceName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.SENTINELS, (RedisValue)serviceName);
            return ExecuteAsync(msg, ResultProcessor.SentinelArrayOfArrays);
        }

        #endregion

        public RedisResult Execute(string command, params object[] args) => Execute(command, args, CommandFlags.None);

        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ExecuteMessage(multiplexer?.CommandMap, -1, flags, command, args);
            return ExecuteSync(msg, ResultProcessor.ScriptResult);
        }

        public Task<RedisResult> ExecuteAsync(string command, params object[] args) => ExecuteAsync(command, args, CommandFlags.None);

        public Task<RedisResult> ExecuteAsync(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
        {
            var msg = new RedisDatabase.ExecuteMessage(multiplexer?.CommandMap, -1, flags, command, args);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        /// <summary>
        /// For testing only
        /// </summary>
        internal void SimulateConnectionFailure() => server.SimulateConnectionFailure();

        public Task<string> LatencyDoctorAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.DOCTOR);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public string LatencyDoctor(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.DOCTOR);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        private static Message LatencyResetCommand(string[] eventNames, CommandFlags flags)
        {
            if (eventNames == null) eventNames = Array.Empty<string>();
            switch (eventNames.Length)
            {
                case 0:
                    return Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.RESET);
                case 1:
                    return Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.RESET, (RedisValue)eventNames[0]);
                default:
                    var arr = new RedisValue[eventNames.Length + 1];
                    arr[0] = RedisLiterals.RESET;
                    for (int i = 0; i < eventNames.Length; i++)
                        arr[i + 1] = eventNames[i];
                    return Message.Create(-1, flags, RedisCommand.LATENCY, arr);

            }
        }
        public Task<long> LatencyResetAsync(string[] eventNames = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = LatencyResetCommand(eventNames, flags);
            return ExecuteAsync(msg, ResultProcessor.Int64);
        }

        public long LatencyReset(string[] eventNames = null, CommandFlags flags = CommandFlags.None)
        {
            var msg = LatencyResetCommand(eventNames, flags);
            return ExecuteSync(msg, ResultProcessor.Int64);
        }

        public Task<LatencyHistoryEntry[]> LatencyHistoryAsync(string eventName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.HISTORY, (RedisValue)eventName);
            return ExecuteAsync(msg, LatencyHistoryEntry.ToArray);
        }

        public LatencyHistoryEntry[] LatencyHistory(string eventName, CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.HISTORY, (RedisValue)eventName);
            return ExecuteSync(msg, LatencyHistoryEntry.ToArray);
        }

        public Task<LatencyLatestEntry[]> LatencyLatestAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.LATEST);
            return ExecuteAsync(msg, LatencyLatestEntry.ToArray);
        }

        public LatencyLatestEntry[] LatencyLatest(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.LATENCY, RedisLiterals.LATEST);
            return ExecuteSync(msg, LatencyLatestEntry.ToArray);
        }

        public Task<string> MemoryDoctorAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.DOCTOR);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public string MemoryDoctor(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.DOCTOR);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task MemoryPurgeAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.PURGE);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public void MemoryPurge(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.PURGE);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task<string> MemoryAllocatorStatsAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.MALLOC_STATS);
            return ExecuteAsync(msg, ResultProcessor.String);
        }

        public string MemoryAllocatorStats(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.MALLOC_STATS);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        public Task<RedisResult> MemoryStatsAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.STATS);
            return ExecuteAsync(msg, ResultProcessor.ScriptResult);
        }

        public RedisResult MemoryStats(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.MEMORY, RedisLiterals.STATS);
            return ExecuteSync(msg, ResultProcessor.ScriptResult);
        }
    }
}
