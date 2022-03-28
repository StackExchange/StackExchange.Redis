using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// A defaults providers for <see cref="ConfigurationOptions"/>.
    /// This providers defaults not explicitly specified and is present to be inherited by environments that want to provide
    /// better defaults for their use case, e.g. in a single wrapper library used many places.
    /// </summary>
    /// <remarks>
    /// Why not just have a default <see cref="ConfigurationOptions"/> instance? Good question!
    /// Since we null coalesce down to the defaults, there's an inherent pit-of-failure with that approach of <see cref="StackOverflowException"/>.
    /// If you forget anything or if someone creates a provider nulling these out...kaboom.
    /// </remarks>
    public class DefaultOptionsProvider
    {
        /// <summary>
        /// The known providers to match against (built into the library) - the default set.
        /// If none of these match, <see cref="DefaultOptionsProvider"/> is used.
        /// </summary>
        private static readonly List<DefaultOptionsProvider> BuiltInProviders = new()
        {
            new AzureOptionsProvider(),
        };

        /// <summary>
        /// The current list of providers to match (potentially modified from defaults via <see cref="AddProvider(DefaultOptionsProvider)"/>.
        /// </summary>
        private static LinkedList<DefaultOptionsProvider> KnownProviders { get; set; } = new (BuiltInProviders);

        /// <summary>
        /// Adds a provider to match endpoints against. The last provider added has the highest priority.
        /// If you want your provider to match everything, implement <see cref="IsMatch(EndPoint)"/> as <c>return true;</c>.
        /// </summary>
        /// <param name="provider">The provider to add.</param>
        public static void AddProvider(DefaultOptionsProvider provider)
        {
            var newList = new LinkedList<DefaultOptionsProvider>(KnownProviders);
            newList.AddFirst(provider);
            KnownProviders = newList;
        }

        /// <summary>
        /// Whether this options provider matches a given endpoint, for automatically selecting a provider based on what's being connected to.
        /// </summary>
        public virtual bool IsMatch(EndPoint endpoint) => false;

        /// <summary>
        /// Gets a provider for the given endpoints, falling back to <see cref="DefaultOptionsProvider"/> if nothing more specific is found.
        /// </summary>
        internal static Func<EndPointCollection, DefaultOptionsProvider> GetForEndpoints { get; } = (endpoints) =>
        {
            foreach (var provider in KnownProviders)
            {
                foreach (var endpoint in endpoints)
                {
                    if (provider.IsMatch(endpoint))
                    {
                        return provider;
                    }
                }
            }

            return new DefaultOptionsProvider();
        };

        /// <summary>
        /// Gets or sets whether connect/configuration timeouts should be explicitly notified via a TimeoutException.
        /// </summary>
        public virtual bool AbortOnConnectFail => true;

        /// <summary>
        /// Indicates whether admin operations should be allowed.
        /// </summary>
        public virtual bool AllowAdmin => false;

        /// <summary>
        /// The backlog policy to be used for commands when a connection is unhealthy.
        /// </summary>
        public virtual BacklogPolicy BacklogPolicy => BacklogPolicy.Default;

        /// <summary>
        /// A Boolean value that specifies whether the certificate revocation list is checked during authentication.
        /// </summary>
        public virtual bool CheckCertificateRevocation => true;

        /// <summary>
        /// The number of times to repeat the initial connect cycle if no servers respond promptly.
        /// </summary>
        public virtual int ConnectRetry => 3;

        /// <summary>
        /// Specifies the time that should be allowed for connection.
        /// Falls back to Max(5000, SyncTimeout) if null.
        /// </summary>
        public virtual TimeSpan? ConnectTimeout => null;

        /// <summary>
        /// The command-map associated with this configuration.
        /// </summary>
        public virtual CommandMap? CommandMap => null;

        /// <summary>
        /// Channel to use for broadcasting and listening for configuration change notification.
        /// </summary>
        public virtual string ConfigurationChannel => "__Booksleeve_MasterChanged";

        /// <summary>
        /// The server version to assume.
        /// </summary>
        public virtual Version DefaultVersion => RedisFeatures.v3_0_0;

        /// <summary>
        /// Should exceptions include identifiable details? (key names, additional .Data annotations)
        /// </summary>
        public virtual bool IncludeDetailInExceptions => true;

        /// <summary>
        /// Should exceptions include performance counter details?
        /// </summary>
        /// <remarks>
        /// CPU usage, etc - note that this can be problematic on some platforms.
        /// </remarks>
        public virtual bool IncludePerformanceCountersInExceptions => false;

        /// <summary>
        /// Specifies the time interval at which connections should be pinged to ensure validity.
        /// </summary>
        public virtual TimeSpan KeepAliveInterval => TimeSpan.FromSeconds(60);

        /// <summary>
        /// Type of proxy to use (if any); for example <see cref="Proxy.Twemproxy"/>.
        /// </summary>
        public virtual Proxy Proxy => Proxy.None;

        /// <summary>
        /// The retry policy to be used for connection reconnects.
        /// </summary>
        public virtual IReconnectRetryPolicy? ReconnectRetryPolicy => null;

        /// <summary>
        /// Indicates whether endpoints should be resolved via DNS before connecting.
        /// If enabled the ConnectionMultiplexer will not re-resolve DNS when attempting to re-connect after a connection failure.
        /// </summary>
        public virtual bool ResolveDns => false;

        /// <summary>
        /// Specifies the time that the system should allow for synchronous operations.
        /// </summary>
        public virtual TimeSpan SyncTimeout => TimeSpan.FromSeconds(5);

        /// <summary>
        /// Tie-breaker used to choose between primaries (must match the endpoint exactly).
        /// </summary>
        public virtual string TieBreaker => "__Booksleeve_TieBreak";

        /// <summary>
        /// Check configuration every n interval.
        /// </summary>
        public virtual TimeSpan ConfigCheckInterval => TimeSpan.FromMinutes(1);

        // We memoize this to reduce cost on re-access
        private string? defaultClientName;
        /// <summary>
        /// The default client name for a connection, with the library version appended.
        /// </summary>
        public string ClientName => defaultClientName ??= GetDefaultClientName();

        /// <summary>
        /// Gets the default client name for a connection.
        /// </summary>
        protected virtual string GetDefaultClientName() =>
            (TryGetAzureRoleInstanceIdNoThrow()
             ?? ComputerName
             ?? "StackExchange.Redis") + "(SE.Redis-v" + LibraryVersion + ")";

        /// <summary>
        /// String version of the StackExchange.Redis library, for use in any options.
        /// </summary>
        protected static string LibraryVersion => Utils.GetLibVersion();

        /// <summary>
        /// Name of the machine we're running on, for use in any options.
        /// </summary>
        protected static string ComputerName => Environment.MachineName ?? Environment.GetEnvironmentVariable("ComputerName") ?? "Unknown";

        /// <summary>
        /// Tries to get the RoleInstance Id if Microsoft.WindowsAzure.ServiceRuntime is loaded.
        /// In case of any failure, swallows the exception and returns null.
        /// </summary>
        /// <remarks>
        /// Azure, in the default provider? Yes, to maintain existing compatibility/convenience.
        /// Source !=  destination here.
        /// </remarks>
        internal static string? TryGetAzureRoleInstanceIdNoThrow()
        {
            string? roleInstanceId;
            try
            {
                Assembly? asm = null;
                foreach (var asmb in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asmb.GetName()?.Name?.Equals("Microsoft.WindowsAzure.ServiceRuntime") == true)
                    {
                        asm = asmb;
                        break;
                    }
                }
                if (asm == null)
                    return null;

                var type = asm.GetType("Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment");

                // https://msdn.microsoft.com/en-us/library/microsoft.windowsazure.serviceruntime.roleenvironment.isavailable.aspx
                if (type?.GetProperty("IsAvailable") is not PropertyInfo isAvailableProp
                    || isAvailableProp.GetValue(null, null) is not bool isAvailableVal
                    || !isAvailableVal)
                {
                    return null;
                }

                var currentRoleInstanceProp = type.GetProperty("CurrentRoleInstance");
                var currentRoleInstanceId = currentRoleInstanceProp?.GetValue(null, null);
                roleInstanceId = currentRoleInstanceId?.GetType().GetProperty("Id")?.GetValue(currentRoleInstanceId, null)?.ToString();

                if (roleInstanceId.IsNullOrEmpty())
                {
                    roleInstanceId = null;
                }
            }
            catch (Exception)
            {
                //silently ignores the exception
                roleInstanceId = null;
            }
            return roleInstanceId;
        }

        /// <summary>
        /// The action to perform, if any, immediately after an initial connection completes.
        /// </summary>
        /// <param name="multiplexer">The multiplexer that just connected.</param>
        /// <param name="log">The logger for the connection, to emit to the connection output log.</param>
        public virtual Task AfterConnectAsync(ConnectionMultiplexer multiplexer, Action<string> log) => Task.CompletedTask;

        /// <summary>
        /// Gets the default SSL "enabled or not" based on a set of endpoints.
        /// Note: this setting then applies for *all* endpoints.
        /// </summary>
        /// <param name="endPoints">The configured endpoints to determine SSL usage from (e.g. from the port).</param>
        /// <returns>Whether to enable SSL for connections (unless explicitly overridden in a direct <see cref="ConfigurationOptions.Ssl"/> set).</returns>
        public virtual bool GetDefaultSsl(EndPointCollection endPoints) => false;

        /// <summary>
        /// Gets the SSL Host to check for when connecting to endpoints (customizable in case of internal certificate shenanigans.
        /// </summary>
        /// <param name="endPoints">The configured endpoints to determine SSL host from (e.g. from the port).</param>
        /// <returns>The common host, if any, detected from the endpoint collection.</returns>
        public virtual string? GetSslHostFromEndpoints(EndPointCollection endPoints)
        {
            string? commonHost = null;
            foreach (var endpoint in endPoints)
            {
                if (endpoint is DnsEndPoint dnsEndpoint)
                {
                    commonHost ??= dnsEndpoint.Host;
                    // Mismatch detected, no assumptions.
                    if (dnsEndpoint.Host != commonHost)
                    {
                        return null;
                    }
                }
            }
            return commonHost;
        }
    }
}
