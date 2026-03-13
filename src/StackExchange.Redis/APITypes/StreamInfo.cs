using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes stream information retrieved using the XINFO STREAM command. <see cref="IDatabase.StreamInfo"/>.
/// </summary>
public readonly struct StreamInfo
{
    // OK, I accept that this parameter list / size is getting silly, but: it is too late
    // to refactor this as a class.
    internal StreamInfo(
        int length,
        int radixTreeKeys,
        int radixTreeNodes,
        int groups,
        StreamEntry firstEntry,
        StreamEntry lastEntry,
        RedisValue lastGeneratedId,
        RedisValue maxDeletedEntryId,
        long entriesAdded,
        RedisValue recordedFirstEntryId,
        long idmpDuration,
        long idmpMaxSize,
        long pidsTracked,
        long iidsTracked,
        long iidsAdded,
        long iidsDuplicates)
    {
        Length = length;
        RadixTreeKeys = radixTreeKeys;
        RadixTreeNodes = radixTreeNodes;
        ConsumerGroupCount = groups;
        FirstEntry = firstEntry;
        LastEntry = lastEntry;
        LastGeneratedId = lastGeneratedId;

        // 7.0
        MaxDeletedEntryId = maxDeletedEntryId;
        EntriesAdded = entriesAdded;
        RecordedFirstEntryId = recordedFirstEntryId;

        // 8.6
        IdmpDuration = idmpDuration;
        IdmpMaxSize = idmpMaxSize;
        PidsTracked = pidsTracked;
        IidsTracked = iidsTracked;
        IidsAdded = iidsAdded;
        IidsDuplicates = iidsDuplicates;
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

    /// <summary>
    /// The first id recorded for the stream.
    /// </summary>
    public RedisValue RecordedFirstEntryId { get; }

    /// <summary>
    /// The count of all entries added to the stream during its lifetime.
    /// </summary>
    public long EntriesAdded { get; }

    /// <summary>
    /// The maximal entry ID that was deleted from the stream.
    /// </summary>
    public RedisValue MaxDeletedEntryId { get; }

    /// <summary>
    /// The duration value configured for the stream’s IDMP map (seconds), or <c>-1</c> if unavailable.
    /// </summary>
    public long IdmpDuration
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }

    /// <summary>
    /// The maxsize value configured for the stream’s IDMP map, or <c>-1</c> if unavailable.
    /// </summary>
    public long IdmpMaxSize
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }

    /// <summary>
    /// The number of idempotent pids currently tracked in the stream, or <c>-1</c> if unavailable.
    /// </summary>
    public long PidsTracked
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }

    /// <summary>
    /// The number of idempotent ids currently tracked in the stream, or <c>-1</c> if unavailable.
    /// This count reflects active iids that haven't expired or been evicted yet.
    /// </summary>
    public long IidsTracked
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }

    /// <summary>
    /// The count of all entries with an idempotent iid added to the stream during its lifetime, or <c>-1</c> if unavailable.
    /// This is a cumulative counter that increases with each idempotent entry added.
    /// </summary>
    public long IidsAdded
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }

    /// <summary>
    /// The count of all duplicate iids (for all pids) detected during the stream's lifetime, or <c>-1</c> if unavailable.
    /// This is a cumulative counter that increases with each duplicate iid.
    /// </summary>
    public long IidsDuplicates
    {
        [Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
        get;
    }
}
