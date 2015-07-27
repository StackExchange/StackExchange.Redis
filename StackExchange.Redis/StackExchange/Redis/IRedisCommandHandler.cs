namespace StackExchange.Redis
{
    /// <summary>
    /// Defines a pluggable service which intercepts Redis command execution to add custom behavior. An implementation will be added to the environment
    /// using <see cref="StackExchange.Redis.RedisServiceFactory"/>.
    /// </summary>
    public interface IRedisCommandHandler
    {
        /// <summary>
        /// Defines behavior to be executed before a Redis command is about to be executed. Involved values can be altered
        /// prior to be executed as part of Redis execution pipeline.
        /// </summary>
        /// <param name="command">The Redis command that is about to be executed</param>
        /// <param name="involvedKeys">The Redis keys involved in the Redis command execution</param>
        /// <param name="involvedValues">The Redis values involved in the Redis command execution</param>
        void OnExecuting(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null);

        /// <summary>
        /// Defines behavior to be executed after a Redis command has been already executed. Command's result value can be altered
        /// prior to be returned to the Redis command's caller by re-assigning the reference since the result is passed by reference.
        /// </summary>
        /// <param name="command">The Redis command that has been already executed</param>
        /// <param name="result">The result</param>
        /// <param name="involvedKeys">The Redis keys involved in the Redis command execution</param>
        void OnExecuted(RedisCommand command, ref object result, RedisKey[] involvedKeys = null);
    }
}