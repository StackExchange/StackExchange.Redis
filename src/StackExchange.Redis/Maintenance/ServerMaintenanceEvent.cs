using System;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis.Maintenance
{
    /// <summary>
    /// Base class for all server maintenance events
    /// </summary>
    public class ServerMaintenanceEvent
    {
        internal ServerMaintenanceEvent()
        {
            ReceivedTimeUtc = DateTime.UtcNow;
        }

        internal async static Task AddListenersAsync(ConnectionMultiplexer muxer, LogProxy logProxy)
        {
            if (!muxer.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE))
            {
                return;
            }

            if (muxer.RawConfig.IsAzureEndpoint())
            {
                await AzureMaintenanceEvent.AddListenerAsync(muxer, logProxy).ForAwait();
            }
            // Other providers could be added here later
        }

        /// <summary>
        /// Raw message received from the server.
        /// </summary>
        public string RawMessage { get; protected set; }

        /// <summary>
        /// The time the event was received.
        /// </summary>
        public DateTime ReceivedTimeUtc { get; }

        /// <summary>
        /// Indicates the expected start time of the event.
        /// </summary>
        public DateTime? StartTimeUtc { get; protected set; }

        /// <summary>
        /// Returns a string representing the maintenance event with all of its properties.
        /// </summary>
        public override string ToString()
            => RawMessage;
    }
}
