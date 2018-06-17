
namespace StackExchange.Redis
{
    /// <summary>
    /// 
    /// </summary>
    public class StreamPendingInfo
    {
        internal StreamPendingInfo(RedisValue pendingMessageCount,
            RedisValue lowestId,
            RedisValue highestId,
            StreamConsumer[] consumers)
        {
            PendingMessageCount = pendingMessageCount;
            LowestPendingMessageId = lowestId;
            HighestPendingMessageId = highestId;
            Consumers = consumers;
        }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue PendingMessageCount { get; }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue LowestPendingMessageId { get; }

        /// <summary>
        /// 
        /// </summary>
        public RedisValue HighestPendingMessageId { get; }

        /// <summary>
        /// 
        /// </summary>
        public StreamConsumer[] Consumers { get; }

    }
}
