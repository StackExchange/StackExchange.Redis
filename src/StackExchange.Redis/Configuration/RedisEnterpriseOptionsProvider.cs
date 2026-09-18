using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Options provider for a self-managed Redis Enterprise deployment, selected explicitly.
    /// </summary>
    /// <remarks>
    /// Deliberately matches no endpoint. An on-premise cluster has whatever DNS its operator gave it, so there
    /// is nothing to recognize - which is exactly why this provider is nameable: it can be asked for as
    /// <c>defaults=enterprise</c> in a configuration string, or assigned to
    /// <see cref="ConfigurationOptions.Defaults"/> in code.
    /// <para>
    /// It is also the right choice for a hosted deployment reached somewhere its own provider cannot see it -
    /// behind private DNS, a CNAME, or a proxy - where the endpoint no longer looks like what it is.
    /// </para>
    /// </remarks>
    public class RedisEnterpriseOptionsProvider : DefaultOptionsProvider
    {
        /// <inheritdoc/>
        public override string Name => "enterprise";

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
        /// <see cref="MaintenanceNotificationMode.Auto"/> rather than
        /// <see cref="MaintenanceNotificationMode.Enabled"/>: a cluster that has not been updated yet, or has
        /// the feature switched off, must keep working. Choose <c>Enabled</c> explicitly if you would rather a
        /// connection be refused than run without advance warning.
        /// </remarks>
        [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
        public override MaintenanceNotificationMode MaintenanceNotifications => MaintenanceNotificationMode.Auto;

        // Note: no GetDefaultSsl and no DefaultVersion override. Both are deployment choices here rather than
        // properties of the product - TLS is configured per database, and the version is whatever was
        // installed - so guessing either would be worse than leaving the library's defaults in place.
    }
}
