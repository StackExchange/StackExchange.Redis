namespace StackExchange.Redis;

/// <summary>
/// Describes stream information retrieved using the XINFO STREAM command. <see cref="IDatabase.StreamInfo"/>.
/// </summary>
public readonly struct StreamInfo
{
    internal StreamInfo(int length, int radixTreeKeys, int radixTreeNodes, int groups, StreamEntry firstEntry, StreamEntry lastEntry, RedisValue lastGeneratedId)
    {
        Length = length;
        RadixTreeKeys = radixTreeKeys;
        RadixTreeNodes = radixTreeNodes;
        ConsumerGroupCount = groups;
        FirstEntry = firstEntry;
        LastEntry = lastEntry;
        LastGeneratedId = lastGeneratedId;
    }

    /// <summary>
    /// The number of entries in the stream.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// The number of radix tree keys in the stream.
    /// </summary>
    public int RadixTreeKeys { get; }

    /// <summary>
    /// The number of radix tree nodes in the stream.
    /// </summary>
    public int RadixTreeNodes { get; }

    /// <summary>
    /// The number of consumers groups in the stream.
    /// </summary>
    public int ConsumerGroupCount { get; }

    /// <summary>
    /// The first entry in the stream.
    /// </summary>
    public StreamEntry FirstEntry { get; }

    /// <summary>
    /// The last entry in the stream.
    /// </summary>
    public StreamEntry LastEntry { get; }

    /// <summary>
    /// The last generated id.
    /// </summary>
    public RedisValue LastGeneratedId { get; }
}
