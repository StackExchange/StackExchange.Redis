using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// The result of the XAUTOCLAIM command.
    /// </summary>
    public readonly struct StreamAutoClaimResult
    {
        internal StreamAutoClaimResult(RedisValue nextStartId, StreamEntry[] claimedEntries)
        {
            NextStartId = nextStartId;
            ClaimedEntries = claimedEntries ?? Array.Empty<StreamEntry>();
        }

        /// <summary>
        /// The stream ID to be used in the next call to StreamAutoClaim.
        /// </summary>
        public RedisValue NextStartId { get; }

        /// <summary>
        /// An array of <see cref="StreamEntry"/> for the successfully claimed entries.
        /// </summary>
        public StreamEntry[] ClaimedEntries { get; }
    }
}
