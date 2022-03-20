using System;

namespace StackExchange.Redis.Maintenance
{
    /// <summary>
    /// Base class for all server maintenance events.
    /// </summary>
    public class ServerMaintenanceEvent
    {
        internal ServerMaintenanceEvent()
        {
            ReceivedTimeUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Raw message received from the server.
        /// </summary>
        public string? RawMessage { get; protected set; }

        /// <summary>
        /// The time the event was received. If we know when the event is expected to start <see cref="StartTimeUtc"/> will be populated.
        /// </summary>
        public DateTime ReceivedTimeUtc { get; }

        /// <summary>
        /// Indicates the expected start time of the event.
        /// </summary>
        public DateTime? StartTimeUtc { get; protected set; }

        /// <summary>
        /// Returns a string representing the maintenance event with all of its properties.
        /// </summary>
        public override string? ToString() => RawMessage;

        /// <summary>
        /// Notifies a ConnectionMultiplexer of this event, for anyone observing its <see cref="ConnectionMultiplexer.ServerMaintenanceEvent"/> handler.
        /// </summary>
        protected void NotifyMultiplexer(ConnectionMultiplexer multiplexer)
            => multiplexer.OnServerMaintenanceEvent(this);
    }
}
