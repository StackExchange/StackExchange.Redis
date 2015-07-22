using System.Collections.Generic;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a set of parameters which configure how command handlers are executed and other conditions.
    /// </summary>
    public sealed class RedisCommandHandlerConfiguration
    {
        private static readonly HashSet<RedisCommand> activatedCommands = new HashSet<RedisCommand>();

        /// <summary>
        /// Gets all activated commands. If there is no specific command activated, handlers will be executed for all Redis commands.
        /// </summary>
        internal static HashSet<RedisCommand> ActivatedCommands { get { return activatedCommands; } }

        /// <summary>
        /// Provides for which commands the configured Redis command handlers must be raised during command life-cycle.
        /// </summary>
        /// <param name="commands">An array of <see cref="StackExchange.Redis.RedisCommand"/> enumeration values</param>
        public void ActivateForCommands(params RedisCommand[] commands)
        {
            foreach (RedisCommand command in commands)
                activatedCommands.Add(command);

        }
    }
}