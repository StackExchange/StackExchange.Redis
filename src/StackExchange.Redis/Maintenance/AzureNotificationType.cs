namespace StackExchange.Redis.Maintenance
{
    /// <summary>
    /// The types of notifications that Azure is sending for events happening.
    /// </summary>
    public enum AzureNotificationType
    {
        /// <summary>
        /// Unrecognized event type, likely needs a library update to recognize new events.
        /// </summary>
        Unknown,

        /// <summary>
        /// Indicates that a maintenance event is scheduled. May be several minutes from now.
        /// </summary>
        NodeMaintenanceScheduled,

        /// <summary>
        /// This event gets fired ~20s before maintenance begins.
        /// </summary>
        NodeMaintenanceStarting,

        /// <summary>
        /// This event gets fired when maintenance is imminent (&lt;5s).
        /// </summary>
        NodeMaintenanceStart,

        /// <summary>
        /// Indicates that the node maintenance operation is over.
        /// </summary>
        NodeMaintenanceEnded,

        /// <summary>
        /// Indicates that a replica has been promoted to primary.
        /// </summary>
        NodeMaintenanceFailoverComplete,

        /// <summary>
        /// Indicates that a scale event (adding or removing nodes) has completed for a cluster.
        /// </summary>
        NodeMaintenanceScaleComplete,
    }
}
