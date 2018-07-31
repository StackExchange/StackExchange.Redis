using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a value/expiry pair
    /// </summary>
    public struct RedisValueWithExpiry
    {
        internal RedisValueWithExpiry(RedisValue value, TimeSpan? expiry)
        {
            Value = value;
            Expiry = expiry;
        }

        /// <summary>
        /// The expiry of this record
        /// </summary>
        public TimeSpan? Expiry { get; }

        /// <summary>
        /// The value of this record
        /// </summary>
        public RedisValue Value { get; }
    }
}