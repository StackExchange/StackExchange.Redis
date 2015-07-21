using System.Collections.Generic;

namespace StackExchange.Redis
{
    public sealed class RedisCommandHandlerConfiguration
    {
        private static readonly HashSet<RedisCommand> activatedCommands = new HashSet<RedisCommand>();

        internal static HashSet<RedisCommand> ActivatedCommands { get { return activatedCommands; } }

        public void ActivateForCommands(params RedisCommand[] commands)
        {
            foreach (RedisCommand command in commands)
                activatedCommands.Add(command);

        }
    }
}