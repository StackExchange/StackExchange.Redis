using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    internal class MaintenanceNotificationListener : IObservable<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;
        private List<IObserver<AzureMaintenanceEvent>> observers = new List<IObserver<AzureMaintenanceEvent>>();

        internal MaintenanceNotificationListener(ConnectionMultiplexer multiplexer)
        {
            this.multiplexer = multiplexer;
        }

        public IDisposable Subscribe(IObserver<AzureMaintenanceEvent> observer)
        {
            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }
            return new Unsubscriber<AzureMaintenanceEvent>(observers, observer);
        }

        internal async Task StartListening(LogProxy logProxy)
        {
            try
            {
                var sub = multiplexer.GetSubscriber();
                if (sub == null)
                {
                    logProxy?.WriteLine("Failed to GetSubscriber for AzureRedisEvents");
                    return;
                }

                await sub.SubscribeAsync("AzureRedisEvents", (channel, message) =>
                {
                    var newMessage = new AzureMaintenanceEvent(message);
                    foreach (var observer in observers)
                    {
                        observer?.OnNext(newMessage);
                    }
                }).ForAwait();
            }
            catch (Exception e)
            {
                logProxy?.WriteLine($"Encountered exception: {e}");
            }
        }
    }

    internal class Unsubscriber<ServerEndPointMaintenanceEvent> : IDisposable
    {
        private List<IObserver<ServerEndPointMaintenanceEvent>> _observers;
        private IObserver<ServerEndPointMaintenanceEvent> _observer;

        internal Unsubscriber(List<IObserver<ServerEndPointMaintenanceEvent>> observers, IObserver<ServerEndPointMaintenanceEvent> observer)
        {
            this._observers = observers;
            this._observer = observer;
        }

        public void Dispose()
        {
            _observers.Remove(_observer);
        }
    }

    /// <summary>
    /// Azure node maintenance event details
    /// </summary>
    public class AzureMaintenanceEvent
    {
        internal AzureMaintenanceEvent(string message)
        {
            RawMessage = message;
            try
            {
                var info = message?.Split('|');
                for (int i = 0; i < info?.Length / 2; i++)
                {
                    string key = null, value = null;
                    if (2 * i < info.Length) { key = info[2 * i].Trim(); }
                    if (2 * i + 1 < info.Length) { value = info[2 * i + 1].Trim(); }
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        switch (key.ToLowerInvariant())
                        {
                            case "notificationtype":
                                NotificationType = value;
                                break;

                            case "starttimeinutc":
                                if (DateTime.TryParse(value, out DateTime startTime))
                                {
                                    StartTimeUtc = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
                                }
                                break;

                            case "isreplica":
                                bool.TryParse(value, out IsReplica);
                                break;

                            case "ipaddress":
                                IPAddress.TryParse(value, out IpAddress);
                                break;

                            case "sslport":
                                Int32.TryParse(value, out var port);
                                SSLPort = port;
                                break;

                            case "nonsslport":
                                Int32.TryParse(value, out var nonsslport);
                                NonSSLPort = nonsslport;
                                break;

                            default:
                                break;
                        }
                    }
                }
            }
            catch { }
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
        public readonly DateTime? StartTimeUtc;

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
                $"{nameof(StartTimeUtc)}|{StartTimeUtc:s}|" +
                $"{nameof(IsReplica)}|{IsReplica}|" +
                $"{nameof(IpAddress)}|{IpAddress}|" +
                $"{nameof(SSLPort)}|{SSLPort}|" +
                $"{nameof(NonSSLPort)}|{NonSSLPort}";
        }
    }
}
