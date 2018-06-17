
namespace StackExchange.Redis
{
    /// <summary>
    /// 
    /// </summary>
    public class StreamPendingMessageInfo
    {
        /// <summary>
        /// 
        /// </summary>
        internal StreamPendingMessageInfo(RedisValue messageId,
            RedisValue consumerName,
            RedisValue idleTimeInMs,
            RedisValue deliveryCount)
        {
            MessageId = messageId;
            ConsumerName = consumerName;
            IdleTimeInMilliseconds = idleTimeInMs;
            DeliveryCount = deliveryCount;
        }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue MessageId { get; }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue ConsumerName { get; }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue IdleTimeInMilliseconds { get; }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue DeliveryCount { get; }
    }
}
