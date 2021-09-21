using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace StackExchange.Redis
{
    internal class ServerEndPointMaintenanceTracker : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;
        internal ConcurrentDictionary<IPEndPoint, DateTime> maintenanceStartTimesUtc = new ConcurrentDictionary<IPEndPoint, DateTime>();
        internal ConcurrentDictionary<DnsEndPoint, IPEndPoint> resolvedEndpoints = new ConcurrentDictionary<DnsEndPoint, IPEndPoint>();

        internal ServerEndPointMaintenanceTracker(ConnectionMultiplexer multiplexer)
        {
            this.multiplexer = multiplexer;
        }

        internal bool TryParseIPEndPoint(EndPoint endPoint, out IPEndPoint ipEndPoint)
        {
            ipEndPoint = null;
            if (endPoint is DnsEndPoint)
            {
                ipEndPoint = ResolveDNSEndpoint(endPoint);
            }
            else if (endPoint is IPEndPoint)
            {
                ipEndPoint = (IPEndPoint)endPoint;
            }
            return ipEndPoint != null;
        }

        internal bool IsMaintenanceGoingToHappen(ServerEndPoint serverEndPoint)
        {
            if (TryParseIPEndPoint(serverEndPoint.EndPoint, out var endPoint))
            {
                if (endPoint != null && maintenanceStartTimesUtc.TryGetValue(endPoint, out var maintenanceStartTimeUtc))
                {
                    var diff = maintenanceStartTimeUtc.Subtract(DateTime.UtcNow);
                    return (diff > TimeSpan.Zero && diff < TimeSpan.FromMilliseconds(500));
                }
            }
            return false;
        }

        private IPEndPoint ResolveDNSEndpoint(EndPoint endPoint)
        {
            var dns = (DnsEndPoint)endPoint;
            IPEndPoint dnsResolvedEndpoint;
            if (!resolvedEndpoints.TryGetValue(dns, out dnsResolvedEndpoint))
            {
                var ips = Dns.GetHostAddresses(dns.Host);
                if (ips.Length == 1)
                {
                    var ipEndPoint = new IPEndPoint(ips[0], dns.Port);
                    resolvedEndpoints[dns] = ipEndPoint;
                    return ipEndPoint;
                }
            }
            return dnsResolvedEndpoint;
        }

        public void OnNext(AzureMaintenanceEvent newMessage)
        {
            try
            {
                if (newMessage.NotificationType == "NodeMaintenanceStarting" && newMessage.IpAddress != null)
                {
                    // clear the resolvedDNSEndpoint cache
                    resolvedEndpoints = new ConcurrentDictionary<DnsEndPoint, IPEndPoint>();
                    var ipEndpoint = new IPEndPoint(newMessage.IpAddress, multiplexer.RawConfig.Ssl ? newMessage.SSLPort : newMessage.NonSSLPort);
                    maintenanceStartTimesUtc[ipEndpoint] = (DateTime)newMessage.StartTimeUtc;
                }
            }
            catch (Exception)
            {
                // Swallow any exceptions to avoid interfering with other observers
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                // TODO(ansoedal): Log ex?
            }
        }

        public void OnError(Exception error)
        {
            return;
        }

        public void OnCompleted()
        {
            return;
        }
    }
}
