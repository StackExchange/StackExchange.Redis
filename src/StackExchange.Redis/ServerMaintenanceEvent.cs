using System;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    /// <summary>
    /// Base class for all server maintenance events
    /// </summary>
    public class ServerMaintenanceEvent
    {
        internal ServerMaintenanceEvent() { }

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
        /// Raw message received from the server
        /// </summary>
        public string RawMessage { get; protected set; }

        /// <summary>
        /// indicates the start time of the event
        /// </summary>
        public DateTime? StartTimeUtc { get; protected set; }

        /// <summary>
        /// Returns a string representing the maintenance event with all of its properties
        /// </summary>
        public override string ToString()
            => RawMessage;
    }
}
