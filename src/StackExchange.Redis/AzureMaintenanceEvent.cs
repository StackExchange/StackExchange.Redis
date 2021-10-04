using System;
using System.Net;
using System.Threading.Tasks;
using System.Buffers.Text;
using static StackExchange.Redis.ConnectionMultiplexer;
using System.Globalization;

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
                                NotificationType = value.ToString();
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
                            case var _ when key.SequenceEqual(nameof(SSLPort).AsSpan()) && Int32.TryParse(value, out var port):
                                SSLPort = port;
                                break;
                            case var _ when key.SequenceEqual(nameof(NonSSLPort).AsSpan()) && Int32.TryParse(value, out var nonsslport):
                                NonSSLPort = nonsslport;
                                break;
                            default:
                                break;
                        }
#else
                        switch (key)
                        {
                            case var _ when key.SequenceEqual(nameof(NotificationType).AsSpan()):
                                NotificationType = value.ToString();
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
                            case var _ when key.SequenceEqual(nameof(SSLPort).AsSpan()) && Int32.TryParse(value.ToString(), out var port):
                                SSLPort = port;
                                break;
                            case var _ when key.SequenceEqual(nameof(NonSSLPort).AsSpan()) && Int32.TryParse(value.ToString(), out var nonsslport):
                                NonSSLPort = nonsslport;
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
        public string RawMessage { get; }

        /// <summary>
        /// indicates the event type
        /// </summary>
        public string NotificationType { get; }

        /// <summary>
        /// indicates the start time of the event
        /// </summary>
        public DateTime? StartTimeUtc { get; }

        /// <summary>
        /// indicates if the event is for a replica node
        /// </summary>
        public bool IsReplica { get; }

        /// <summary>
        /// IPAddress of the node event is intended for
        /// </summary>
        public IPAddress IPAddress { get; }

        /// <summary>
        /// ssl port
        /// </summary>
        public int SSLPort { get; }

        /// <summary>
        /// non-ssl port
        /// </summary>
        public int NonSSLPort { get; }

        /// <summary>
        /// Returns a string representing the maintenance event with all of its properties
        /// </summary>
        public override string ToString()
            => RawMessage;
    }
}
