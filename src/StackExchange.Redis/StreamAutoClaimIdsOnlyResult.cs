namespace StackExchange.Redis
{
    /// <summary>
    /// The result of the XAUTOCLAIM command with the JUSTID option.
    /// </summary>
    public readonly struct StreamAutoClaimIdsOnlyResult
    {
        internal StreamAutoClaimIdsOnlyResult(RedisValue nextStartId, RedisValue[] claimedEntryIds, RedisValue[] deletedIds)
        {
            NextStartId = nextStartId;
            ClaimedEntryIds = claimedEntryIds;
            DeletedIds = deletedIds;
        }

        /// <summary>
        /// The stream ID to be used in the next call to StreamAutoClaim.
        /// </summary>
        public RedisValue NextStartId { get; }

        /// <summary>
        /// Array of IDs claimed by the command.
        /// </summary>
        public RedisValue[] ClaimedEntryIds { get; }

        /// <summary>
        /// Array of message IDs deleted from the stream.
        /// </summary>
        public RedisValue[] DeletedIds { get; }
    }
}
