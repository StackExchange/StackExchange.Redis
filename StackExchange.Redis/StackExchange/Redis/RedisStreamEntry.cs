namespace StackExchange.Redis
{
    /// <summary>
    /// Describes an entry contained in a Redis Stream.
    /// </summary>
    public struct RedisStreamEntry
    {
        internal RedisStreamEntry(RedisValue id, NameValueEntry[] values)
        {
            Id = id;
            Values = values;
        }

        /// <summary>
        /// A null stream entry.
        /// </summary>
        public static RedisStreamEntry Null { get; } = new RedisStreamEntry(RedisValue.Null, null);

        /// <summary>
        /// The ID assigned to the message.
        /// </summary>
        public RedisValue Id { get; }

        /// <summary>
        /// The values contained within the message.
        /// </summary>
        public NameValueEntry[] Values { get; }

        /// <summary>
        /// Indicates that the Redis Stream Entry is null.
        /// </summary>
        public bool IsNull => Id == RedisValue.Null && Values == null;
    }
}
