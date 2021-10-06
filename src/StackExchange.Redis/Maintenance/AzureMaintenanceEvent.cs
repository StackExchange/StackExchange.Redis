using System;
using System.Net;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;
using System.Globalization;
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
using System.Buffers.Text;
#endif

namespace StackExchange.Redis.Maintenance
{
    /// <summary>
    /// Azure node maintenance event. For more information, please see: https://github.com/Azure/AzureCacheForRedis/blob/main/AzureRedisEvents.md
    /// </summary>
    public sealed class AzureMaintenanceEvent : ServerMaintenanceEvent
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
            var message = azureEvent.AsSpan();
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
                    var nextDelimiter = message.IndexOf('|');
                    if (nextDelimiter < 0)
                    {
                        // The rest of the message is not a key-value pair and is therefore malformed. Stop processing it.
                        break;
                    }

                    if (nextDelimiter == message.Length - 1)
                    {
                        // The message is missing the value for this key-value pair. It is malformed so we stop processing it.
                        break;
                    }

                    var key = message.Slice(0, nextDelimiter);
                    message = message.Slice(key.Length + 1);

                    var valueEnd = message.IndexOf('|');
                    var value = valueEnd > -1 ? message.Slice(0, valueEnd) : message;
                    message = message.Slice(value.Length);

                    if (key.Length > 0 && value.Length > 0)
                    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
                        switch (key)
                        {
                            case var _ when key.SequenceEqual(nameof(NotificationType).AsSpan()):
                                NotificationTypeString = value.ToString();
                                NotificationType = ParseNotificationType(NotificationTypeString);
                                break;
                            case var _ when key.SequenceEqual("StartTimeInUTC".AsSpan()) && DateTime.TryParseExact(value, "s", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime startTime):
                                StartTimeUtc = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
                                break;
                            case var _ when key.SequenceEqual(nameof(IsReplica).AsSpan()) && bool.TryParse(value, out var isReplica):
                                IsReplica = isReplica;
                                break;
                            case var _ when key.SequenceEqual(nameof(IPAddress).AsSpan()) && IPAddress.TryParse(value, out var ipAddress):
                                IPAddress = ipAddress;
                                break;
                            case var _ when key.SequenceEqual("SSLPort".AsSpan()) && int.TryParse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var port):
                                SslPort = port;
                                break;
                            case var _ when key.SequenceEqual("NonSSLPort".AsSpan()) && int.TryParse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var nonsslport):
                                NonSslPort = nonsslport;
                                break;
                            default:
                                break;
                        }
#else
                        switch (key)
                        {
                            case var _ when key.SequenceEqual(nameof(NotificationType).AsSpan()):
                                NotificationTypeString = value.ToString();
                                NotificationType = ParseNotificationType(NotificationTypeString);
                                break;
                            case var _ when key.SequenceEqual("StartTimeInUTC".AsSpan()) && DateTime.TryParseExact(value.ToString(), "s", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime startTime):
                                StartTimeUtc = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
                                break;
                            case var _ when key.SequenceEqual(nameof(IsReplica).AsSpan()) && bool.TryParse(value.ToString(), out var isReplica):
                                IsReplica = isReplica;
                                break;
                            case var _ when key.SequenceEqual(nameof(IPAddress).AsSpan()) && IPAddress.TryParse(value.ToString(), out var ipAddress):
                                IPAddress = ipAddress;
                                break;
                            case var _ when key.SequenceEqual("SSLPort".AsSpan()) && int.TryParse(value.ToString(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var port):
                                SslPort = port;
                                break;
                            case var _ when key.SequenceEqual("NonSSLPort".AsSpan()) && int.TryParse(value.ToString(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var nonsslport):
                                NonSslPort = nonsslport;
                                break;
                            default:
                                break;
                        }
#endif
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

                    if (newMessage.NotificationType == NotificationTypes.NodeMaintenanceEnded || newMessage.NotificationType == NotificationTypes.NodeMaintenanceFailoverComplete)
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
        /// Indicates the type of event (raw string form).
        /// </summary>
        public string NotificationTypeString { get; }

        /// <summary>
        /// The parsed version of <see cref="NotificationTypeString"/> for easier consumption.
        /// </summary>
        public NotificationTypes NotificationType { get; }

        /// <summary>
        /// Indicates if the event is for a replica node.
        /// </summary>
        public bool IsReplica { get; }

        /// <summary>
        /// IPAddress of the node event is intended for.
        /// </summary>
        public IPAddress IPAddress { get; }

        /// <summary>
        /// SSL Port.
        /// </summary>
        public int SslPort { get; }

        /// <summary>
        /// Non-SSL port.
        /// </summary>
        public int NonSslPort { get; }

        /// <summary>
        /// The types of notifications that Azure is sending for events happening.
        /// </summary>
        public enum NotificationTypes
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
        }

        private NotificationTypes ParseNotificationType(string typeString) => typeString switch
        {
            "NodeMaintenanceScheduled" => NotificationTypes.NodeMaintenanceScheduled,
            "NodeMaintenanceStarting" => NotificationTypes.NodeMaintenanceStarting,
            "NodeMaintenanceStart" => NotificationTypes.NodeMaintenanceStart,
            "NodeMaintenanceEnded" => NotificationTypes.NodeMaintenanceEnded,
            // This is temporary until server changes go into effect - to be removed in later versions
            "NodeMaintenanceFailover" => NotificationTypes.NodeMaintenanceFailoverComplete,
            "NodeMaintenanceFailoverComplete" => NotificationTypes.NodeMaintenanceFailoverComplete,
            _ => NotificationTypes.Unknown,
        };
    }
}
