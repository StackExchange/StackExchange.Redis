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
        private static readonly CommandMap @default = CreateImpl(null);
        private readonly byte[][] map;

        internal CommandMap(byte[][] map)
        {
            this.map = map;
        }
        /// <summary>
        /// The default commands specified by redis
        /// </summary>
        public static CommandMap Default { get { return @default; } }

        /// <summary>
        /// Create a new CommandMap, customizing some commands
        /// </summary>
        public static CommandMap Create(Dictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0) return Default;

            return CreateImpl(overrides);
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
                var key = ((RedisCommand)i).ToString();
                var value = map[i] == null ? "" : Encoding.UTF8.GetString(map[i]);
                if (key != value)
                {
                    if (sb.Length != 0) sb.Append(',');
                    sb.Append('$').Append(key).Append('=').Append(value);
                }
            }
        }

        internal void AssertAvailable(RedisCommand command)
        {
            if (map[(int)command] == null) throw ExceptionFactory.CommandDisabled(false, command, null, null);
        }

        internal byte[] GetBytes(RedisCommand command)
        {
            return map[(int)command];
        }

        internal bool IsAvailable(RedisCommand command)
        {
            return map[(int)command] != null;
        }

        private static CommandMap CreateImpl(Dictionary<string, string> overrides)
        {
            RedisCommand[] values = (RedisCommand[])Enum.GetValues(typeof(RedisCommand));

            byte[][] map = new byte[values.Length][];
            bool haveDelta = false;
            for (int i = 0; i < values.Length; i++)
            {
                int idx = (int)values[i];
                string name = values[i].ToString(), value = name;

                if (overrides != null)
                {
                    foreach (var pair in overrides)
                    {
                        if (string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            value = pair.Value;
                            break;
                        }
                    }
                }
                if (value != name) haveDelta = true;

                haveDelta = true;
                byte[] val = string.IsNullOrWhiteSpace(value) ? null : Encoding.UTF8.GetBytes(value);
                map[idx] = val;
            }
            if (!haveDelta && @default != null) return @default;

            return new CommandMap(map);
        }
    }
}
