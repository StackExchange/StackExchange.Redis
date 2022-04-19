namespace StackExchange.Redis;

/// <summary>
/// Describes basic information about pending messages for a consumer group.
/// </summary>
public readonly struct StreamPendingInfo
{
    internal StreamPendingInfo(int pendingMessageCount, RedisValue lowestId, RedisValue highestId, StreamConsumer[] consumers)
    {
        PendingMessageCount = pendingMessageCount;
        LowestPendingMessageId = lowestId;
        HighestPendingMessageId = highestId;
        Consumers = consumers;
    }

    /// <summary>
    /// The number of pending messages. A pending message is a message that has been consumed but not yet acknowledged.
    /// </summary>
    public int PendingMessageCount { get; }

    /// <summary>
    /// The lowest message ID in the set of pending messages.
    /// </summary>
    public RedisValue LowestPendingMessageId { get; }

    /// <summary>
    /// The highest message ID in the set of pending messages.
    /// </summary>
    public RedisValue HighestPendingMessageId { get; }

    /// <summary>
    /// An array of consumers within the consumer group that have pending messages.
    /// </summary>
    public StreamConsumer[] Consumers { get; }
}
