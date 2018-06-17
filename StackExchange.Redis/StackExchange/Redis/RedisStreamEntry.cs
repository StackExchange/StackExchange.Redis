namespace StackExchange.Redis
{
    /// <summary>
    /// Describes an entry contained in a Redis Stream.
    /// </summary>
    public struct RedisStreamEntry
    {
        internal readonly RedisValue id;
        internal readonly NameValueEntry[] values;

        /// <summary>
        /// Initializes a <see cref="RedisStreamEntry"/> instance.
        /// </summary>
        /// <param name="id">The ID assigned to the message.</param>
        /// <param name="values">The values contained within the message.</param>
        public RedisStreamEntry(RedisValue id, NameValueEntry[] values)
        {
            this.id = id;
            this.values = values;
        }

        /// <summary>
        /// The ID assigned to the message.
        /// </summary>
        public RedisValue Id => id;

        /// <summary>
        /// The values contained within the message.
        /// </summary>
        public NameValueEntry[] Values => values;
    }
}
