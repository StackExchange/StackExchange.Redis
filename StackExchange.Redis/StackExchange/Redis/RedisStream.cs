namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a Redis Stream with an associated array of entries.
    /// </summary>
    public struct RedisStream
    {
        internal RedisStream(RedisKey key, RedisStreamEntry[] entries)
        {
            Key = key;
            Entries = entries;
        }

        /// <summary>
        /// The key for the stream.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// An arry of entries contained within the stream.
        /// </summary>
        public RedisStreamEntry[] Entries { get; }
    }
}
