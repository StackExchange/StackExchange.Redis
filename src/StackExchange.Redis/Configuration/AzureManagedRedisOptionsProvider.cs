using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Options provider for Azure Managed Redis environments.
    /// </summary>
    public class AzureManagedRedisOptionsProvider : DefaultOptionsProvider
    {
        /// <summary>
        /// Allow connecting after startup, in the cases where remote cache isn't ready or is overloaded.
        /// </summary>
        public override bool AbortOnConnectFail => false;

        /// <summary>
        /// The minimum version of Redis in Azure Managed Redis is 7.4, so use the widest set of available commands when connecting.
        /// </summary>
        public override Version DefaultVersion => RedisFeatures.v7_4_0;

        private static readonly string[] azureManagedRedisDomains =
        [
            ".redis.azure.net",
            ".redis.chinacloudapi.cn",
            ".redis.usgovcloudapi.net",
        ];

        /// <inheritdoc/>
        public override bool IsMatch(EndPoint endpoint)
        {
            if (endpoint is DnsEndPoint dnsEp && IsHostInDomains(dnsEp.Host, azureManagedRedisDomains))
            {
                return true;
            }

            return false;
        }

        private bool IsHostInDomains(string hostName, string[] domains)
        {
            foreach (var domain in domains)
            {
                if (hostName.EndsWith(domain, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public override Task AfterConnectAsync(ConnectionMultiplexer muxer, Action<string> log)
            => AzureMaintenanceEvent.AddListenerAsync(muxer, log);

        /// <inheritdoc/>
        public override bool GetDefaultSsl(EndPointCollection endPoints) => true;
    }
}
