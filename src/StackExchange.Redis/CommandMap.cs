using System;
using System.Collections.Generic;
using System.Text;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the commands mapped on a particular configuration.
    /// </summary>
    public sealed class CommandMap
    {
        private readonly AsciiHash[] map;

        internal CommandMap(AsciiHash[] map) => this.map = map;

        /// <summary>
        /// The default commands specified by redis.
        /// </summary>
        public static CommandMap Default { get; } = CreateImpl(null, null);

        /// <summary>
        /// The commands available to <a href="https://github.com/twitter/twemproxy">twemproxy</a>.
        /// </summary>
        /// <remarks><seealso href="https://github.com/twitter/twemproxy/blob/master/notes/redis.md"/></remarks>
        public static CommandMap Twemproxy { get; } = CreateImpl(null, exclusions: new HashSet<RedisCommand>
        {
            RedisCommand.KEYS, RedisCommand.MIGRATE, RedisCommand.MOVE, RedisCommand.OBJECT, RedisCommand.RANDOMKEY,
            RedisCommand.RENAME, RedisCommand.RENAMENX, RedisCommand.SCAN,

            RedisCommand.BITOP, RedisCommand.MSETEX, RedisCommand.MSETNX,

            RedisCommand.BLPOP, RedisCommand.BRPOP, RedisCommand.BRPOPLPUSH, // yeah, me neither!

            RedisCommand.PSUBSCRIBE, RedisCommand.PUBLISH, RedisCommand.PUNSUBSCRIBE, RedisCommand.SUBSCRIBE, RedisCommand.UNSUBSCRIBE, RedisCommand.SPUBLISH, RedisCommand.SSUBSCRIBE, RedisCommand.SUNSUBSCRIBE,

            RedisCommand.DISCARD, RedisCommand.EXEC, RedisCommand.MULTI, RedisCommand.UNWATCH, RedisCommand.WATCH,

            RedisCommand.SCRIPT,

            RedisCommand.ECHO, RedisCommand.SELECT,

            RedisCommand.BGREWRITEAOF, RedisCommand.BGSAVE, RedisCommand.CLIENT, RedisCommand.CLUSTER, RedisCommand.CONFIG, RedisCommand.DBSIZE,
            RedisCommand.DEBUG, RedisCommand.FLUSHALL, RedisCommand.FLUSHDB, RedisCommand.INFO, RedisCommand.LASTSAVE, RedisCommand.MONITOR, RedisCommand.REPLICAOF,
            RedisCommand.SAVE, RedisCommand.SHUTDOWN, RedisCommand.SLAVEOF, RedisCommand.SLOWLOG, RedisCommand.SYNC, RedisCommand.TIME, RedisCommand.HOTKEYS,
        });

        /// <summary>
        /// The commands available to <a href="https://github.com/envoyproxy/envoy">envoyproxy</a>.
        /// </summary>
        /// <remarks><seealso href="https://www.envoyproxy.io/docs/envoy/latest/intro/arch_overview/other_protocols/redis.html?highlight=redis"/></remarks>
        public static CommandMap Envoyproxy { get; } = CreateImpl(null, exclusions: new HashSet<RedisCommand>
        {
            RedisCommand.KEYS, RedisCommand.MIGRATE, RedisCommand.MOVE, RedisCommand.OBJECT, RedisCommand.RANDOMKEY,
            RedisCommand.RENAME, RedisCommand.RENAMENX, RedisCommand.SORT, RedisCommand.SCAN,

            RedisCommand.BITOP, RedisCommand.MSETEX, RedisCommand.MSETNX,

            RedisCommand.BLPOP, RedisCommand.BRPOP, RedisCommand.BRPOPLPUSH, // yeah, me neither!

            RedisCommand.PSUBSCRIBE, RedisCommand.PUBLISH, RedisCommand.PUNSUBSCRIBE, RedisCommand.SUBSCRIBE, RedisCommand.UNSUBSCRIBE, RedisCommand.SPUBLISH, RedisCommand.SSUBSCRIBE, RedisCommand.SUNSUBSCRIBE,

            RedisCommand.SCRIPT,

            RedisCommand.SELECT,

            RedisCommand.BGREWRITEAOF, RedisCommand.BGSAVE, RedisCommand.CLIENT, RedisCommand.CLUSTER, RedisCommand.CONFIG, RedisCommand.DBSIZE,
            RedisCommand.DEBUG, RedisCommand.FLUSHALL, RedisCommand.FLUSHDB, RedisCommand.INFO, RedisCommand.LASTSAVE, RedisCommand.MONITOR, RedisCommand.REPLICAOF,
            RedisCommand.SAVE, RedisCommand.SHUTDOWN, RedisCommand.SLAVEOF, RedisCommand.SLOWLOG, RedisCommand.SYNC, RedisCommand.TIME, RedisCommand.HOTKEYS,

            // supported by envoy but not enabled by stack exchange
            // RedisCommand.BITFIELD,
            //
            // RedisCommand.GEORADIUS_RO,
            // RedisCommand.GEORADIUSBYMEMBER_RO,
        });

        /// <summary>
        /// The commands available to <a href="https://ssdb.io/">SSDB</a>.
        /// </summary>
        /// <remarks><seealso href="https://ssdb.io/docs/redis-to-ssdb.html"/></remarks>
        public static CommandMap SSDB { get; } = Create(
            new HashSet<string>
            {
                "ping",
                "get", "set", "del", "incr", "incrby", "mget", "mset", "keys", "getset", "setnx",
                "hget", "hset", "hdel", "hincrby", "hkeys", "hvals", "hmget", "hmset", "hlen",
                "zscore", "zadd", "zrem", "zrange", "zrangebyscore", "zincrby", "zdecrby", "zcard",
                "llen", "lpush", "rpush", "lpop", "rpop", "lrange", "lindex",
            },
            true);

        /// <summary>
        /// The commands available to <a href="https://redis.io/topics/sentinel">Sentinel</a>.
        /// </summary>
        /// <remarks><seealso href="https://redis.io/topics/sentinel"/></remarks>
        public static CommandMap Sentinel { get; } = Create(
            new HashSet<string>
            {
                "auth", "hello", "ping", "info", "role", "sentinel", "subscribe", "shutdown", "psubscribe", "unsubscribe", "punsubscribe",
            },
            true);

        /// <summary>
        /// Create a new <see cref="CommandMap"/>, customizing some commands.
        /// </summary>
        /// <param name="overrides">The commands to override.</param>
        public static CommandMap Create(Dictionary<string, string?>? overrides)
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
                overrides = new Dictionary<string, string?>(overrides, StringComparer.OrdinalIgnoreCase);
            }
            return CreateImpl(overrides, null);
        }

        /// <summary>
        /// Creates a <see cref="CommandMap"/> by specifying which commands are available or unavailable.
        /// </summary>
        /// <param name="commands">The commands to specify.</param>
        /// <param name="available">Whether the commands are available or excluded.</param>
        public static CommandMap Create(HashSet<string> commands, bool available = true)
        {
            if (available)
            {
                var dictionary = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                // nix everything
                foreach (RedisCommand command in AllCommands)
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
                HashSet<RedisCommand>? exclusions = null;
                if (commands != null)
                {
                    // nix the things that are specified
                    foreach (var command in commands)
                    {
                        if (RedisCommandMetadata.TryParseCI(command, out RedisCommand parsed))
                        {
                            (exclusions ??= new HashSet<RedisCommand>()).Add(parsed);
                        }
                    }
                }
                if (exclusions == null || exclusions.Count == 0) return Default;
                return CreateImpl(null, exclusions);
            }
        }

        /// <summary>
        /// See <see cref="object.ToString"/>.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            AppendDeltas(sb);
            return sb.ToString();
        }

        internal void AppendDeltas(StringBuilder sb)
        {
            var all = AllCommands;
            for (int i = 0; i < map.Length; i++)
            {
                var knownCmd = all[i];
                if (knownCmd is RedisCommand.UNKNOWN) continue;
                var keyString = knownCmd.ToString();
                var keyBytes = new AsciiHash(keyString);
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

        internal AsciiHash GetBytes(RedisCommand command) => map[(int)command];

        internal bool IsAvailable(RedisCommand command) => !map[(int)command].IsEmpty;

        private static RedisCommand[]? s_AllCommands;

        private static ReadOnlySpan<RedisCommand> AllCommands => s_AllCommands ??= (RedisCommand[])Enum.GetValues(typeof(RedisCommand));

        private static CommandMap CreateImpl(Dictionary<string, string?>? caseInsensitiveOverrides, HashSet<RedisCommand>? exclusions)
        {
            var commands = AllCommands;

            // todo: optimize and support ad-hoc overrides/disables, and shared buffer rather than multiple arrays
            var map = new AsciiHash[commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                int idx = (int)commands[i];
                string? name = commands[i].ToString(), value = name;

                if (commands[i] is RedisCommand.UNKNOWN || exclusions?.Contains(commands[i]) == true)
                {
                    map[idx] = default;
                }
                else
                {
                    if (caseInsensitiveOverrides != null && caseInsensitiveOverrides.TryGetValue(name, out string? tmp))
                    {
                        value = tmp?.ToUpperInvariant();
                    }
                    map[idx] = new AsciiHash(value);
                }
            }
            return new CommandMap(map);
        }
    }
}
