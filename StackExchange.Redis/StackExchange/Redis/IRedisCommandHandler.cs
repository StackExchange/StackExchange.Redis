namespace StackExchange.Redis
{
    public interface IRedisCommandHandler
    {
        void OnExecuting(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null);
        void OnExecuted<TResult>(RedisCommand command,  ref TResult result, RedisKey[] involvedKeys = null);
    }
}