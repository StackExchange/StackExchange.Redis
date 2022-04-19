using System;

namespace StackExchange.Redis;

/// <summary>
/// Result of the <see href="https://redis.io/commands/xautoclaim/">XAUTOCLAIM</see> command.
/// </summary>
public readonly struct StreamAutoClaimResult
{
    internal StreamAutoClaimResult(RedisValue nextStartId, StreamEntry[] claimedEntries, RedisValue[] deletedIds)
    {
        NextStartId = nextStartId;
        ClaimedEntries = claimedEntries;
        DeletedIds = deletedIds;
    }

    /// <summary>
    /// A null <see cref="StreamAutoClaimResult"/>, indicating no results.
    /// </summary>
    public static StreamAutoClaimResult Null { get; } = new StreamAutoClaimResult(RedisValue.Null, Array.Empty<StreamEntry>(), Array.Empty<RedisValue>());

    /// <summary>
    /// Whether this object is null/empty.
    /// </summary>
    public bool IsNull => NextStartId.IsNull && ClaimedEntries == Array.Empty<StreamEntry>() && DeletedIds == Array.Empty<RedisValue>();

    /// <summary>
    /// The stream ID to be used in the next call to StreamAutoClaim.
    /// </summary>
    public RedisValue NextStartId { get; }

    /// <summary>
    /// An array of <see cref="StreamEntry"/> for the successfully claimed entries.
    /// </summary>
    public StreamEntry[] ClaimedEntries { get; }

    /// <summary>
    /// An array of message IDs deleted from the stream.
    /// </summary>
    public RedisValue[] DeletedIds { get; }
}
