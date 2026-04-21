namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal sealed class GcraMessage(
        int database,
        CommandFlags flags,
        RedisKey key,
        int maxBurst,
        int tokensPerPeriod,
        double periodSeconds,
        int count) : Message(database, flags, RedisCommand.GCRA)
    {
        protected override void WriteImpl(PhysicalConnection connection)
        {
            // GCRA key max_burst tokens_per_period period [TOKENS count]
            connection.WriteHeader(Command, ArgCount);
            connection.WriteBulkString(key);
            connection.WriteBulkString(maxBurst);
            connection.WriteBulkString(tokensPerPeriod);
            connection.WriteBulkString(periodSeconds);

            if (count != 1)
            {
                connection.WriteBulkString("TOKENS"u8);
                connection.WriteBulkString(count);
            }
        }

        public override int ArgCount
        {
            get
            {
                int argCount = 4; // key, max_burst, tokens_per_period, period
                if (count != 1) argCount += 2; // TOKENS, count
                return argCount;
            }
        }
    }
}
