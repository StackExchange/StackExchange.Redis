
namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a consumer group retrieved using the XINFO GROUPS command. <see cref="IDatabase.StreamGroupInfo"/>
    /// </summary>
    public readonly struct StreamGroupInfo
    {
        internal StreamGroupInfo(string name, int consumerCount, int pendingMessageCount, string lastDeliveredId)
        {
            Name = name;
            ConsumerCount = consumerCount;
            PendingMessageCount = pendingMessageCount;
            LastDeliveredId = lastDeliveredId;
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
        /// The Id of the last message delivered to the group
        /// </summary>
        public string LastDeliveredId { get; }
    }
}
