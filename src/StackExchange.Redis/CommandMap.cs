using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the commands mapped on a particular configuration.
    /// </summary>
    public sealed class CommandMap
    {
        private readonly CommandBytes[] map;
        private readonly byte[] bytes;

        private CommandMap(CommandBytes[] map, byte[] bytes)
        {
            this.map = map;
            this.bytes = bytes;
        }

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
                var value = map[i];
                var valueBytes = value.GetCommandBytes(bytes);
                if (!AsciiHash.EqualsCI(keyString, valueBytes))
                {
                    if (sb.Length != 0) sb.Append(',');
                    sb.Append('$').Append(keyString).Append('=');
                    if (!valueBytes.IsEmpty)
                    {
                        sb.Append(Encoding.ASCII.GetString(valueBytes));
                    }
                }
            }
        }

        internal void AssertAvailable(RedisCommand command)
        {
            if (map[(int)command].IsEmpty) ThrowCommandDisabled(command);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowCommandDisabled(RedisCommand command) => throw ExceptionFactory.CommandDisabled(command);
        }

        internal ReadOnlySpan<byte> GetCommandBytes(RedisCommand command) => map[(int)command].GetCommandBytes(bytes);

        internal ReadOnlySpan<byte> GetResp(RedisCommand command) => map[(int)command].GetResp(bytes);

        internal bool IsAvailable(RedisCommand command) => !map[(int)command].IsEmpty;

        private static RedisCommand[]? s_AllCommands;

        private static ReadOnlySpan<RedisCommand> AllCommands => s_AllCommands ??= (RedisCommand[])Enum.GetValues(typeof(RedisCommand));

        private static CommandMap CreateImpl(Dictionary<string, string?>? caseInsensitiveOverrides, HashSet<RedisCommand>? exclusions)
        {
            var commands = AllCommands;

            int totalLength = 0;
            for (int i = 0; i < commands.Length; i++)
            {
                var value = GetCommandValue(commands[i], caseInsensitiveOverrides, exclusions);
                if (string.IsNullOrEmpty(value)) continue;

                totalLength += GetBulkStringLength(Encoding.ASCII.GetByteCount(value));
            }

            // Store all mapped command names as RESP bulk-string fragments in one buffer - everything is then
            // ready to throw directly into the stream.
            var map = new CommandBytes[commands.Length];

            // Currently (8.8-ish) this is approx 3k; that's very reasonable to avoid a ton of CPU cycles in
            // the most common write path.
            var bytes = totalLength == 0 ? Array.Empty<byte>() : new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                var value = GetCommandValue(command, caseInsensitiveOverrides, exclusions);
                if (string.IsNullOrEmpty(value)) continue;

                int payloadLength = Encoding.ASCII.GetByteCount(value);
                int respLength = GetBulkStringLength(payloadLength);
                map[(int)command] = new CommandBytes(offset, respLength, GetPayloadOffset(payloadLength));

                var span = bytes.AsSpan(offset, respLength);
                span[0] = (byte)'$';
                int payloadOffset = MessageWriter.WriteRaw(span, payloadLength, offset: 1);
                var payload = span.Slice(payloadOffset, payloadLength);
                int written = Encoding.ASCII.GetBytes(value.AsSpan(), payload);
                if (written != payloadLength) ThrowAsciiEncodeLengthCheckFailure();
                AsciiHash.ToUpper(payload);
                MessageWriter.WriteCrlf(span, payloadOffset + payloadLength);
                offset += respLength;
            }
            return new CommandMap(map, bytes);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowAsciiEncodeLengthCheckFailure() => throw new InvalidOperationException("ASCII encode length check failure");
        }

        private static string? GetCommandValue(
            RedisCommand command,
            Dictionary<string, string?>? caseInsensitiveOverrides,
            HashSet<RedisCommand>? exclusions)
        {
            if (command is RedisCommand.UNKNOWN || exclusions?.Contains(command) == true) return null;

            var name = command.ToString();
            if (caseInsensitiveOverrides != null && caseInsensitiveOverrides.TryGetValue(name, out string? value))
            {
                return value;
            }

            return name;
        }

        // ${N}\r\n{RAW}\r\n
        private static int GetBulkStringLength(int payloadLength) => 5 + GetDigitCount(payloadLength) + payloadLength;

        // ${N}\r\n
        private static byte GetPayloadOffset(int payloadLength) => checked((byte)(3 + GetDigitCount(payloadLength)));

        private static int GetDigitCount(int value)
        {
            if (value < 10) return 1;
            if (value < 100) return 2;
            int digits = 1;
            while ((value /= 10) != 0)
            {
                digits++;
            }
            return digits;
        }

        private readonly struct CommandBytes(int offset, int length, byte payloadOffset)
        {
            // Tracks position inside a shared buffer; given
            // $3\r\nFOO\r\n$3\r\nBAR\r\n we have the positions (for BAR):
            //              ^ a   ^ b    ^c
            // We know that the trailer is always exactly 2 bytes, so we don't need to store the
            // length of the command itself - we can infer from the other values.
            // offset is a, payloadOffset is a-to-b, length is a-to-c
            private readonly uint offset = checked((uint)offset);
            private readonly ushort length = checked((ushort)length);

            public bool IsEmpty => length == 0;

            // this will be fine even for a default instance
            public ReadOnlySpan<byte> GetResp(byte[] bytes) => new(bytes, (int)offset, length);

            public ReadOnlySpan<byte> GetCommandBytes(byte[] bytes)
            {
                if (IsEmpty) return default;
                return new ReadOnlySpan<byte>(
                    bytes,
                    checked((int)offset) + payloadOffset,
                    length - payloadOffset - 2);
            }
        }
    }
}
