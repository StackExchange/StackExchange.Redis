
namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a consumer off a Redis Stream.
    /// </summary>
    public class StreamConsumer
    {
        /// <summary>
        /// The name of the consumer.
        /// </summary>
        public RedisValue Name { get; set; }

        /// <summary>
        /// The number of messages that have been delivered by not yet acknowledged by the consumer.
        /// </summary>
        public RedisValue PendingMessageCount { get; set; }
    }
}
