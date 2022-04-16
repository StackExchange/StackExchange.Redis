namespace StackExchange.Redis;

/// <summary>
/// Describes a consumer within a consumer group, retrieved using the XINFO CONSUMERS command. <see cref="IDatabase.StreamConsumerInfo"/>.
/// </summary>
public readonly struct StreamConsumerInfo
{
    internal StreamConsumerInfo(string name, int pendingMessageCount, long idleTimeInMilliseconds)
    {
        Name = name;
        PendingMessageCount = pendingMessageCount;
        IdleTimeInMilliseconds = idleTimeInMilliseconds;
    }

    /// <summary>
    /// The name of the consumer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The number of pending messages for the consumer. A pending message is one that has been
    /// received by the consumer but not yet acknowledged.
    /// </summary>
    public int PendingMessageCount { get; }

    /// <summary>
    /// The idle time, if any, for the consumer.
    /// </summary>
    public long IdleTimeInMilliseconds { get; }
}
