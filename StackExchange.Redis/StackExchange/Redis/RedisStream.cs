namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a Redis Stream with an associated array of entries.
    /// </summary>
    public struct RedisStream
    {
        internal readonly RedisKey key;
        internal readonly RedisStreamEntry[] entries;

        /// <summary>
        /// Initializes a <see cref="RedisStream"/> instance.
        /// </summary>
        /// <param name="key">The key for the stream.</param>
        /// <param name="entries">An arry of entries contained within the stream.</param>
        public RedisStream(RedisKey key, RedisStreamEntry[] entries)
        {
            this.key = key;
            this.entries = entries;
        }

        /// <summary>
        /// The key for the stream.
        /// </summary>
        public RedisKey Key => key;

        /// <summary>
        /// An arry of entries contained within the stream.
        /// </summary>
        public RedisStreamEntry[] Entries => entries;
    }
}
