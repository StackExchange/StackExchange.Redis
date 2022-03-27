using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// Additional operations to perform when making a server a primary.
    /// </summary>
    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Compatibility")]
    public enum ReplicationChangeOptions
    {
        /// <summary>
        /// No additional operations.
        /// </summary>
        None = 0,
        /// <summary>
        /// Set the tie-breaker key on all available primaries, to specify this server.
        /// </summary>
        SetTiebreaker = 1,
        /// <summary>
        /// Broadcast to the pub-sub channel to listening clients to reconfigure themselves.
        /// </summary>
        Broadcast = 2,
        /// <summary>
        /// Issue a REPLICAOF to all other known nodes, making this primary of all.
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(ReplicateToOtherEndpoints) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        EnslaveSubordinates = 4,
        /// <summary>
        /// Issue a REPLICAOF to all other known nodes, making this primary of all.
        /// </summary>
        ReplicateToOtherEndpoints = 4, // note ToString prefers *later* options
        /// <summary>
        /// All additional operations.
        /// </summary>
        All = SetTiebreaker | Broadcast | ReplicateToOtherEndpoints,
    }
}
