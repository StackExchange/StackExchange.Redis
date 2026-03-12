namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    private Message GetStringGcraRateLimitMessage(in RedisKey key, int maxBurst, int requestsPerPeriod, double periodSeconds, int count, CommandFlags flags)
    {
        // GCRA key max_burst requests_per_period period [NUM_REQUESTS count]
        if (count == 1)
        {
            return Message.Create(Database, flags, RedisCommand.GCRA, key, maxBurst, requestsPerPeriod, periodSeconds);
        }
        else
        {
            return Message.Create(Database, flags, RedisCommand.GCRA, key, maxBurst, requestsPerPeriod, periodSeconds, RedisLiterals.NUM_REQUESTS, count);
        }
    }
}
