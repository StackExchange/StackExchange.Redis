using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// A position within a stream. Defaults to <see cref="Position.New"/>.
    /// </summary>
    public struct Position
    {
        /// <summary>
        /// Indicate a position from which to read a stream.
        /// </summary>
        /// <param name="readAfter">The position from which to read a stream.</param>
        public Position(RedisValue readAfter)
        {
            if (readAfter == RedisValue.Null) throw new ArgumentNullException(nameof(readAfter), "readAfter cannot be RedisValue.Null.");

            Kind = PositionKind.Explicit;
            ExplicitValue = readAfter;
        }

        private Position(PositionKind kind)
        {
            Kind = kind;
            ExplicitValue = RedisValue.Null;
        }

        private PositionKind Kind { get; }

        private RedisValue ExplicitValue { get; }

        /// <summary>
        /// Read new messages.
        /// </summary>
        public static Position New = new Position(PositionKind.New);

        /// <summary>
        /// Read from the beginning of a stream.
        /// </summary>
        public static Position Beginning = new Position(PositionKind.Beginning);

        internal RedisValue ResolveForCommand(RedisCommand command)
        {
            if (Kind == PositionKind.Explicit) return ExplicitValue;
            if (Kind == PositionKind.Beginning) return StreamConstants.ReadMinValue;

            // PositionKind.New
            if (command == RedisCommand.XREAD) throw new InvalidOperationException("Position.New cannot be used with StreamRead.");
            if (command == RedisCommand.XREADGROUP) return StreamConstants.UndeliveredMessages;
            if (command == RedisCommand.XGROUP) return StreamConstants.NewMessages;
            
            throw new ArgumentException($"Unsupported command in ResolveForCommand: {command}.", nameof(command));
        }
    }
}


