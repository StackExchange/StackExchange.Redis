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
        /// Whether this options provider matches a given endpoint, for automatically selecting a provider based on what's being connected to.
        /// </summary>
        public virtual bool IsMatch(EndPoint endpoint) => false;

        /// <summary>
        /// Gets a provider for the given endpoints, falling back to <see cref="DefaultOptionsProvider"/> if nothing more specific is found.
        /// </summary>
        internal static Func<EndPointCollection, DefaultOptionsProvider> GetForEndpoints { get; } = (endpoints) =>
        {
            foreach (var endpoint in endpoints)
            {
                foreach (var provider in KnownProviders)
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
        /// Known providers to match - this is intentionally modifiable for expert users and wrapper libraries.
        /// </summary>
        public static List<DefaultOptionsProvider> KnownProviders { get; set; } = new()
        {
            new AzureOptionsProvider(),
            new DefaultOptionsProvider()
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
        public virtual CommandMap CommandMap => null;

        /// <summary>
        /// Channel to use for broadcasting and listening for configuration change notification.
        /// </summary>
        public virtual string ConfigurationChannel => "__Booksleeve_MasterChanged";

        /// <summary>
        /// The server version to assume.
        /// </summary>
        public virtual Version DefaultVersion => RedisFeatures.v3_0_0;

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
        public virtual IReconnectRetryPolicy ReconnectRetryPolicy => null;

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
        /// Tie-breaker used to choose between masters (must match the endpoint exactly).
        /// </summary>
        public virtual string TieBreaker => "__Booksleeve_TieBreak";

        /// <summary>
        /// Check configuration every n interval.
        /// </summary>
        public virtual TimeSpan ConfigCheckInterval => TimeSpan.FromMinutes(1);

        // Note: this is statically backed because it doesn't change.
        private static string defaultClientName;
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
        protected static string ComputerName => Environment.MachineName ?? Environment.GetEnvironmentVariable("ComputerName");

        /// <summary>
        /// Tries to get the RoleInstance Id if Microsoft.WindowsAzure.ServiceRuntime is loaded.
        /// In case of any failure, swallows the exception and returns null.
        /// </summary>
        /// <remarks>
        /// Azure, in the default provider? Yes, to maintain existing compatibility/convenience.
        /// Source !=  destination here.
        /// </remarks>
        internal static string TryGetAzureRoleInstanceIdNoThrow()
        {
            string roleInstanceId;
            try
            {
                Assembly asm = null;
                foreach (var asmb in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asmb.GetName().Name.Equals("Microsoft.WindowsAzure.ServiceRuntime"))
                    {
                        asm = asmb;
                        break;
                    }
                }
                if (asm == null)
                    return null;

                var type = asm.GetType("Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment");

                // https://msdn.microsoft.com/en-us/library/microsoft.windowsazure.serviceruntime.roleenvironment.isavailable.aspx
                if (!(bool)type.GetProperty("IsAvailable").GetValue(null, null))
                    return null;

                var currentRoleInstanceProp = type.GetProperty("CurrentRoleInstance");
                var currentRoleInstanceId = currentRoleInstanceProp.GetValue(null, null);
                roleInstanceId = currentRoleInstanceId.GetType().GetProperty("Id").GetValue(currentRoleInstanceId, null).ToString();

                if (string.IsNullOrEmpty(roleInstanceId))
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
    }
}
