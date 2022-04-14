using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// The result of the XAUTOCLAIM command.
    /// </summary>
    public readonly struct StreamAutoClaimResult
    {
        internal StreamAutoClaimResult(RedisValue nextStartId, StreamEntry[] claimedEntries, RedisValue[] deletedIds)
        {
            NextStartId = nextStartId;
            ClaimedEntries = claimedEntries ?? Array.Empty<StreamEntry>();
            DeletedIds = deletedIds ?? Array.Empty<RedisValue>();
        }

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
}
