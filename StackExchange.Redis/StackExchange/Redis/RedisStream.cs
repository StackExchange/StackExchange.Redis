﻿namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a Redis Stream with an associated array of entries.
    /// </summary>
    public struct RedisStream
    {
        internal RedisStream(RedisKey key, StreamEntry[] entries)
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
        public StreamEntry[] Entries { get; }
    }
}
