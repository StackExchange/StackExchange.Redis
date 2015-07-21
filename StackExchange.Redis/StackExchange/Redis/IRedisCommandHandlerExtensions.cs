using System.Collections.Generic;

namespace StackExchange.Redis
{
    public static class IRedisCommandHandlerExtensions
    {
        public static void ExecuteBeforeHandlers(this IEnumerable<IRedisCommandHandler> handlers, RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null)
        {
            bool executeHandlers = false;

            if (RedisCommandHandlerConfiguration.ActivatedCommands.Count > 0)
                executeHandlers = RedisCommandHandlerConfiguration.ActivatedCommands.Contains(command);

            if (RedisCommandHandlerConfiguration.ActivatedCommands.Count == 0 || executeHandlers)
                foreach (IRedisCommandHandler handler in handlers)
                    handler.OnExecuting(command, involvedKeys, involvedValues);
        }
        public static void ExecuteAfterHandlers<TResult>(this IEnumerable<IRedisCommandHandler> handlers, RedisCommand command, RedisKey[] involvedKeys, ref TResult result)
        {
            bool executeHandlers = false;

            if (RedisCommandHandlerConfiguration.ActivatedCommands.Count > 0)
                executeHandlers = RedisCommandHandlerConfiguration.ActivatedCommands.Contains(command);

            if (RedisCommandHandlerConfiguration.ActivatedCommands.Count == 0 || executeHandlers)
                foreach (IRedisCommandHandler handler in handlers)
                    handler.OnExecuted(command, ref result, involvedKeys);
        }
    }
}