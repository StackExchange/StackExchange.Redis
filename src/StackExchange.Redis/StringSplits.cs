namespace StackExchange.Redis
{
    internal static class StringSplits
    {
        public static readonly char[]
            Space = { ' ' },
            Comma = { ',' };
    }

    internal static class Messages
    {
        public const string PreferReplica = "Starting with Redis version 5, redis has moved to 'replica' terminology.";
    }
}
