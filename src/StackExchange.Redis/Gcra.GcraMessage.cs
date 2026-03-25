namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal sealed class GcraMessage(
        int database,
        CommandFlags flags,
        RedisKey key,
        int maxBurst,
        int requestsPerPeriod,
        double periodSeconds,
        int count) : Message(database, flags, RedisCommand.GCRA)
    {
        protected override void WriteImpl(in MessageWriter connection)
        {
            // GCRA key max_burst requests_per_period period [NUM_REQUESTS count]
            connection.WriteHeader(Command, ArgCount);
            connection.WriteBulkString(key);
            connection.WriteBulkString(maxBurst);
            connection.WriteBulkString(requestsPerPeriod);
            connection.WriteBulkString(periodSeconds);

            if (count != 1)
            {
                connection.WriteBulkString("NUM_REQUESTS"u8);
                connection.WriteBulkString(count);
            }
        }

        public override int ArgCount
        {
            get
            {
                int argCount = 4; // key, max_burst, requests_per_period, period
                if (count != 1) argCount += 2; // NUM_REQUESTS, count
                return argCount;
            }
        }
    }
}
