using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// The client flags can be a combination of:
    /// <list type="table">
    ///     <item>
    ///         <term>A</term>
    ///         <description>Connection to be closed ASAP.</description>
    ///     </item>
    ///     <item>
    ///         <term>b</term>
    ///         <description>The client is waiting in a blocking operation.</description>
    ///     </item>
    ///     <item>
    ///         <term>c</term>
    ///         <description>Connection to be closed after writing entire reply.</description>
    ///     </item>
    ///     <item>
    ///         <term>d</term>
    ///         <description>A watched keys has been modified - EXEC will fail.</description>
    ///     </item>
    ///     <item>
    ///         <term>i</term>
    ///         <description>The client is waiting for a VM I/O (deprecated).</description>
    ///     </item>
    ///     <item>
    ///         <term>M</term>
    ///         <description>The client is a primary.</description>
    ///     </item>
    ///     <item>
    ///         <term>N</term>
    ///         <description>No specific flag set.</description>
    ///     </item>
    ///     <item>
    ///         <term>O</term>
    ///         <description>The client is a replica in MONITOR mode.</description>
    ///     </item>
    ///     <item>
    ///         <term>P</term>
    ///         <description>The client is a Pub/Sub subscriber.</description>
    ///     </item>
    ///     <item>
    ///         <term>r</term>
    ///         <description>The client is in readonly mode against a cluster node.</description>
    ///     </item>
    ///     <item>
    ///         <term>S</term>
    ///         <description>The client is a normal replica server.</description>
    ///     </item>
    ///     <item>
    ///         <term>u</term>
    ///         <description>The client is unblocked.</description>
    ///     </item>
    ///     <item>
    ///         <term>U</term>
    ///         <description>The client is unblocked.</description>
    ///     </item>
    ///     <item>
    ///         <term>x</term>
    ///         <description>The client is in a MULTI/EXEC context.</description>
    ///     </item>
    ///     <item>
    ///         <term>t</term>
    ///         <description>The client enabled keys tracking in order to perform client side caching.</description>
    ///     </item>
    ///     <item>
    ///         <term>R</term>
    ///         <description>The client tracking target client is invalid.</description>
    ///     </item>
    ///     <item>
    ///         <term>B</term>
    ///         <description>The client enabled broadcast tracking mode.</description>
    ///     </item>
    /// </list>
    /// </summary>
    /// <remarks><seealso href="https://redis.io/commands/client-list"/></remarks>
    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Compatibility")]
    public enum ClientFlags : long
    {
        /// <summary>
        /// No specific flag set.
        /// </summary>
        None = 0,
        /// <summary>
        /// The client is a replica in MONITOR mode.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicaMonitor) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        SlaveMonitor = 1,
        /// <summary>
        /// The client is a replica in MONITOR mode.
        /// </summary>
        ReplicaMonitor = 1, // as an implementation detail, note that enum.ToString on [Flags] prefers *later* options when naming Flags
        /// <summary>
        /// The client is a normal replica server.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(Replica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Slave = 2,
        /// <summary>
        /// The client is a normal replica server.
        /// </summary>
        Replica = 2, // as an implementation detail, note that enum.ToString on [Flags] prefers *later* options when naming Flags
        /// <summary>
        /// The client is a primary.
        /// </summary>
        Master = 4,
        /// <summary>
        /// The client is in a MULTI/EXEC context.
        /// </summary>
        Transaction = 8,
        /// <summary>
        /// The client is waiting in a blocking operation.
        /// </summary>
        Blocked = 16,
        /// <summary>
        /// A watched keys has been modified - EXEC will fail.
        /// </summary>
        TransactionDoomed = 32,
        /// <summary>
        /// Connection to be closed after writing entire reply.
        /// </summary>
        Closing = 64,
        /// <summary>
        /// The client is unblocked.
        /// </summary>
        Unblocked = 128,
        /// <summary>
        /// Connection to be closed ASAP.
        /// </summary>
        CloseASAP = 256,
        /// <summary>
        /// The client is a Pub/Sub subscriber.
        /// </summary>
        PubSubSubscriber = 512,
        /// <summary>
        /// The client is in readonly mode against a cluster node.
        /// </summary>
        ReadOnlyCluster = 1024,
        /// <summary>
        /// The client is connected via a Unix domain socket.
        /// </summary>
        UnixDomainSocket = 2048,
        /// <summary>
        /// The client enabled keys tracking in order to perform client side caching.
        /// </summary>
        KeysTracking = 4096,
        /// <summary>
        /// The client tracking target client is invalid.
        /// </summary>
        TrackingTargetInvalid = 8192,
        /// <summary>
        /// The client enabled broadcast tracking mode.
        /// </summary>
        BroadcastTracking = 16384,
    }
}
