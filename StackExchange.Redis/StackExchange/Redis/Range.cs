
namespace StackExchange.Redis
{
    /// <summary>
    /// Options for reading a range of messages in a stream.
    /// </summary>
    public struct Range
    {
        private Range(RedisValue minId, RedisValue maxId, Order order = Order.Ascending)
        {
            MinId = minId;
            MaxId = maxId;
            Order = order;
        }

        internal RedisValue MinId { get; }
        internal RedisValue MaxId { get; }
        internal Order Order { get; }

        /// <summary>
        /// Read all messages in a stream in ascending order.
        /// </summary>
        public static Range All = new Range("-", "+");

        /// <summary>
        /// Read all messages in a stream in descending order.
        /// </summary>
        public static Range AllDescending = new Range("-", "+", Order.Descending);

        /// <summary>
        /// Read messages within the given range in ascending order.
        /// </summary>
        /// <param name="minId">The minimum message ID to read.</param>
        /// <param name="maxId">The maximum message ID to read.</param>
        /// <returns></returns>
        public static Range Ascending(RedisValue minId, RedisValue maxId) => new Range(minId, maxId);

        /// <summary>
        /// Read messages within the given range in descending order.
        /// </summary>
        /// <param name="maxId">The maximum message ID to read.</param>
        /// <param name="minId">The minimum message ID to read.</param>
        /// <returns></returns>
        public static Range Descending(RedisValue maxId, RedisValue minId) => new Range(minId, maxId, Order.Descending);
    }
}
