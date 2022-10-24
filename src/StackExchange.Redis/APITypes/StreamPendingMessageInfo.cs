
namespace StackExchange.Redis;

/// <summary>
/// Describes properties of a pending message.
/// A pending message is one that has been received by a consumer but has not yet been acknowledged.
/// </summary>
public readonly struct StreamPendingMessageInfo
{
    internal StreamPendingMessageInfo(RedisValue messageId, RedisValue consumerName, long idleTimeInMs, int deliveryCount)
    {
        MessageId = messageId;
        ConsumerName = consumerName;
        IdleTimeInMilliseconds = idleTimeInMs;
        DeliveryCount = deliveryCount;
    }

    /// <summary>
    /// The ID of the pending message.
    /// </summary>
    public RedisValue MessageId { get; }

    /// <summary>
    /// The consumer that received the pending message.
    /// </summary>
    public RedisValue ConsumerName { get; }

    /// <summary>
    /// The time that has passed since the message was last delivered to a consumer.
    /// </summary>
    public long IdleTimeInMilliseconds { get; }

    /// <summary>
    /// The number of times the message has been delivered to a consumer.
    /// </summary>
    public int DeliveryCount { get; }
}
