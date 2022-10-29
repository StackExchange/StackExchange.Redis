using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// The class of the connection
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Compatibility")]
    public enum ClientType
    {
        /// <summary>
        /// Regular connections, including MONITOR connections
        /// </summary>
        Normal = 0,
        /// <summary>
        /// Replication connections
        /// </summary>
        Replica = 1, // as an implementation detail, note that enum.ToString without [Flags] prefers *earlier* values
        /// <summary>
        /// Replication connections
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(Replica) + " instead, this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        Slave = 1,
        /// <summary>
        /// Subscription connections
        /// </summary>
        PubSub = 2,
    }
}
