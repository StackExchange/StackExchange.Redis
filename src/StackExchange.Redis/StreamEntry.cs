using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes an entry contained in a Redis Stream.
    /// </summary>
    public readonly struct StreamEntry
    {
        internal StreamEntry(RedisValue id, NameValueEntry[] values)
        {
            Id = id;
            Values = values;
        }

        /// <summary>
        /// A null stream entry.
        /// </summary>
        public static StreamEntry Null { get; } = new StreamEntry(RedisValue.Null, null);

        /// <summary>
        /// The ID assigned to the message.
        /// </summary>
        public RedisValue Id { get; }

        /// <summary>
        /// The values contained within the message.
        /// </summary>
        public NameValueEntry[] Values { get; }

        /// <summary>
        /// Search for a specific field by name, returning the value
        /// </summary>
        public RedisValue this[RedisValue fieldName]
        {
            get
            {
                var values = Values;
                if (values != null)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (values[i].name == fieldName)
                            return values[i].value;
                    }
                }
                return RedisValue.Null;
            }
        }

        /// <summary>
        /// Indicates that the Redis Stream Entry is null.
        /// </summary>
        public bool IsNull => Id == RedisValue.Null && Values == null;
    }
}
