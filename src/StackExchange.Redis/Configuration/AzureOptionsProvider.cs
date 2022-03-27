using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Options provider for Azure environments.
    /// </summary>
    public class AzureOptionsProvider : DefaultOptionsProvider
    {
        /// <summary>
        /// Allow connecting after startup, in the cases where remote cache isn't ready or is overloaded.
        /// </summary>
        public override bool AbortOnConnectFail => false;

        /// <summary>
        /// The minimum version of Redis in Azure is 4, so use the widest set of available commands when connecting.
        /// </summary>
        public override Version DefaultVersion => RedisFeatures.v4_0_0;

        /// <summary>
        /// List of domains known to be Azure Redis, so we can light up some helpful functionality
        /// for minimizing downtime during maintenance events and such.
        /// </summary>
        private static readonly string[] azureRedisDomains = new[]
        {
            ".redis.cache.windows.net",
            ".redis.cache.chinacloudapi.cn",
            ".redis.cache.usgovcloudapi.net",
            ".redis.cache.cloudapi.de",
            ".redisenterprise.cache.azure.net",
        };

        /// <inheritdoc/>
        public override bool IsMatch(EndPoint endpoint)
        {
            if (endpoint is DnsEndPoint dnsEp)
            {
                foreach (var host in azureRedisDomains)
                {
                    if (dnsEp.Host.EndsWith(host, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public override Task AfterConnectAsync(ConnectionMultiplexer muxer, Action<string> log)
            => AzureMaintenanceEvent.AddListenerAsync(muxer, log);

        /// <inheritdoc/>
        public override bool GetDefaultSsl(EndPointCollection endPoints)
        {
            foreach (var ep in endPoints)
            {
                switch (ep)
                {
                    case DnsEndPoint dns:
                        if (dns.Port == 6380)
                        {
                            return true;
                        }
                        break;
                    case IPEndPoint ip:
                        if (ip.Port == 6380)
                        {
                            return true;
                        }
                        break;
                }
            }
            return false;
        }
    }
}
