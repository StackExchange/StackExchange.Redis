using System.Collections.Generic;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a set of Redis command handling extensions that work as façade methods.
    /// </summary>
    public static class IRedisCommandHandlerExtensions
    {
        /// <summary>
        /// Determines if a given sequence of handlers are executable for a given Redis command.
        /// </summary>
        /// <param name="handlers"></param>
        /// <param name="command"></param>
        /// <returns>True if given handlers can be executed for the given handlers. Otherwise, returns false.</returns>
        private static bool CanExecuteHandlers(IEnumerable<IRedisCommandHandler> handlers, RedisCommand command)
        {
            return handlers != null &&
            (
                RedisCommandHandlerConfiguration.ActivatedCommands.Count == 0
                ||
                (RedisCommandHandlerConfiguration.ActivatedCommands.Count > 0
                && RedisCommandHandlerConfiguration.ActivatedCommands.Contains(command))
            );
        }

        /// <summary>
        /// Executes all Redis command handlers, running behaviors that must run before some given command is about to get executed.
        /// </summary>
        /// <param name="handlers">The sequence of Redis command handlers</param>
        /// <param name="command">The Redis command</param>
        /// <param name="involvedKeys">An array of involved Redis keys in the command</param>
        /// <param name="involvedValues">An array of involved Redis values in the command</param>
        /// <returns>True if handlers could be executed. Otherwise, false.</returns>
        public static bool ExecuteBeforeHandlers(this IEnumerable<IRedisCommandHandler> handlers, RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null)
        {
            bool canExecute = CanExecuteHandlers(handlers, command);

            if (canExecute)
                foreach (IRedisCommandHandler handler in handlers)
                    handler.OnExecuting(command, involvedKeys, involvedValues);

            return canExecute;
        }

        /// <summary>
        /// Executes all Redis command handlers, running behaviors that must run after some given command has been already executed.
        /// </summary>
        /// <typeparam name="TResult">The type of Redis command result</typeparam>
        /// <param name="handlers">The sequence of Redis command handlers</param>
        /// <param name="command">The Redis command</param>
        /// <param name="involvedKeys">An array of involved Redis keys in the command</param>
        /// <param name="result">The result of the Redis command execution</param>
        /// <returns>True if handlers could be executed. Otherwise, false.</returns>
        public static bool ExecuteAfterHandlers<TResult>(this IEnumerable<IRedisCommandHandler> handlers, RedisCommand command, RedisKey[] involvedKeys, ref TResult result)
        {
            bool canExecute = CanExecuteHandlers(handlers, command);

            if (canExecute)
                foreach (IRedisCommandHandler handler in handlers)
                    handler.OnExecuted(command, ref result, involvedKeys);

            return canExecute;
        }
    }
}