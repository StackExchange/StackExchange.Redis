namespace StackExchange.Redis;

/// <summary>
/// Describes a consumer off a Redis Stream.
/// </summary>
public readonly struct StreamConsumer
{
    internal StreamConsumer(RedisValue name, int pendingMessageCount)
    {
        Name = name;
        PendingMessageCount = pendingMessageCount;
    }

    /// <summary>
    /// The name of the consumer.
    /// </summary>
    public RedisValue Name { get; }

    /// <summary>
    /// The number of messages that have been delivered by not yet acknowledged by the consumer.
    /// </summary>
    public int PendingMessageCount { get; }
}
