using System;
using RESPite.Messages;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the information known about long-running commands.
    /// </summary>
    public sealed class CommandTrace
    {
        internal static readonly ResultProcessor<CommandTrace[]> Processor = new CommandTraceProcessor();

        internal CommandTrace(long uniqueId, long time, long duration, RedisValue[] arguments)
        {
            UniqueId = uniqueId;
            Time = RedisBase.UnixEpoch.AddSeconds(time);
            // duration = The amount of time needed for its execution, in microseconds.
            // A tick is equal to 100 nanoseconds, or one ten-millionth of a second.
            // So 1 microsecond = 10 ticks
            Duration = TimeSpan.FromTicks(duration * 10);
            Arguments = arguments;
        }

        /// <summary>
        /// The array composing the arguments of the command.
        /// </summary>
        public RedisValue[] Arguments { get; }

        /// <summary>
        /// The amount of time needed for its execution.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// The time at which the logged command was processed.
        /// </summary>
        public DateTime Time { get; }

        /// <summary>
        /// A unique progressive identifier for every slow log entry.
        /// </summary>
        /// <remarks>The entry's unique ID can be used in order to avoid processing slow log entries multiple times (for instance you may have a script sending you an email alert for every new slow log entry). The ID is never reset in the course of the Redis server execution, only a server restart will reset it.</remarks>
        public long UniqueId { get; }

        /// <summary>
        /// Deduces a link to the redis documentation about the specified command.
        /// </summary>
        public string? GetHelpUrl()
        {
            if (Arguments == null || Arguments.Length == 0) return null;

            const string BaseUrl = "https://redis.io/commands/";

            string encoded0 = Uri.EscapeDataString(((string)Arguments[0]!).ToLowerInvariant());

            if (Arguments.Length > 1)
            {
                switch (encoded0)
                {
                    case "script":
                    case "client":
                    case "cluster":
                    case "config":
                    case "debug":
                    case "pubsub":
                        string encoded1 = Uri.EscapeDataString(((string)Arguments[1]!).ToLowerInvariant());
                        return BaseUrl + encoded0 + "-" + encoded1;
                }
            }
            return BaseUrl + encoded0;
        }

        private sealed class CommandTraceProcessor : ResultProcessor<CommandTrace[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // see: SLOWLOG GET
                switch (reader.Resp2PrefixArray)
                {
                    case RespPrefix.Array:

                        static CommandTrace ParseOne(ref RespReader reader)
                        {
                            CommandTrace result = null!;
                            if (reader.IsAggregate)
                            {
                                long uniqueId = 0, time = 0, duration = 0;
                                if (reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out uniqueId)
                                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out time)
                                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out duration)
                                    && reader.TryMoveNext() && reader.IsAggregate)
                                {
                                    var values = reader.ReadPastRedisValues() ?? [];
                                    result = new CommandTrace(uniqueId, time, duration, values);
                                }
                            }
                            return result;
                        }
                        var arr = reader.ReadPastArray(ParseOne, scalar: false)!;
                        if (arr.AnyNull()) return false;

                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }
    }
}
