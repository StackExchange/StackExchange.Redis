using System;

namespace StackExchange.Redis;

/// <summary>
/// Result of the <see href="https://redis.io/commands/xautoclaim/">XAUTOCLAIM</see> command with the <c>JUSTID</c> option.
/// </summary>
public readonly struct StreamAutoClaimIdsOnlyResult
{
    internal StreamAutoClaimIdsOnlyResult(RedisValue nextStartId, RedisValue[] claimedIds, RedisValue[] deletedIds)
    {
        NextStartId = nextStartId;
        ClaimedIds = claimedIds;
        DeletedIds = deletedIds;
    }

    /// <summary>
    /// A null <see cref="StreamAutoClaimIdsOnlyResult"/>, indicating no results.
    /// </summary>
    public static StreamAutoClaimIdsOnlyResult Null { get; } = new StreamAutoClaimIdsOnlyResult(RedisValue.Null, Array.Empty<RedisValue>(), Array.Empty<RedisValue>());

    /// <summary>
    /// Whether this object is null/empty.
    /// </summary>
    public bool IsNull => NextStartId.IsNull && ClaimedIds == Array.Empty<RedisValue>() && DeletedIds == Array.Empty<RedisValue>();

    /// <summary>
    /// The stream ID to be used in the next call to StreamAutoClaim.
    /// </summary>
    public RedisValue NextStartId { get; }

    /// <summary>
    /// Array of IDs claimed by the command.
    /// </summary>
    public RedisValue[] ClaimedIds { get; }

    /// <summary>
    /// Array of message IDs deleted from the stream.
    /// </summary>
    public RedisValue[] DeletedIds { get; }
}
