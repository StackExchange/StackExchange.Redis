namespace StackExchange.Redis;

/// <summary>
/// Describes a consumer group retrieved using the XINFO GROUPS command. <see cref="IDatabase.StreamGroupInfo"/>.
/// </summary>
public readonly struct StreamGroupInfo
{
    internal StreamGroupInfo(string name, int consumerCount, int pendingMessageCount, string? lastDeliveredId, long? entriesRead, long? lag)
    {
        Name = name;
        ConsumerCount = consumerCount;
        PendingMessageCount = pendingMessageCount;
        LastDeliveredId = lastDeliveredId;
        EntriesRead = entriesRead;
        Lag = lag;
    }

    /// <summary>
    /// The name of the consumer group.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The number of consumers within the consumer group.
    /// </summary>
    public int ConsumerCount { get; }

    /// <summary>
    /// The total number of pending messages for the consumer group. A pending message is one that has been
    /// received by a consumer but not yet acknowledged.
    /// </summary>
    public int PendingMessageCount { get; }

    /// <summary>
    /// The Id of the last message delivered to the group.
    /// </summary>
    public string? LastDeliveredId { get; }

    /// <summary>
    /// Total number of entries the group had read.
    /// </summary>
    public long? EntriesRead { get; }

    /// <summary>
    /// The number of entries in the range between the group's read entries and the stream's entries.
    /// </summary>
    public long? Lag { get; }
}
