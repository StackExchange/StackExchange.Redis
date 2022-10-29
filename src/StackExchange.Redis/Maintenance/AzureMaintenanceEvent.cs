using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
#if NETCOREAPP
using System.Buffers.Text;
#endif

namespace StackExchange.Redis.Maintenance
{
    /// <summary>
    /// Azure node maintenance event. For more information, please see: <see href="https://aka.ms/redis/maintenanceevents"/>.
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

            // The message consists of key-value pairs delimited by pipes. For example, a message might look like:
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
#if NETCOREAPP
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
                            case var _ when key.SequenceEqual("SSLPort".AsSpan()) && Format.TryParseInt32(value, out var port):
                                SslPort = port;
                                break;
                            case var _ when key.SequenceEqual("NonSSLPort".AsSpan()) && Format.TryParseInt32(value, out var nonsslport):
                                NonSslPort = nonsslport;
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
                            case var _ when key.SequenceEqual("SSLPort".AsSpan()) && Format.TryParseInt32(value.ToString(), out var port):
                                SslPort = port;
                                break;
                            case var _ when key.SequenceEqual("NonSSLPort".AsSpan()) && Format.TryParseInt32(value.ToString(), out var nonsslport):
                                NonSslPort = nonsslport;
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

        internal async static Task AddListenerAsync(ConnectionMultiplexer multiplexer, Action<string>? log = null)
        {
            if (!multiplexer.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE))
            {
                return;
            }

            try
            {
                var sub = multiplexer.GetSubscriber();
                if (sub == null)
                {
                    log?.Invoke("Failed to GetSubscriber for AzureRedisEvents");
                    return;
                }

                await sub.SubscribeAsync(PubSubChannelName, async (_, message) =>
                {
                    var newMessage = new AzureMaintenanceEvent(message!);
                    newMessage.NotifyMultiplexer(multiplexer);

                    switch (newMessage.NotificationType)
                    {
                        case AzureNotificationType.NodeMaintenanceEnded:
                        case AzureNotificationType.NodeMaintenanceFailoverComplete:
                        case AzureNotificationType.NodeMaintenanceScaleComplete:
                            await multiplexer.ReconfigureAsync($"Azure Event: {newMessage.NotificationType}").ForAwait();
                            break;
                    }
                }).ForAwait();
            }
            catch (Exception e)
            {
                log?.Invoke($"Encountered exception: {e}");
            }
        }

        /// <summary>
        /// Indicates the type of event (raw string form).
        /// </summary>
        public string NotificationTypeString { get; } = "Unknown";

        /// <summary>
        /// The parsed version of <see cref="NotificationTypeString"/> for easier consumption.
        /// </summary>
        public AzureNotificationType NotificationType { get; }

        /// <summary>
        /// Indicates if the event is for a replica node.
        /// </summary>
        public bool IsReplica { get; }

        /// <summary>
        /// IPAddress of the node event is intended for.
        /// </summary>
        public IPAddress? IPAddress { get; }

        /// <summary>
        /// SSL Port.
        /// </summary>
        public int SslPort { get; }

        /// <summary>
        /// Non-SSL port.
        /// </summary>
        public int NonSslPort { get; }

        private static AzureNotificationType ParseNotificationType(string typeString) => typeString switch
        {
            "NodeMaintenanceScheduled" => AzureNotificationType.NodeMaintenanceScheduled,
            "NodeMaintenanceStarting" => AzureNotificationType.NodeMaintenanceStarting,
            "NodeMaintenanceStart" => AzureNotificationType.NodeMaintenanceStart,
            "NodeMaintenanceEnded" => AzureNotificationType.NodeMaintenanceEnded,
            // This is temporary until server changes go into effect - to be removed in later versions
            "NodeMaintenanceFailover" => AzureNotificationType.NodeMaintenanceFailoverComplete,
            "NodeMaintenanceFailoverComplete" => AzureNotificationType.NodeMaintenanceFailoverComplete,
            "NodeMaintenanceScaleComplete" => AzureNotificationType.NodeMaintenanceScaleComplete,
            _ => AzureNotificationType.Unknown,
        };
    }
}
