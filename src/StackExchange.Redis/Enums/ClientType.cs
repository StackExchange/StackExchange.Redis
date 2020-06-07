using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// The class of the connection
    /// </summary>
    public enum ClientType
    {
        /// <summary>
        /// Regular connections, including MONITOR connections
        /// </summary>
        Normal = 0,
        /// <summary>
        /// Replication connections
        /// </summary>
        [Obsolete(Messages.PreferReplica)]
        Slave = 1,
        /// <summary>
        /// Replication connections
        /// </summary>
        Replica = 1,
        /// <summary>
        /// Subscription connections
        /// </summary>
        PubSub = 2,
    }
}
