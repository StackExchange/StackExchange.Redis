using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// Additional operations to perform when making a server a master
    /// </summary>
    [Flags]
    public enum ReplicationChangeOptions
    {
        /// <summary>
        /// No additional operations
        /// </summary>
        None = 0,
        /// <summary>
        /// Set the tie-breaker key on all available masters, to specify this server
        /// </summary>
        SetTiebreaker = 1,
        /// <summary>
        /// Broadcast to the pub-sub channel to listening clients to reconfigure themselves
        /// </summary>
        Broadcast = 2,
        /// <summary>
        /// Issue a REPLICAOF to all other known nodes, making this this master of all
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicateToOtherEndpoints) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        EnslaveSubordinates = 4,
        /// <summary>
        /// Issue a REPLICAOF to all other known nodes, making this this master of all
        /// </summary>
        ReplicateToOtherEndpoints = 4, // note ToString prefers *later* options
        /// <summary>
        /// All additional operations
        /// </summary>
        All = SetTiebreaker | Broadcast | ReplicateToOtherEndpoints,
    }
}
