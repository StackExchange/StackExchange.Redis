using System;
using System.Collections.Generic;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the commands mapped on a particular configuration
    /// </summary>
    public sealed class CommandMap
    {
        private readonly CommandBytes[] map;

        internal CommandMap(CommandBytes[] map)
        {
            this.map = map;
        }
        /// <summary>
        /// The default commands specified by redis
        /// </summary>
        public static CommandMap Default { get; } = CreateImpl(null, null);

        /// <summary>
        /// The commands available to <a href="twemproxy">https://github.com/twitter/twemproxy</a>
        /// </summary>
        /// <remarks>https://github.com/twitter/twemproxy/blob/master/notes/redis.md</remarks>
        public static CommandMap Twemproxy { get; } = CreateImpl(null, exclusions: new HashSet<RedisCommand>
        {
            // see https://github.com/twitter/twemproxy/blob/master/notes/redis.md
            RedisCommand.KEYS, RedisCommand.MIGRATE, RedisCommand.MOVE, RedisCommand.OBJECT, RedisCommand.RANDOMKEY,
            RedisCommand.RENAME, RedisCommand.RENAMENX, RedisCommand.SORT, RedisCommand.SCAN,

            RedisCommand.BITOP, RedisCommand.MSET, RedisCommand.MSETNX,

            RedisCommand.HSCAN,

            RedisCommand.BLPOP, RedisCommand.BRPOP, RedisCommand.BRPOPLPUSH, // yeah, me neither!

            RedisCommand.SSCAN,

            RedisCommand.ZSCAN,

            RedisCommand.PSUBSCRIBE, RedisCommand.PUBLISH, RedisCommand.PUNSUBSCRIBE, RedisCommand.SUBSCRIBE, RedisCommand.UNSUBSCRIBE,

            RedisCommand.DISCARD, RedisCommand.EXEC, RedisCommand.MULTI, RedisCommand.UNWATCH, RedisCommand.WATCH,

            RedisCommand.SCRIPT,

            RedisCommand.ECHO, RedisCommand.PING, RedisCommand.QUIT, RedisCommand.SELECT,

            RedisCommand.BGREWRITEAOF, RedisCommand.BGSAVE, RedisCommand.CLIENT, RedisCommand.CLUSTER, RedisCommand.CONFIG, RedisCommand.DBSIZE,
            RedisCommand.DEBUG, RedisCommand.FLUSHALL, RedisCommand.FLUSHDB, RedisCommand.INFO, RedisCommand.LASTSAVE, RedisCommand.MONITOR, RedisCommand.REPLICAOF,
            RedisCommand.SAVE, RedisCommand.SHUTDOWN, RedisCommand.SLAVEOF, RedisCommand.SLOWLOG, RedisCommand.SYNC, RedisCommand.TIME
        });

        /// <summary>
        /// The commands available to <a href="ssdb">http://www.ideawu.com/ssdb/</a>
        /// </summary>
        /// <remarks>http://www.ideawu.com/ssdb/docs/redis-to-ssdb.html</remarks>
        public static CommandMap SSDB { get; } = Create(new HashSet<string> {
            // see http://www.ideawu.com/ssdb/docs/redis-to-ssdb.html
            "ping",
            "get", "set", "del", "incr", "incrby", "mget", "mset", "keys", "getset", "setnx",
            "hget", "hset", "hdel", "hincrby", "hkeys", "hvals", "hmget", "hmset", "hlen",
            "zscore", "zadd", "zrem", "zrange", "zrangebyscore", "zincrby", "zdecrby", "zcard",
            "llen", "lpush", "rpush", "lpop", "rpop", "lrange", "lindex"
        }, true);

        /// <summary>
        /// The commands available to <a href="Sentinel">https://redis.io/topics/sentinel</a>
        /// </summary>
        /// <remarks>https://redis.io/topics/sentinel</remarks>
        public static CommandMap Sentinel { get; } = Create(new HashSet<string> {
            // see https://redis.io/topics/sentinel
            "auth", "ping", "info", "role", "sentinel", "subscribe", "shutdown", "psubscribe", "unsubscribe", "punsubscribe" }, true);

        /// <summary>
        /// Create a new CommandMap, customizing some commands
        /// </summary>
        /// <param name="overrides">The commands to override.</param>
        public static CommandMap Create(Dictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0) return Default;

            if (ReferenceEquals(overrides.Comparer, StringComparer.OrdinalIgnoreCase))
            {
                // that's ok; we're happy with ordinal/invariant case-insensitive
                // (but not culture-specific insensitive; completely untested)
            }
            else
            {
                // need case insensitive
                overrides = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
            }
            return CreateImpl(overrides, null);
        }

        /// <summary>
        /// Creates a CommandMap by specifying which commands are available or unavailable
        /// </summary>
        /// <param name="commands">The commands to specify.</param>
        /// <param name="available">Whether the commands are available or excluded.</param>
        public static CommandMap Create(HashSet<string> commands, bool available = true)
        {
            if (available)
            {
                var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // nix everything
                foreach (RedisCommand command in Enum.GetValues(typeof(RedisCommand)))
                {
                    dictionary[command.ToString()] = null;
                }
                if (commands != null)
                {
                    // then include (by removal) the things that are available
                    foreach (string command in commands)
                    {
                        dictionary.Remove(command);
                    }
                }
                return CreateImpl(dictionary, null);
            }
            else
            {
                HashSet<RedisCommand> exclusions = null;
                if (commands != null)
                {
                    // nix the things that are specified
                    foreach (var command in commands)
                    {
                        if (Enum.TryParse(command, true, out RedisCommand parsed))
                        {
                            (exclusions ?? (exclusions = new HashSet<RedisCommand>())).Add(parsed);
                        }
                    }
                }
                if (exclusions == null || exclusions.Count == 0) return Default;
                return CreateImpl(null, exclusions);
            }
        }

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            AppendDeltas(sb);
            return sb.ToString();
        }

        internal void AppendDeltas(StringBuilder sb)
        {
            for (int i = 0; i < map.Length; i++)
            {
                var keyString = ((RedisCommand)i).ToString();
                var keyBytes = new CommandBytes(keyString);
                var value = map[i];
                if (!keyBytes.Equals(value))
                {
                    if (sb.Length != 0) sb.Append(',');
                    sb.Append('$').Append(keyString).Append('=').Append(value);
                }
            }
        }

        internal void AssertAvailable(RedisCommand command)
        {
            if (map[(int)command].IsEmpty) throw ExceptionFactory.CommandDisabled(command);
        }

        internal CommandBytes GetBytes(RedisCommand command) => map[(int)command];

        internal CommandBytes GetBytes(string command)
        {
            if (command == null) return default;
            if(Enum.TryParse(command, true, out RedisCommand cmd))
            {   // we know that one!
                return map[(int)cmd];
            }
            return new CommandBytes(command);
        }

        internal bool IsAvailable(RedisCommand command) => !map[(int)command].IsEmpty;

        private static CommandMap CreateImpl(Dictionary<string, string> caseInsensitiveOverrides, HashSet<RedisCommand> exclusions)
        {
            var commands = (RedisCommand[])Enum.GetValues(typeof(RedisCommand));

            var map = new CommandBytes[commands.Length];
            bool haveDelta = false;
            for (int i = 0; i < commands.Length; i++)
            {
                int idx = (int)commands[i];
                string name = commands[i].ToString(), value = name;

                if (exclusions?.Contains(commands[i]) == true)
                {
                    map[idx] = default;
                }
                else
                {
                    if (caseInsensitiveOverrides != null && caseInsensitiveOverrides.TryGetValue(name, out string tmp))
                    {
                        value = tmp;
                    }
                    if (value != name) haveDelta = true;
                    // TODO: bug?
                    haveDelta = true;
                    map[idx] = new CommandBytes(value);
                }
            }
            if (!haveDelta && Default != null) return Default;

            return new CommandMap(map);
        }
    }
}
