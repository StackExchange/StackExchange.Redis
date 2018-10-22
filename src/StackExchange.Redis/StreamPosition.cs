using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a pair consisting of the Stream Key and the <see cref="Position"/> from which to begin reading a stream.
    /// </summary>
    public struct StreamPosition
    {
        /// <summary>
        /// Read from the beginning of a stream.
        /// </summary>
        public static RedisValue Beginning => StreamConstants.ReadMinValue;

        /// <summary>
        /// Read new messages.
        /// </summary>
        public static RedisValue NewMessages => StreamConstants.NewMessages;

        /// <summary>
        /// Initializes a <see cref="StreamPosition"/> value.
        /// </summary>
        /// <param name="key">The key for the stream.</param>
        /// <param name="position">The position from which to begin reading the stream.</param>
        public StreamPosition(RedisKey key, RedisValue position)
        {
            Key = key;
            Position = position;
        }

        /// <summary>
        /// The stream key.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// The offset at which to begin reading the stream.
        /// </summary>
        public RedisValue Position { get; }

        internal static RedisValue Resolve(RedisValue value, RedisCommand command)
        {
            if (value == NewMessages)
            {
                switch (command)
                {
                    case RedisCommand.XREAD: throw new InvalidOperationException("StreamPosition.NewMessages cannot be used with StreamRead.");
                    case RedisCommand.XREADGROUP: return StreamConstants.UndeliveredMessages;
                    case RedisCommand.XGROUP: return StreamConstants.NewMessages;
                    default: // new is only valid for the above
                        throw new ArgumentException($"Unsupported command in StreamPosition.Resolve: {command}.", nameof(command));
                }
            } else if (value == StreamPosition.Beginning)
            {
                switch(command)
                {
                    case RedisCommand.XREAD:
                    case RedisCommand.XREADGROUP:
                    case RedisCommand.XGROUP:
                        return StreamConstants.AllMessages;
                }
            }
            return value;
        }
    }
}
