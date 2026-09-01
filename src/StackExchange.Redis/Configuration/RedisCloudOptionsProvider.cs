using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using RESPite;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Options provider for Redis Cloud environments.
    /// </summary>
    /// <remarks>
    /// Deliberately *not* a copy of <see cref="AzureManagedRedisOptionsProvider"/>, despite the deployments
    /// looking similar: AMR is TLS-only and has a 7.4 floor, and Redis Cloud is neither. See the individual
    /// members for what is shared and what is not.
    /// </remarks>
    public class RedisCloudOptionsProvider : DefaultOptionsProvider
    {
        // note the third subsumes the first (EndsWith), so it is kept for the documentation rather than the
        // logic - the narrower entry records what the common case actually looks like
        private static readonly string[] redisCloudDomains =
        [
            ".cloud.redislabs.com", // standard fully-managed endpoints (AWS, GCP, Azure BYOC)
            ".cloud.redis.io",      // newer routing scheme for managed instances
            ".redislabs.com",       // older subscriptions, addressed directly
        ];

        /// <inheritdoc/>
        public override string Name => "rediscloud";

        /// <inheritdoc/>
        public override bool IsMatch(EndPoint endpoint)
            => endpoint is DnsEndPoint dnsEp && IsHostInDomains(dnsEp.Host, redisCloudDomains);

        private static bool IsHostInDomains(string hostName, string[] domains)
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

        /// <summary>
        /// Allow connecting after startup, in the cases where the remote cache isn't ready or is overloaded.
        /// </summary>
        public override bool AbortOnConnectFail => false;

        /// <summary>
        /// Prefer RESP3, which the deployment supports and which maintenance notifications require.
        /// </summary>
        public override RedisProtocol? Protocol => RedisProtocol.Resp3;

        /// <summary>
        /// Disabled: the deployment is proxied, so the OSS configuration-broadcast channel conveys nothing.
        /// </summary>
        public override string ConfigurationChannel => "";

        /// <summary>
        /// Ask for maintenance notifications, tolerating a server that doesn't offer them.
        /// </summary>
        /// <remarks>
        /// This is the deployment family the feature exists for. <see cref="MaintenanceNotificationMode.Auto"/>
        /// rather than <see cref="MaintenanceNotificationMode.Enabled"/> because a database that has not been
        /// updated yet must keep working: the opt-in is then refused and the feature stays off, rather than the
        /// connection being rejected.
        /// </remarks>
        [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
        public override MaintenanceNotificationMode MaintenanceNotifications => MaintenanceNotificationMode.Auto;

        // Two things AzureManagedRedisOptionsProvider does that are deliberately *not* repeated here:
        //
        // - GetDefaultSsl => true. AMR is TLS-only, so assuming TLS there is safe. Redis Cloud enables TLS
        //   per database and plenty of databases are plaintext, so defaulting it on would fail their connect
        //   outright - a much worse outcome than not having guessed.
        // - DefaultVersion => 7.4. That is AMR's floor. Redis Cloud still offers older versions per database,
        //   and claiming a version we do not have unlocks commands the server will reject, so the base
        //   default stands.
    }
}
