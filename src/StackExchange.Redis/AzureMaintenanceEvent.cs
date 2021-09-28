using System;
using System.Net;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    /// <summary>
    /// Azure node maintenance event. For more information, please see: https://github.com/Azure/AzureCacheForRedis/blob/main/AzureRedisEvents.md
    /// </summary>
    public class AzureMaintenanceEvent
    {
        private const string PubSubChannelName = "AzureRedisEvents";

        internal AzureMaintenanceEvent(string azureEvent)
        {
            if (azureEvent == null)
            {
                return;
            }

            // The message consists of key-value pairs delimted by pipes. For example, a message might look like:
            // NotificationType|NodeMaintenanceStarting|StartTimeUtc|2021-09-23T12:34:19|IsReplica|False|IpAddress|13.67.42.199|SSLPort|15001|NonSSLPort|13001
            var message = new ReadOnlySpan<char>(azureEvent.ToCharArray());
            try
            {
                while (message.Length > 0)
                {
                    if (message[0] == '|')
                    {
                        message = message.Slice(1);
                        continue;
                    }

                    // Grab the next pair
                    var key = message.Slice(0, message.IndexOf('|'));
                    message = message.Slice(key.Length + 1);

                    var valueEnd = message.IndexOf('|');
                    var value = valueEnd > -1 ? message.Slice(0, valueEnd) : message;
                    message = message.Slice(value.Length);

                    if (key.Length > 0 && value.Length > 0)
                    {
                        switch (key)
                        {
                            case var _ when key.SequenceEqual(nameof(NotificationType).ToCharArray()):
                                NotificationType = value.ToString();
                                break;
                            case var _ when key.SequenceEqual(nameof(StartTimeInUTC).ToCharArray()) && DateTime.TryParse(value.ToString(), out DateTime startTime):
                                StartTimeInUTC = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
                                break;
                            case var _ when key.SequenceEqual(nameof(IsReplica).ToCharArray()) && bool.TryParse(value.ToString(), out var isReplica):
                                IsReplica = isReplica;
                                break;
                            case var _ when key.SequenceEqual(nameof(IPAddress).ToCharArray()) && IPAddress.TryParse(value.ToString(), out var ipAddress):
                                IpAddress = ipAddress;
                                break;
                            case var _ when key.SequenceEqual(nameof(SSLPort).ToCharArray()) && Int32.TryParse(value.ToString(), out var port):
                                SSLPort = port;
                                break;
                            case var _ when key.SequenceEqual(nameof(NonSSLPort).ToCharArray()) && Int32.TryParse(value.ToString(), out var nonsslport):
                                NonSSLPort = nonsslport;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            catch
            {
                // TODO: Append to rolling debug log when it's present
            }
        }

        internal async static Task AddListenerAsync(ConnectionMultiplexer multiplexer, LogProxy logProxy)
        {
            try
            {
                var sub = multiplexer.GetSubscriber();
                if (sub == null)
                {
                    logProxy?.WriteLine("Failed to GetSubscriber for AzureRedisEvents");
                    return;
                }

                await sub.SubscribeAsync(PubSubChannelName, (channel, message) =>
                {
                    var newMessage = new AzureMaintenanceEvent(message);
                    multiplexer.InvokeServerMaintenanceEvent(newMessage);

                    if (newMessage.NotificationType.Equals("NodeMaintenanceEnded") || newMessage.NotificationType.Equals("NodeMaintenanceFailover"))
                    {
                        multiplexer.ReconfigureAsync(first: false, reconfigureAll: true, log: logProxy, blame: null, cause: $"Azure Event: {newMessage.NotificationType}").Wait();
                    }
                }).ForAwait();
            }
            catch (Exception e)
            {
                logProxy?.WriteLine($"Encountered exception: {e}");
            }
        }

        /// <summary>
        /// Raw message received from the server
        /// </summary>
        public readonly string RawMessage;

        /// <summary>
        /// indicates the event type
        /// </summary>
        public readonly string NotificationType;

        /// <summary>
        /// indicates the start time of the event
        /// </summary>
        public readonly DateTime? StartTimeInUTC;

        /// <summary>
        /// indicates if the event is for a replica node
        /// </summary>
        public readonly bool IsReplica;

        /// <summary>
        /// IPAddress of the node event is intended for
        /// </summary>
        public readonly IPAddress IpAddress;

        /// <summary>
        /// ssl port
        /// </summary>
        public readonly int SSLPort;

        /// <summary>
        /// non-ssl port
        /// </summary>
        public readonly int NonSSLPort;

        /// <summary>
        /// Returns a string representing the maintenance event with all of its properties
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(NotificationType)}|{NotificationType}|" +
                $"{nameof(StartTimeInUTC)}|{StartTimeInUTC:s}|" +
                $"{nameof(IsReplica)}|{IsReplica}|" +
                $"{nameof(IpAddress)}|{IpAddress}|" +
                $"{nameof(SSLPort)}|{SSLPort}|" +
                $"{nameof(NonSSLPort)}|{NonSSLPort}";
        }
    }
}
