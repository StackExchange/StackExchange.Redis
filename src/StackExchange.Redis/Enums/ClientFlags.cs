using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// The client flags can be a combination of:
    /// O: the client is a replica in MONITOR mode
    /// S: the client is a normal replica server
    /// M: the client is a master
    /// x: the client is in a MULTI/EXEC context
    /// b: the client is waiting in a blocking operation
    /// i: the client is waiting for a VM I/O (deprecated)
    /// d: a watched keys has been modified - EXEC will fail
    /// c: connection to be closed after writing entire reply
    /// u: the client is unblocked
    /// A: connection to be closed ASAP
    /// N: no specific flag set
    /// </summary>
    [Flags]
    public enum ClientFlags : long
    {
        /// <summary>
        /// no specific flag set
        /// </summary>
        None = 0,
        /// <summary>
        /// the client is a replica in MONITOR mode
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicaMonitor) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        SlaveMonitor = 1,
        /// <summary>
        /// the client is a replica in MONITOR mode
        /// </summary>
        ReplicaMonitor = 1, // as an implementation detail, note that enum.ToString on [Flags] prefers *later* options when naming Flags
        /// <summary>
        /// the client is a normal replica server
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(Replica) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Slave = 2,
        /// <summary>
        /// the client is a normal replica server
        /// </summary>
        Replica = 2, // as an implementation detail, note that enum.ToString on [Flags] prefers *later* options when naming Flags
        /// <summary>
        /// the client is a master
        /// </summary>
        Master = 4,
        /// <summary>
        /// the client is in a MULTI/EXEC context
        /// </summary>
        Transaction = 8,
        /// <summary>
        /// the client is waiting in a blocking operation
        /// </summary>
        Blocked = 16,
        /// <summary>
        /// a watched keys has been modified - EXEC will fail
        /// </summary>
        TransactionDoomed = 32,
        /// <summary>
        /// connection to be closed after writing entire reply
        /// </summary>
        Closing = 64,
        /// <summary>
        /// the client is unblocked
        /// </summary>
        Unblocked = 128,
        /// <summary>
        /// connection to be closed ASAP
        /// </summary>
        CloseASAP = 256,
        /// <summary>
        /// the client is a Pub/Sub subscriber
        /// </summary>
        PubSubSubscriber = 512,
        /// <summary>
        /// the client is in readonly mode against a cluster node
        /// </summary>
        ReadOnlyCluster = 1024,
        /// <summary>
        /// the client is connected via a Unix domain socket
        /// </summary>
        UnixDomainSocket = 2048,
    }
}
