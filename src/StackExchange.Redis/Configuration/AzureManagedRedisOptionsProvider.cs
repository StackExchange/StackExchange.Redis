using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using RESPite;

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
            ".redis.sovcloud-api.de",
            ".redis.sovcloud-api.fr",
            ".redisenterprise.cache.azure.net",
        ];

        /// <inheritdoc/>
        public override string Name => "amr";

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
            => Task.CompletedTask;

        /// <inheritdoc/>
        public override bool GetDefaultSsl(EndPointCollection endPoints) => true;

        /// <inheritdoc/>
        public override RedisProtocol? Protocol => RedisProtocol.Resp3; // prefer RESP3 on AMR

        /// <inheritdoc/>
        public override string ConfigurationChannel => ""; // disable on AMR

        /// <summary>
        /// Ask for maintenance notifications, tolerating a server that doesn't offer them.
        /// </summary>
        /// <remarks>
        /// Pre-emptive: AMR does not emit these yet, and support is being added concurrently with this
        /// client-side work. <see cref="MaintenanceNotificationMode.Auto"/> is what makes that safe - until
        /// the server side ships, the opt-in is refused and the feature stays off, and it then starts working
        /// without anybody needing to change a connection string. AMR also already prefers RESP3 here, which
        /// the feature requires.
        /// </remarks>
        [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
        public override MaintenanceNotificationMode MaintenanceNotifications => MaintenanceNotificationMode.Auto;
    }
}
