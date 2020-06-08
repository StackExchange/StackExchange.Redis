﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Provides configuration controls of a redis server
    /// </summary>
    public partial interface IServer : IRedis
    {
        /// <summary>
        /// Gets the cluster configuration associated with this server, if known
        /// </summary>
        ClusterConfiguration ClusterConfiguration { get; }

        /// <summary>
        /// Gets the address of the connected server
        /// </summary>
        EndPoint EndPoint { get; }

        /// <summary>
        /// Gets the features available to the connected server
        /// </summary>
        RedisFeatures Features { get; }

        /// <summary>
        /// Gets whether the connection to the server is active and usable
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets whether the connected server is a replica
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(IsReplica) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        bool IsSlave { get; }

        /// <summary>
        /// Gets whether the connected server is a replica
        /// </summary>
        bool IsReplica { get; }

        /// <summary>
        /// Explicitly opt in for replica writes on writable replica
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(AllowReplicaWrites) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        bool AllowSlaveWrites { get; set; }

        /// <summary>
        /// Explicitly opt in for replica writes on writable replica
        /// </summary>
        bool AllowReplicaWrites { get; set; }

        /// <summary>
        /// Gets the operating mode of the connected server
        /// </summary>
        ServerType ServerType { get; }

        /// <summary>
        /// Gets the version of the connected server
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// The number of databases supported on this server
        /// </summary>
        int DatabaseCount { get; }

        /// <summary>
        /// The CLIENT KILL command closes a given client connection identified by ip:port.
        /// The ip:port should match a line returned by the CLIENT LIST command.
        /// Due to the single-treaded nature of Redis, it is not possible to kill a client connection while it is executing a command.From the client point of view, the connection can never be closed in the middle of the execution of a command.However, the client will notice the connection has been closed only when the next command is sent (and results in network error).
        /// </summary>
        /// <param name="endpoint">The endpoint of the client to kill.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/client-kill</remarks>
        void ClientKill(EndPoint endpoint, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CLIENT KILL command closes a given client connection identified by ip:port.
        /// The ip:port should match a line returned by the CLIENT LIST command.
        /// Due to the single-treaded nature of Redis, it is not possible to kill a client connection while it is executing a command.From the client point of view, the connection can never be closed in the middle of the execution of a command.However, the client will notice the connection has been closed only when the next command is sent (and results in network error).
        /// </summary>
        /// <param name="endpoint">The endpoint of the client to kill.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/client-kill</remarks>
        Task ClientKillAsync(EndPoint endpoint, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CLIENT KILL command closes multiple connections that match the specified filters
        /// </summary>
        /// <param name="id">The ID of the client to kill.</param>
        /// <param name="clientType">The type of client.</param>
        /// <param name="endpoint">The endpoint to kill.</param>
        /// <param name="skipMe">Whether to kskip the current connection.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of clients killed.</returns>
        /// <remarks>https://redis.io/commands/client-kill</remarks>
        long ClientKill(long? id = null, ClientType? clientType = null, EndPoint endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CLIENT KILL command closes multiple connections that match the specified filters
        /// </summary>
        /// <param name="id">The ID of the client to kill.</param>
        /// <param name="clientType">The type of client.</param>
        /// <param name="endpoint">The endpoint to kill.</param>
        /// <param name="skipMe">Whether to kskip the current connection.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of clients killed.</returns>
        /// <remarks>https://redis.io/commands/client-kill</remarks>
        Task<long> ClientKillAsync(long? id = null, ClientType? clientType = null, EndPoint endpoint = null, bool skipMe = true, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CLIENT LIST command returns information and statistics about the client connections server in a mostly human readable format.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/client-list</remarks>
        ClientInfo[] ClientList(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CLIENT LIST command returns information and statistics about the client connections server in a mostly human readable format.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/client-list</remarks>
        Task<ClientInfo[]> ClientListAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Obtains the current CLUSTER NODES output from a cluster server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        ClusterConfiguration ClusterNodes(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Obtains the current CLUSTER NODES output from a cluster server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        Task<ClusterConfiguration> ClusterNodesAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Obtains the current raw CLUSTER NODES output from a cluster server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        string ClusterNodesRaw(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Obtains the current raw CLUSTER NODES output from a cluster server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        Task<string> ClusterNodesRawAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get all configuration parameters matching the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern of config values to get.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>All matching configuration parameters.</returns>
        /// <remarks>https://redis.io/commands/config-get</remarks>
        KeyValuePair<string, string>[] ConfigGet(RedisValue pattern = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get all configuration parameters matching the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern of config values to get.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>All matching configuration parameters.</returns>
        /// <remarks>https://redis.io/commands/config-get</remarks>
        Task<KeyValuePair<string, string>[]> ConfigGetAsync(RedisValue pattern = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Resets the statistics reported by Redis using the INFO command.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-resetstat</remarks>
        void ConfigResetStatistics(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Resets the statistics reported by Redis using the INFO command.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-resetstat</remarks>
        Task ConfigResetStatisticsAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CONFIG REWRITE command rewrites the redis.conf file the server was started with, applying the minimal changes needed to make it reflecting the configuration currently used by the server, that may be different compared to the original one because of the use of the CONFIG SET command.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-rewrite</remarks>
        void ConfigRewrite(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CONFIG REWRITE command rewrites the redis.conf file the server was started with, applying the minimal changes needed to make it reflecting the configuration currently used by the server, that may be different compared to the original one because of the use of the CONFIG SET command.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-rewrite</remarks>
        Task ConfigRewriteAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CONFIG SET command is used in order to reconfigure the server at runtime without the need to restart Redis. You can change both trivial parameters or switch from one to another persistence option using this command.
        /// </summary>
        /// <param name="setting">The setting name.</param>
        /// <param name="value">The new setting value.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-set</remarks>
        void ConfigSet(RedisValue setting, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The CONFIG SET command is used in order to reconfigure the server at runtime without the need to restart Redis. You can change both trivial parameters or switch from one to another persistence option using this command.
        /// </summary>
        /// <param name="setting">The setting name.</param>
        /// <param name="value">The new setting value.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/config-set</remarks>
        Task ConfigSetAsync(RedisValue setting, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the number of keys in the database.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/dbsize</remarks>
        long DatabaseSize(int database = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the number of keys in the database.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/dbsize</remarks>
        Task<long> DatabaseSizeAsync(int database = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the same message passed in.
        /// </summary>
        /// <param name="message">The message to echo.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/echo</remarks>
        RedisValue Echo(RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the same message passed in.
        /// </summary>
        /// <param name="message">The message to echo.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/echo</remarks>
        Task<RedisValue> EchoAsync(RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        RedisResult Execute(string command, params object[] args);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        Task<RedisResult> ExecuteAsync(string command, params object[] args);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        Task<RedisResult> ExecuteAsync(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete all the keys of all databases on the server.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/flushall</remarks>
        void FlushAllDatabases(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete all the keys of all databases on the server.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/flushall</remarks>
        Task FlushAllDatabasesAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete all the keys of the database.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/flushdb</remarks>
        void FlushDatabase(int database = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete all the keys of the database.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/flushdb</remarks>
        Task FlushDatabaseAsync(int database = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get summary statistics associates with this server
        /// </summary>
        ServerCounters GetCounters();
        /// <summary>
        /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
        /// </summary>
        /// <param name="section">The info section to get, if getting a specific one.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/info</remarks>
        IGrouping<string, KeyValuePair<string, string>>[] Info(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
        /// </summary>
        /// <param name="section">The info section to get, if getting a specific one.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/info</remarks>
        Task<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
        /// </summary>
        /// <param name="section">The info section to get, if getting a specific one.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/info</remarks>
        string InfoRaw(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
        /// </summary>
        /// <param name="section">The info section to get, if getting a specific one.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/info</remarks>
        Task<string> InfoRawAsync(RedisValue section = default(RedisValue), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns all keys matching pattern; the KEYS or SCAN commands will be used based on the server capabilities.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="pattern">The pattern to use.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>Warning: consider KEYS as a command that should only be used in production environments with extreme care.</remarks>
        /// <remarks>https://redis.io/commands/keys</remarks>
        /// <remarks>https://redis.io/commands/scan</remarks>
        IEnumerable<RedisKey> Keys(int database, RedisValue pattern, int pageSize, CommandFlags flags);

        /// <summary>
        /// Returns all keys matching pattern; the KEYS or SCAN commands will be used based on the server capabilities; note: to resume an iteration via <i>cursor</i>, cast the original enumerable or enumerator to <i>IScanningCursor</i>.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="pattern">The pattern to use.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="cursor">The cursor position to resume at.</param>
        /// <param name="pageOffset">The page offset to start at.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>Warning: consider KEYS as a command that should only be used in production environments with extreme care.</remarks>
        /// <remarks>https://redis.io/commands/keys</remarks>
        /// <remarks>https://redis.io/commands/scan</remarks>
        IEnumerable<RedisKey> Keys(int database = -1, RedisValue pattern = default(RedisValue), int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns all keys matching pattern; the KEYS or SCAN commands will be used based on the server capabilities; note: to resume an iteration via <i>cursor</i>, cast the original enumerable or enumerator to <i>IScanningCursor</i>.
        /// </summary>
        /// <param name="database">The database ID.</param>
        /// <param name="pattern">The pattern to use.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="cursor">The cursor position to resume at.</param>
        /// <param name="pageOffset">The page offset to start at.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>Warning: consider KEYS as a command that should only be used in production environments with extreme care.</remarks>
        /// <remarks>https://redis.io/commands/keys</remarks>
        /// <remarks>https://redis.io/commands/scan</remarks>
        IAsyncEnumerable<RedisKey> KeysAsync(int database = -1, RedisValue pattern = default(RedisValue), int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the time of the last DB save executed with success. A client may check if a BGSAVE command succeeded reading the LASTSAVE value, then issuing a BGSAVE command and checking at regular intervals every N seconds if LASTSAVE changed.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/lastsave</remarks>
        DateTime LastSave(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the time of the last DB save executed with success. A client may check if a BGSAVE command succeeded reading the LASTSAVE value, then issuing a BGSAVE command and checking at regular intervals every N seconds if LASTSAVE changed.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/lastsave</remarks>
        Task<DateTime> LastSaveAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Promote the selected node to be master
        /// </summary>
        /// <param name="options">The options to use for this topology change.</param>
        /// <param name="log">The log to write output to.</param>
        void MakeMaster(ReplicationChangeOptions options, TextWriter log = null);

        /// <summary>
        /// Explicitly request the database to persist the current state to disk
        /// </summary>
        /// <param name="type">The method of the save (e.g. background or foreground).</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/bgrewriteaof</remarks>
        /// <remarks>https://redis.io/commands/bgsave</remarks>
        /// <remarks>https://redis.io/commands/save</remarks>
        /// <remarks>https://redis.io/topics/persistence</remarks>
        void Save(SaveType type, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Explicitly request the database to persist the current state to disk
        /// </summary>
        /// <param name="type">The method of the save (e.g. background or foreground).</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/bgrewriteaof</remarks>
        /// <remarks>https://redis.io/commands/bgsave</remarks>
        /// <remarks>https://redis.io/commands/save</remarks>
        /// <remarks>https://redis.io/topics/persistence</remarks>
        Task SaveAsync(SaveType type, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inidicates whether the specified script is defined on the server
        /// </summary>
        /// <param name="script">The text of the script to check for on the server.</param>
        /// <param name="flags">The command flags to use.</param>
        bool ScriptExists(string script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inidicates whether the specified script hash is defined on the server
        /// </summary>
        /// <param name="sha1">The SHA1 of the script to check for on the server.</param>
        /// <param name="flags">The command flags to use.</param>
        bool ScriptExists(byte[] sha1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inidicates whether the specified script is defined on the server
        /// </summary>
        /// <param name="script">The text of the script to check for on the server.</param>
        /// <param name="flags">The command flags to use.</param>
        Task<bool> ScriptExistsAsync(string script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inidicates whether the specified script hash is defined on the server
        /// </summary>
        /// <param name="sha1">The SHA1 of the script to check for on the server.</param>
        /// <param name="flags">The command flags to use.</param>
        Task<bool> ScriptExistsAsync(byte[] sha1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes all cached scripts on this server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        void ScriptFlush(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes all cached scripts on this server
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        Task ScriptFlushAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Explicitly defines a script on the server
        /// </summary>
        /// <param name="script">The script to load.</param>
        /// <param name="flags">The command flags to use.</param>
        byte[] ScriptLoad(string script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Explicitly defines a script on the server
        /// </summary>
        /// <param name="script">The script to load.</param>
        /// <param name="flags">The command flags to use.</param>
        LoadedLuaScript ScriptLoad(LuaScript script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Explicitly defines a script on the server
        /// </summary>
        /// <param name="script">The script to load.</param>
        /// <param name="flags">The command flags to use.</param>
        Task<byte[]> ScriptLoadAsync(string script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Explicitly defines a script on the server
        /// </summary>
        /// <param name="script">The script to load.</param>
        /// <param name="flags">The command flags to use.</param>
        Task<LoadedLuaScript> ScriptLoadAsync(LuaScript script, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Asks the redis server to shutdown, killing all connections. Please FULLY read the notes on the SHUTDOWN command.
        /// </summary>
        /// <param name="shutdownMode">The mode of the shutdown.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/shutdown</remarks>
        void Shutdown(ShutdownMode shutdownMode = ShutdownMode.Default, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The REPLICAOF command can change the replication settings of a replica on the fly. If a Redis server is already acting as replica, specifying a null master will turn off the replication, turning the Redis server into a MASTER. Specifying a non-null master will make the server a replica of another server listening at the specified hostname and port.
        /// </summary>
        /// <param name="master">Endpoint of the new master to replicate from.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/replicaof</remarks>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicaOf) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        void SlaveOf(EndPoint master, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The REPLICAOF command can change the replication settings of a replica on the fly. If a Redis server is already acting as replica, specifying a null master will turn off the replication, turning the Redis server into a MASTER. Specifying a non-null master will make the server a replica of another server listening at the specified hostname and port.
        /// </summary>
        /// <param name="master">Endpoint of the new master to replicate from.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/replicaof</remarks>
        void ReplicaOf(EndPoint master, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The REPLICAOF command can change the replication settings of a replica on the fly. If a Redis server is already acting as replica, specifying a null master will turn off the replication, turning the Redis server into a MASTER. Specifying a non-null master will make the server a replica of another server listening at the specified hostname and port.
        /// </summary>
        /// <param name="master">Endpoint of the new master to replicate from.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/replicaof</remarks>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicaOfAsync) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task SlaveOfAsync(EndPoint master, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The REPLICAOF command can change the replication settings of a replica on the fly. If a Redis server is already acting as replica, specifying a null master will turn off the replication, turning the Redis server into a MASTER. Specifying a non-null master will make the server a replica of another server listening at the specified hostname and port.
        /// </summary>
        /// <param name="master">Endpoint of the new master to replicate from.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/replicaof</remarks>
        Task ReplicaOfAsync(EndPoint master, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// To read the slow log the SLOWLOG GET command is used, that returns every entry in the slow log. It is possible to return only the N most recent entries passing an additional argument to the command (for instance SLOWLOG GET 10).
        /// </summary>
        /// <param name="count">The count of items to get.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/slowlog</remarks>
        CommandTrace[] SlowlogGet(int count = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// To read the slow log the SLOWLOG GET command is used, that returns every entry in the slow log. It is possible to return only the N most recent entries passing an additional argument to the command (for instance SLOWLOG GET 10).
        /// </summary>
        /// <param name="count">The count of items to get.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/slowlog</remarks>
        Task<CommandTrace[]> SlowlogGetAsync(int count = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// You can reset the slow log using the SLOWLOG RESET command. Once deleted the information is lost forever.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/slowlog</remarks>
        void SlowlogReset(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// You can reset the slow log using the SLOWLOG RESET command. Once deleted the information is lost forever.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/slowlog</remarks>
        Task SlowlogResetAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Lists the currently active channels. An active channel is a Pub/Sub channel with one ore more subscribers (not including clients subscribed to patterns).
        /// </summary>
        /// <param name="pattern">The channel name pattern to get channels for.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns> a list of active channels, optionally matching the specified pattern.</returns>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        RedisChannel[] SubscriptionChannels(RedisChannel pattern = default(RedisChannel), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Lists the currently active channels. An active channel is a Pub/Sub channel with one ore more subscribers (not including clients subscribed to patterns).
        /// </summary>
        /// <param name="pattern">The channel name pattern to get channels for.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns> a list of active channels, optionally matching the specified pattern.</returns>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        Task<RedisChannel[]> SubscriptionChannelsAsync(RedisChannel pattern = default(RedisChannel), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the number of subscriptions to patterns (that are performed using the PSUBSCRIBE command). Note that this is not just the count of clients subscribed to patterns but the total number of patterns all the clients are subscribed to.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of patterns all the clients are subscribed to.</returns>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        long SubscriptionPatternCount(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the number of subscriptions to patterns (that are performed using the PSUBSCRIBE command). Note that this is not just the count of clients subscribed to patterns but the total number of patterns all the clients are subscribed to.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of patterns all the clients are subscribed to.</returns>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        Task<long> SubscriptionPatternCountAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the number of subscribers (not counting clients subscribed to patterns) for the specified channel.
        /// </summary>
        /// <param name="channel">The channel to get a subcriber count for.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        long SubscriptionSubscriberCount(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the number of subscribers (not counting clients subscribed to patterns) for the specified channel.
        /// </summary>
        /// <param name="channel">The channel to get a subcriber count for.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/pubsub</remarks>
        Task<long> SubscriptionSubscriberCountAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Swaps two Redis databases, so that immediately all the clients connected to a given database will see the data of the other database, and the other way around
        /// </summary>
        /// <param name="first">The ID of the first database.</param>
        /// <param name="second">The ID of the second database.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/swapdb</remarks>
        void SwapDatabases(int first, int second, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Swaps two Redis databases, so that immediately all the clients connected to a given database will see the data of the other database, and the other way around
        /// </summary>
        /// <param name="first">The ID of the first database.</param>
        /// <param name="second">The ID of the second database.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/swapdb</remarks>
        Task SwapDatabasesAsync(int first, int second, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The TIME command returns the current server time in UTC format.
        /// Use the DateTime.ToLocalTime() method to get local time.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The server's current time.</returns>
        /// <remarks>https://redis.io/commands/time</remarks>
        DateTime Time(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The TIME command returns the current server time in UTC format.
        /// Use the DateTime.ToLocalTime() method to get local time.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The server's current time.</returns>
        /// <remarks>https://redis.io/commands/time</remarks>
        Task<DateTime> TimeAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Gets a text-based latency diagnostic
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        Task<string> LatencyDoctorAsync(CommandFlags flags = CommandFlags.None);
        /// <summary>
        /// Gets a text-based latency diagnostic
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        string LatencyDoctor(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Resets the given events (or all if none are specified), discarding the currently logged latency spike events, and resetting the maximum event time register.
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        Task<long> LatencyResetAsync(string[] eventNames = null, CommandFlags flags = CommandFlags.None);
        /// <summary>
        /// Resets the given events (or all if none are specified), discarding the currently logged latency spike events, and resetting the maximum event time register.
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        long LatencyReset(string[] eventNames = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Fetch raw latency data from the event time series, as timestamp-latency pairs
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        Task<LatencyHistoryEntry[]> LatencyHistoryAsync(string eventName, CommandFlags flags = CommandFlags.None);
        /// <summary>
        /// Fetch raw latency data from the event time series, as timestamp-latency pairs
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        LatencyHistoryEntry[] LatencyHistory(string eventName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Fetch raw latency data from the event time series, as timestamp-latency pairs
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        Task<LatencyLatestEntry[]> LatencyLatestAsync(CommandFlags flags = CommandFlags.None);
        /// <summary>
        /// Fetch raw latency data from the event time series, as timestamp-latency pairs
        /// </summary>
        /// <remarks>https://redis.io/topics/latency-monitor</remarks>
        LatencyLatestEntry[] LatencyLatest(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Reports about different memory-related issues that the Redis server experiences, and advises about possible remedies.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-doctor</remarks>
        Task<string> MemoryDoctorAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Reports about different memory-related issues that the Redis server experiences, and advises about possible remedies.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-doctor</remarks>
        string MemoryDoctor(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Attempts to purge dirty pages so these can be reclaimed by the allocator.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-purge</remarks>
        Task MemoryPurgeAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Attempts to purge dirty pages so these can be reclaimed by the allocator.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-purge</remarks>
        void MemoryPurge(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns an array reply about the memory usage of the server.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-stats</remarks>
        Task<RedisResult> MemoryStatsAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns an array reply about the memory usage of the server.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-stats</remarks>
        RedisResult MemoryStats(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Provides an internal statistics report from the memory allocator.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-malloc-stats</remarks>
        Task<string> MemoryAllocatorStatsAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Provides an internal statistics report from the memory allocator.
        /// </summary>
        /// <remarks>https://redis.io/commands/memory-malloc-stats</remarks>
        string MemoryAllocatorStats(CommandFlags flags = CommandFlags.None);

        #region Sentinel

        /// <summary>
        /// Returns the ip and port number of the master with that name. 
        /// If a failover is in progress or terminated successfully for this master it returns the address and port of the promoted replica.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the master ip and port</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        EndPoint SentinelGetMasterAddressByName(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the ip and port number of the master with that name. 
        /// If a failover is in progress or terminated successfully for this master it returns the address and port of the promoted replica.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the master ip and port</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task<EndPoint> SentinelGetMasterAddressByNameAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the ip and port numbers of all known Sentinels
        /// for the given service name.
        /// </summary>
        /// <param name="serviceName">the sentinel service name</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>a list of the sentinel ips and ports</returns>
        EndPoint[] SentinelGetSentinelAddresses(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the ip and port numbers of all known Sentinels
        /// for the given service name.
        /// </summary>
        /// <param name="serviceName">the sentinel service name</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>a list of the sentinel ips and ports</returns>
        Task<EndPoint[]> SentinelGetSentinelAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the ip and port numbers of all known Sentinel replicas
        /// for the given service name.
        /// </summary>
        /// <param name="serviceName">the sentinel service name</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>a list of the replica ips and ports</returns>
        EndPoint[] SentinelGetReplicaAddresses(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the ip and port numbers of all known Sentinel replicas
        /// for the given service name.
        /// </summary>
        /// <param name="serviceName">the sentinel service name</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>a list of the replica ips and ports</returns>
        Task<EndPoint[]> SentinelGetReplicaAddressesAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show the state and info of the specified master.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the master state as KeyValuePairs</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        KeyValuePair<string, string>[] SentinelMaster(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show the state and info of the specified master.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the master state as KeyValuePairs</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task<KeyValuePair<string, string>[]> SentinelMasterAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of monitored masters and their state.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of master state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        KeyValuePair<string, string>[][] SentinelMasters(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of monitored masters and their state.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of master state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task<KeyValuePair<string, string>[][]> SentinelMastersAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of replicas for this master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of replica state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(SentinelReplicas) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        KeyValuePair<string, string>[][] SentinelSlaves(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of replicas for this master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of replica state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        KeyValuePair<string, string>[][] SentinelReplicas(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of replicas for this master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of replica state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(SentinelReplicasAsync) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Task<KeyValuePair<string, string>[][]> SentinelSlavesAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of replicas for this master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>an array of replica state KeyValuePair arrays</returns>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task<KeyValuePair<string, string>[][]> SentinelReplicasAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Force a failover as if the master was not reachable, and without asking for agreement to other Sentinels 
        /// (however a new version of the configuration will be published so that the other Sentinels will update their configurations).
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        void SentinelFailover(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Force a failover as if the master was not reachable, and without asking for agreement to other Sentinels 
        /// (however a new version of the configuration will be published so that the other Sentinels will update their configurations).
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task SentinelFailoverAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of sentinels for a master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        KeyValuePair<string, string>[][] SentinelSentinels(string serviceName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Show a list of sentinels for a master, and their state.
        /// </summary>
        /// <param name="serviceName">The sentinel service name.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        Task<KeyValuePair<string, string>[][]> SentinelSentinelsAsync(string serviceName, CommandFlags flags = CommandFlags.None);

        #endregion
    }

    /// <summary>
    /// A latency entry as reported by the built-in LATENCY HISTORY command
    /// </summary>
    public readonly struct LatencyHistoryEntry
    {
        internal static readonly ResultProcessor<LatencyHistoryEntry[]> ToArray = new Processor();

        private sealed class Processor : ArrayResultProcessor<LatencyHistoryEntry>
        {
            protected override bool TryParse(in RawResult raw, out LatencyHistoryEntry parsed)
            {
                if (raw.Type == ResultType.MultiBulk)
                {
                    var items = raw.GetItems();
                    if (items.Length >= 2
                        && items[0].TryGetInt64(out var timestamp)
                        && items[1].TryGetInt64(out var duration))
                    {
                        parsed = new LatencyHistoryEntry(timestamp, duration);
                        return true;
                    }
                }
                parsed = default;
                return false;
            }
        }

        /// <summary>
        /// The time at which this entry was recorded
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// The latency recorded for this event
        /// </summary>
        public int DurationMilliseconds { get; }

        internal LatencyHistoryEntry(long timestamp, long duration)
        {
            Timestamp = RedisBase.UnixEpoch.AddSeconds(timestamp);
            DurationMilliseconds = checked((int)duration);
        }
    }

    /// <summary>
    /// A latency entry as reported by the built-in LATENCY LATEST command
    /// </summary>
    public readonly struct LatencyLatestEntry
    {
        internal static readonly ResultProcessor<LatencyLatestEntry[]> ToArray = new Processor();

        private sealed class Processor : ArrayResultProcessor<LatencyLatestEntry>
        {
            protected override bool TryParse(in RawResult raw, out LatencyLatestEntry parsed)
            {
                if (raw.Type == ResultType.MultiBulk)
                {
                    var items = raw.GetItems();
                    if (items.Length >= 4
                        && items[1].TryGetInt64(out var timestamp)
                        && items[2].TryGetInt64(out var duration)
                        && items[3].TryGetInt64(out var maxDuration))
                    {
                        parsed = new LatencyLatestEntry(items[0].GetString(), timestamp, duration, maxDuration);
                        return true;
                    }
                }
                parsed = default;
                return false;
            }
        }

        /// <summary>
        /// The name of this event
        /// </summary>
        public string EventName { get; }

        /// <summary>
        /// The time at which this entry was recorded
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// The latency recorded for this event
        /// </summary>
        public int DurationMilliseconds { get; }

        /// <summary>
        /// The max latency recorded for all events
        /// </summary>
        public int MaxDurationMilliseconds { get; }

        internal LatencyLatestEntry(string eventName, long timestamp, long duration, long maxDuration)
        {
            EventName = eventName;
            Timestamp = RedisBase.UnixEpoch.AddSeconds(timestamp);
            DurationMilliseconds = checked((int)duration);
            MaxDurationMilliseconds = checked((int)maxDuration);
        }
    }

    internal static class IServerExtensions
    {
        /// <summary>
        /// For testing only: Break the connection without mercy or thought
        /// </summary>
        /// <param name="server">The server to simulate failure on.</param>
        public static void SimulateConnectionFailure(this IServer server) => (server as RedisServer)?.SimulateConnectionFailure();

        public static string Role(this IServer server)
        {
            var result = (RedisResult[])server.Execute("ROLE");
            if (result != null && result.Length > 0)
                return result[0].ToString();

            return null;
        }
    }
}
