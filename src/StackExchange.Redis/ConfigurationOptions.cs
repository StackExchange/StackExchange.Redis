using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Configuration;

namespace StackExchange.Redis
{
    /// <summary>
    /// The options relevant to a set of redis connections.
    /// </summary>
    /// <remarks>
    /// Some options are not observed by a <see cref="ConnectionMultiplexer"/> after initial creation:
    /// <list type="bullet">
    ///     <item><see cref="CommandMap"/></item>
    ///     <item><see cref="ConfigurationChannel"/></item>
    ///     <item><see cref="EndPoints"/></item>
    ///     <item><see cref="SocketManager"/></item>
    /// </list>
    /// </remarks>
    public sealed class ConfigurationOptions : ICloneable
    {
        private static class OptionKeys
        {
            public static int ParseInt32(string key, string value, int minValue = int.MinValue, int maxValue = int.MaxValue)
            {
                if (!Format.TryParseInt32(value, out int tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires an integer value; the value '{value}' is not recognised.");
                if (tmp < minValue) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' has a minimum value of '{minValue}'; the value '{tmp}' is not permitted.");
                if (tmp > maxValue) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' has a maximum value of '{maxValue}'; the value '{tmp}' is not permitted.");
                return tmp;
            }

            internal static bool ParseBoolean(string key, string value)
            {
                if (!Format.TryParseBoolean(value, out bool tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a boolean value; the value '{value}' is not recognised.");
                return tmp;
            }

            internal static Version ParseVersion(string key, string value)
            {
                if (!System.Version.TryParse(value, out Version? tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a version value; the value '{value}' is not recognised.");
                return tmp;
            }

            internal static Proxy ParseProxy(string key, string value)
            {
                if (!Enum.TryParse(value, true, out Proxy tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a proxy value; the value '{value}' is not recognised.");
                return tmp;
            }

            internal static SslProtocols ParseSslProtocols(string key, string? value)
            {
                //Flags expect commas as separators, but we need to use '|' since commas are already used in the connection string to mean something else
                value = value?.Replace("|", ",");

                if (!Enum.TryParse(value, true, out SslProtocols tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires an SslProtocol value (multiple values separated by '|'); the value '{value}' is not recognised.");

                return tmp;
            }

            internal static void Unknown(string key) =>
                throw new ArgumentException($"Keyword '{key}' is not supported.", key);

            internal const string
                AbortOnConnectFail = "abortConnect",
                AllowAdmin = "allowAdmin",
                AsyncTimeout = "asyncTimeout",
                ChannelPrefix = "channelPrefix",
                ConfigChannel = "configChannel",
                ConfigCheckSeconds = "configCheckSeconds",
                ConnectRetry = "connectRetry",
                ConnectTimeout = "connectTimeout",
                DefaultDatabase = "defaultDatabase",
                HighPrioritySocketThreads = "highPriorityThreads",
                KeepAlive = "keepAlive",
                ClientName = "name",
                User = "user",
                Password = "password",
                PreserveAsyncOrder = "preserveAsyncOrder",
                Proxy = "proxy",
                ResolveDns = "resolveDns",
                ResponseTimeout = "responseTimeout",
                ServiceName = "serviceName",
                Ssl = "ssl",
                SslHost = "sslHost",
                SslProtocols = "sslProtocols",
                SyncTimeout = "syncTimeout",
                TieBreaker = "tiebreaker",
                Version = "version",
                WriteBuffer = "writeBuffer",
                CheckCertificateRevocation = "checkCertificateRevocation";

            private static readonly Dictionary<string, string> normalizedOptions = new[]
            {
                AbortOnConnectFail,
                AllowAdmin,
                AsyncTimeout,
                ChannelPrefix,
                ClientName,
                ConfigChannel,
                ConfigCheckSeconds,
                ConnectRetry,
                ConnectTimeout,
                DefaultDatabase,
                HighPrioritySocketThreads,
                KeepAlive,
                User,
                Password,
                PreserveAsyncOrder,
                Proxy,
                ResolveDns,
                ServiceName,
                Ssl,
                SslHost,
                SslProtocols,
                SyncTimeout,
                TieBreaker,
                Version,
                WriteBuffer,
                CheckCertificateRevocation
            }.ToDictionary(x => x, StringComparer.OrdinalIgnoreCase);

            public static string TryNormalize(string value)
            {
                if (value != null && normalizedOptions.TryGetValue(value, out string? tmp))
                {
                    return tmp ?? "";
                }
                return value ?? "";
            }
        }

        private DefaultOptionsProvider? defaultOptions;

        private bool? allowAdmin, abortOnConnectFail, resolveDns, ssl, checkCertificateRevocation,
                      includeDetailInExceptions, includePerformanceCountersInExceptions;

        private string? tieBreaker, sslHost, configChannel;

        private CommandMap? commandMap;

        private Version? defaultVersion;

        private int? keepAlive, asyncTimeout, syncTimeout, connectTimeout, responseTimeout, connectRetry, configCheckSeconds;

        private Proxy? proxy;

        private IReconnectRetryPolicy? reconnectRetryPolicy;

        private BacklogPolicy? backlogPolicy;

        /// <summary>
        /// A LocalCertificateSelectionCallback delegate responsible for selecting the certificate used for authentication; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event LocalCertificateSelectionCallback? CertificateSelection;

        /// <summary>
        /// A RemoteCertificateValidationCallback delegate responsible for validating the certificate supplied by the remote party; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event RemoteCertificateValidationCallback? CertificateValidation;

        /// <summary>
        /// The default (not explicitly configured) options for this connection, fetched based on our parsed endpoints.
        /// </summary>
        public DefaultOptionsProvider Defaults
        {
            get => defaultOptions ??= DefaultOptionsProvider.GetForEndpoints(EndPoints);
            set => defaultOptions = value;
        }

        /// <summary>
        /// Allows modification of a <see cref="Socket"/> between creation and connection.
        /// Passed in is the endpoint we're connecting to, which type of connection it is, and the socket itself.
        /// For example, a specific local IP endpoint could be bound, linger time altered, etc.
        /// </summary>
        public Action<EndPoint, ConnectionType, Socket>? BeforeSocketConnect { get; set; }

        internal Func<ConnectionMultiplexer, Action<string>, Task> AfterConnectAsync => Defaults.AfterConnectAsync;

        /// <summary>
        /// Gets or sets whether connect/configuration timeouts should be explicitly notified via a TimeoutException.
        /// </summary>
        public bool AbortOnConnectFail
        {
            get => abortOnConnectFail ?? Defaults.AbortOnConnectFail;
            set => abortOnConnectFail = value;
        }

        /// <summary>
        /// Indicates whether admin operations should be allowed.
        /// </summary>
        public bool AllowAdmin
        {
            get => allowAdmin ?? Defaults.AllowAdmin;
            set => allowAdmin = value;
        }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for asynchronous operations (defaults to SyncTimeout).
        /// </summary>
        public int AsyncTimeout
        {
            get => asyncTimeout ?? SyncTimeout;
            set => asyncTimeout = value;
        }

        /// <summary>
        /// Indicates whether the connection should be encrypted
        /// </summary>
        [Obsolete("Please use .Ssl instead of .UseSsl, will be removed in 3.0."),
         Browsable(false),
         EditorBrowsable(EditorBrowsableState.Never)]
        public bool UseSsl
        {
            get => Ssl;
            set => Ssl = value;
        }

        /// <summary>
        /// Automatically encodes and decodes channels.
        /// </summary>
        public RedisChannel ChannelPrefix { get; set; }

        /// <summary>
        /// A Boolean value that specifies whether the certificate revocation list is checked during authentication.
        /// </summary>
        public bool CheckCertificateRevocation
        {
            get => checkCertificateRevocation ?? Defaults.CheckCertificateRevocation;
            set => checkCertificateRevocation = value;
        }

        /// <summary>
        /// Create a certificate validation check that checks against the supplied issuer even if not known by the machine.
        /// </summary>
        /// <param name="issuerCertificatePath">The file system path to find the certificate at.</param>
        public void TrustIssuer(string issuerCertificatePath) => CertificateValidationCallback = TrustIssuerCallback(issuerCertificatePath);

        /// <summary>
        /// Create a certificate validation check that checks against the supplied issuer even if not known by the machine.
        /// </summary>
        /// <param name="issuer">The issuer to trust.</param>
        public void TrustIssuer(X509Certificate2 issuer) => CertificateValidationCallback = TrustIssuerCallback(issuer);

        internal static RemoteCertificateValidationCallback TrustIssuerCallback(string issuerCertificatePath)
            => TrustIssuerCallback(new X509Certificate2(issuerCertificatePath));
        private static RemoteCertificateValidationCallback TrustIssuerCallback(X509Certificate2 issuer)
        {
            if (issuer == null) throw new ArgumentNullException(nameof(issuer));

            return (object _, X509Certificate? certificate, X509Chain? __, SslPolicyErrors sslPolicyError)
                => sslPolicyError == SslPolicyErrors.RemoteCertificateChainErrors
                    && certificate is X509Certificate2 v2
                    && CheckTrustedIssuer(v2, issuer);
        }

        private static bool CheckTrustedIssuer(X509Certificate2 certificateToValidate, X509Certificate2 authority)
        {
            // reference: https://stackoverflow.com/questions/6497040/how-do-i-validate-that-a-certificate-was-created-by-a-particular-certification-a
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.VerificationTime = DateTime.Now;
            chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 0);

            chain.ChainPolicy.ExtraStore.Add(authority);
            return chain.Build(certificateToValidate);
        }

        /// <summary>
        /// The client name to use for all connections.
        /// </summary>
        public string? ClientName { get; set; }

        /// <summary>
        /// The number of times to repeat the initial connect cycle if no servers respond promptly.
        /// </summary>
        public int ConnectRetry
        {
            get => connectRetry ?? Defaults.ConnectRetry;
            set => connectRetry = value;
        }

        /// <summary>
        /// The command-map associated with this configuration.
        /// </summary>
        /// <remarks>
        /// This is memoized when a <see cref="ConnectionMultiplexer"/> connects.
        /// Modifying it afterwards will have no effect on already-created multiplexers.
        /// </remarks>
        public CommandMap CommandMap
        {
            get => commandMap ?? Defaults.CommandMap ?? Proxy switch
            {
                Proxy.Twemproxy => CommandMap.Twemproxy,
                Proxy.Envoyproxy => CommandMap.Envoyproxy,
                _ => CommandMap.Default,
            };
            set => commandMap = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets the command map for a given server type, since some supersede settings when connecting.
        /// </summary>
        internal CommandMap GetCommandMap(ServerType? serverType) => serverType switch
        {
            ServerType.Sentinel => CommandMap.Sentinel,
            _ => CommandMap,
        };

        /// <summary>
        /// Channel to use for broadcasting and listening for configuration change notification.
        /// </summary>
        /// <remarks>
        /// This is memoized when a <see cref="ConnectionMultiplexer"/> connects.
        /// Modifying it afterwards will have no effect on already-created multiplexers.
        /// </remarks>
        public string ConfigurationChannel
        {
            get => configChannel ?? Defaults.ConfigurationChannel;
            set => configChannel = value;
        }

        /// <summary>
        /// Specifies the time in milliseconds that should be allowed for connection (defaults to 5 seconds unless SyncTimeout is higher).
        /// </summary>
        public int ConnectTimeout
        {
            get => connectTimeout ?? ((int?)Defaults.ConnectTimeout?.TotalMilliseconds) ?? Math.Max(5000, SyncTimeout);
            set => connectTimeout = value;
        }

        /// <summary>
        /// Specifies the default database to be used when calling <see cref="ConnectionMultiplexer.GetDatabase(int, object)"/> without any parameters.
        /// </summary>
        public int? DefaultDatabase { get; set; }

        /// <summary>
        /// The server version to assume.
        /// </summary>
        public Version DefaultVersion
        {
            get => defaultVersion ?? Defaults.DefaultVersion;
            set => defaultVersion = value;
        }

        /// <summary>
        /// The endpoints defined for this configuration.
        /// </summary>
        /// <remarks>
        /// This is memoized when a <see cref="ConnectionMultiplexer"/> connects.
        /// Modifying it afterwards will have no effect on already-created multiplexers.
        /// </remarks>
        public EndPointCollection EndPoints { get; init; } = new EndPointCollection();

        /// <summary>
        /// Use ThreadPriority.AboveNormal for SocketManager reader and writer threads (true by default).
        /// If <see langword="false"/>, <see cref="ThreadPriority.Normal"/> will be used.
        /// </summary>
        [Obsolete($"This setting no longer has any effect, please use {nameof(SocketManager.SocketManagerOptions)}.{nameof(SocketManager.SocketManagerOptions.UseHighPrioritySocketThreads)} instead - this setting will be removed in 3.0.")]
        public bool HighPrioritySocketThreads
        {
            get => false;
            set { }
        }

        /// <summary>
        /// Should exceptions include identifiable details? (key names, additional .Data annotations)
        /// </summary>
        public bool IncludeDetailInExceptions
        {
            get => includeDetailInExceptions ?? Defaults.IncludeDetailInExceptions;
            set => includeDetailInExceptions = value;
        }

        /// <summary>
        /// Should exceptions include performance counter details?
        /// </summary>
        /// <remarks>
        /// CPU usage, etc - note that this can be problematic on some platforms.
        /// </remarks>
        public bool IncludePerformanceCountersInExceptions
        {
            get => includePerformanceCountersInExceptions ?? Defaults.IncludePerformanceCountersInExceptions;
            set => includePerformanceCountersInExceptions = value;
        }

        /// <summary>
        /// Specifies the time in seconds at which connections should be pinged to ensure validity.
        /// -1 Defaults to 60 Seconds
        /// </summary>
        public int KeepAlive
        {
            get => keepAlive ?? (int)Defaults.KeepAliveInterval.TotalSeconds;
            set => keepAlive = value;
        }

        /// <summary>
        /// The user to use to authenticate with the server.
        /// </summary>
        public string? User { get; set; }

        /// <summary>
        /// The password to use to authenticate with the server.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Specifies whether asynchronous operations should be invoked in a way that guarantees their original delivery order.
        /// </summary>
        [Obsolete("Not supported; if you require ordered pub/sub, please see " + nameof(ChannelMessageQueue) + " - this will be removed in 3.0.", false)]
        public bool PreserveAsyncOrder
        {
            get => false;
            set { }
        }

        /// <summary>
        /// Type of proxy to use (if any); for example <see cref="Proxy.Twemproxy"/>.
        /// </summary>
        public Proxy Proxy
        {
            get => proxy ?? Defaults.Proxy;
            set => proxy = value;
        }

        /// <summary>
        /// The retry policy to be used for connection reconnects.
        /// </summary>
        public IReconnectRetryPolicy ReconnectRetryPolicy
        {
            get => reconnectRetryPolicy ??= Defaults.ReconnectRetryPolicy ?? new ExponentialRetry(ConnectTimeout / 2);
            set => reconnectRetryPolicy = value;
        }

        /// <summary>
        /// The backlog policy to be used for commands when a connection is unhealthy.
        /// </summary>
        public BacklogPolicy BacklogPolicy
        {
            get => backlogPolicy ?? Defaults.BacklogPolicy;
            set => backlogPolicy = value;
        }

        /// <summary>
        /// Indicates whether endpoints should be resolved via DNS before connecting.
        /// If enabled the ConnectionMultiplexer will not re-resolve DNS when attempting to re-connect after a connection failure.
        /// </summary>
        public bool ResolveDns
        {
            get => resolveDns ?? Defaults.ResolveDns;
            set => resolveDns = value;
        }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for responses before concluding that the socket is unhealthy.
        /// </summary>
        [Obsolete("This setting no longer has any effect, and should not be used - will be removed in 3.0.")]
        public int ResponseTimeout
        {
            get => 0;
            set { }
        }

        /// <summary>
        /// The service name used to resolve a service via sentinel.
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// Gets or sets the SocketManager instance to be used with these options.
        /// If this is null a shared cross-multiplexer <see cref="SocketManager"/> is used.
        /// </summary>
        /// <remarks>
        /// This is only used when a <see cref="ConnectionMultiplexer"/> is created.
        /// Modifying it afterwards will have no effect on already-created multiplexers.
        /// </remarks>
        public SocketManager? SocketManager { get; set; }

        /// <summary>
        /// Indicates whether the connection should be encrypted.
        /// </summary>
        public bool Ssl
        {
            get => ssl ?? Defaults.GetDefaultSsl(EndPoints);
            set => ssl = value;
        }

        /// <summary>
        /// The target-host to use when validating SSL certificate; setting a value here enables SSL mode.
        /// </summary>
        public string? SslHost
        {
            get => sslHost ?? Defaults.GetSslHostFromEndpoints(EndPoints);
            set => sslHost = value;
        }

        /// <summary>
        /// Configures which SSL/TLS protocols should be allowed.  If not set, defaults are chosen by the .NET framework.
        /// </summary>
        public SslProtocols? SslProtocols { get; set; }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for synchronous operations (defaults to 5 seconds).
        /// </summary>
        public int SyncTimeout
        {
            get => syncTimeout ?? (int)Defaults.SyncTimeout.TotalMilliseconds;
            set => syncTimeout = value;
        }

        /// <summary>
        /// Tie-breaker used to choose between primaries (must match the endpoint exactly).
        /// </summary>
        public string TieBreaker
        {
            get => tieBreaker ?? Defaults.TieBreaker;
            set => tieBreaker = value;
        }

        /// <summary>
        /// The size of the output buffer to use.
        /// </summary>
        [Obsolete("This setting no longer has any effect, and should not be used - will be removed in 3.0.")]
        public int WriteBuffer
        {
            get => 0;
            set { }
        }

        internal LocalCertificateSelectionCallback? CertificateSelectionCallback
        {
            get => CertificateSelection;
            private set => CertificateSelection = value;
        }

        // these just rip out the underlying handlers, bypassing the event accessors - needed when creating the SSL stream
        internal RemoteCertificateValidationCallback? CertificateValidationCallback
        {
            get => CertificateValidation;
            private set => CertificateValidation = value;
        }

        /// <summary>
        /// Check configuration every n seconds (every minute by default).
        /// </summary>
        public int ConfigCheckSeconds
        {
            get => configCheckSeconds ?? (int)Defaults.ConfigCheckInterval.TotalSeconds;
            set => configCheckSeconds = value;
        }

        /// <summary>
        /// Parse the configuration from a comma-delimited configuration string.
        /// </summary>
        /// <param name="configuration">The configuration string to parse.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="configuration"/> is empty.</exception>
        public static ConfigurationOptions Parse(string configuration) => Parse(configuration, false);

        /// <summary>
        /// Parse the configuration from a comma-delimited configuration string.
        /// </summary>
        /// <param name="configuration">The configuration string to parse.</param>
        /// <param name="ignoreUnknown">Whether to ignore unknown elements in <paramref name="configuration"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="configuration"/> is empty.</exception>
        public static ConfigurationOptions Parse(string configuration, bool ignoreUnknown) =>
            new ConfigurationOptions().DoParse(configuration, ignoreUnknown);

        /// <summary>
        /// Create a copy of the configuration.
        /// </summary>
        public ConfigurationOptions Clone() => new ConfigurationOptions
        {
            defaultOptions = defaultOptions,
            ClientName = ClientName,
            ServiceName = ServiceName,
            keepAlive = keepAlive,
            syncTimeout = syncTimeout,
            asyncTimeout = asyncTimeout,
            allowAdmin = allowAdmin,
            defaultVersion = defaultVersion,
            connectTimeout = connectTimeout,
            User = User,
            Password = Password,
            tieBreaker = tieBreaker,
            ssl = ssl,
            sslHost = sslHost,
            configChannel = configChannel,
            abortOnConnectFail = abortOnConnectFail,
            resolveDns = resolveDns,
            proxy = proxy,
            commandMap = commandMap,
            CertificateValidationCallback = CertificateValidationCallback,
            CertificateSelectionCallback = CertificateSelectionCallback,
            ChannelPrefix = ChannelPrefix.Clone(),
            SocketManager = SocketManager,
            connectRetry = connectRetry,
            configCheckSeconds = configCheckSeconds,
            responseTimeout = responseTimeout,
            DefaultDatabase = DefaultDatabase,
            reconnectRetryPolicy = reconnectRetryPolicy,
            backlogPolicy = backlogPolicy,
            SslProtocols = SslProtocols,
            checkCertificateRevocation = checkCertificateRevocation,
            BeforeSocketConnect = BeforeSocketConnect,
            EndPoints = EndPoints.Clone(),
        };

        /// <summary>
        /// Apply settings to configure this instance of <see cref="ConfigurationOptions"/>, e.g. for a specific scenario.
        /// </summary>
        /// <param name="configure">An action that will update the properties of this <see cref="ConfigurationOptions"/> instance.</param>
        /// <returns>This <see cref="ConfigurationOptions"/> instance, with any changes <paramref name="configure"/> made.</returns>
        public ConfigurationOptions Apply(Action<ConfigurationOptions> configure)
        {
            configure?.Invoke(this);
            return this;
        }

        /// <summary>
        /// Resolve the default port for any endpoints that did not have a port explicitly specified.
        /// </summary>
        public void SetDefaultPorts() => EndPoints.SetDefaultPorts(ServerType.Standalone, ssl: Ssl);

        internal bool IsSentinel => !string.IsNullOrEmpty(ServiceName);

        /// <summary>
        /// Gets a tie breaker if we both have one set, and should be using one.
        /// </summary>
        internal bool TryGetTieBreaker(out RedisKey tieBreaker)
        {
            var key = TieBreaker;
            if (!IsSentinel && !string.IsNullOrWhiteSpace(key))
            {
                tieBreaker = key;
                return true;
            }
            tieBreaker = default;
            return false;
        }

        /// <summary>
        /// Returns the effective configuration string for this configuration, including Redis credentials.
        /// </summary>
        /// <remarks>
        /// Includes password to allow generation of configuration strings used for connecting multiplexer.
        /// </remarks>
        public override string ToString() => ToString(includePassword: true);

        /// <summary>
        /// Returns the effective configuration string for this configuration
        /// with the option to include or exclude the password from the string.
        /// </summary>
        /// <param name="includePassword">Whether to include the password.</param>
        public string ToString(bool includePassword)
        {
            var sb = new StringBuilder();
            foreach (var endpoint in EndPoints)
            {
                Append(sb, Format.ToString(endpoint));
            }
            Append(sb, OptionKeys.ClientName, ClientName);
            Append(sb, OptionKeys.ServiceName, ServiceName);
            Append(sb, OptionKeys.KeepAlive, keepAlive);
            Append(sb, OptionKeys.SyncTimeout, syncTimeout);
            Append(sb, OptionKeys.AsyncTimeout, asyncTimeout);
            Append(sb, OptionKeys.AllowAdmin, allowAdmin);
            Append(sb, OptionKeys.Version, defaultVersion);
            Append(sb, OptionKeys.ConnectTimeout, connectTimeout);
            Append(sb, OptionKeys.User, User);
            Append(sb, OptionKeys.Password, (includePassword || string.IsNullOrEmpty(Password)) ? Password : "*****");
            Append(sb, OptionKeys.TieBreaker, tieBreaker);
            Append(sb, OptionKeys.Ssl, ssl);
            Append(sb, OptionKeys.SslProtocols, SslProtocols?.ToString().Replace(',', '|'));
            Append(sb, OptionKeys.CheckCertificateRevocation, checkCertificateRevocation);
            Append(sb, OptionKeys.SslHost, sslHost);
            Append(sb, OptionKeys.ConfigChannel, configChannel);
            Append(sb, OptionKeys.AbortOnConnectFail, abortOnConnectFail);
            Append(sb, OptionKeys.ResolveDns, resolveDns);
            Append(sb, OptionKeys.ChannelPrefix, (string?)ChannelPrefix);
            Append(sb, OptionKeys.ConnectRetry, connectRetry);
            Append(sb, OptionKeys.Proxy, proxy);
            Append(sb, OptionKeys.ConfigCheckSeconds, configCheckSeconds);
            Append(sb, OptionKeys.ResponseTimeout, responseTimeout);
            Append(sb, OptionKeys.DefaultDatabase, DefaultDatabase);
            commandMap?.AppendDeltas(sb);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, object value)
        {
            if (value == null) return;
            string s = Format.ToString(value);
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (sb.Length != 0) sb.Append(',');
                sb.Append(s);
            }
        }

        private static void Append(StringBuilder sb, string prefix, object? value)
        {
            string? s = value?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (sb.Length != 0) sb.Append(',');
                if (!string.IsNullOrEmpty(prefix))
                {
                    sb.Append(prefix).Append('=');
                }
                sb.Append(s);
            }
        }

        private void Clear()
        {
            ClientName = ServiceName = User = Password = tieBreaker = sslHost = configChannel = null;
            keepAlive = syncTimeout = asyncTimeout = connectTimeout = connectRetry = configCheckSeconds = DefaultDatabase = null;
            allowAdmin = abortOnConnectFail = resolveDns = ssl = null;
            SslProtocols = null;
            defaultVersion = null;
            EndPoints.Clear();
            commandMap = null;

            CertificateSelection = null;
            CertificateValidation = null;
            ChannelPrefix = default;
            SocketManager = null;
        }

        object ICloneable.Clone() => Clone();

        private ConfigurationOptions DoParse(string configuration, bool ignoreUnknown)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration))
            {
                throw new ArgumentException("is empty", nameof(configuration));
            }

            Clear();

            // break it down by commas
            var arr = configuration.Split(StringSplits.Comma);
            Dictionary<string, string?>? map = null;
            foreach (var paddedOption in arr)
            {
                var option = paddedOption.Trim();

                if (string.IsNullOrWhiteSpace(option)) continue;

                // check for special tokens
                int idx = option.IndexOf('=');
                if (idx > 0)
                {
                    var key = option.Substring(0, idx).Trim();
                    var value = option.Substring(idx + 1).Trim();

                    switch (OptionKeys.TryNormalize(key))
                    {
                        case OptionKeys.CheckCertificateRevocation:
                            CheckCertificateRevocation = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.SyncTimeout:
                            SyncTimeout = OptionKeys.ParseInt32(key, value, minValue: 1);
                            break;
                        case OptionKeys.AsyncTimeout:
                            AsyncTimeout = OptionKeys.ParseInt32(key, value, minValue: 1);
                            break;
                        case OptionKeys.AllowAdmin:
                            AllowAdmin = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.AbortOnConnectFail:
                            AbortOnConnectFail = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.ResolveDns:
                            ResolveDns = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.ServiceName:
                            ServiceName = value;
                            break;
                        case OptionKeys.ClientName:
                            ClientName = value;
                            break;
                        case OptionKeys.ChannelPrefix:
                            ChannelPrefix = value;
                            break;
                        case OptionKeys.ConfigChannel:
                            ConfigurationChannel = value;
                            break;
                        case OptionKeys.KeepAlive:
                            KeepAlive = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.ConnectTimeout:
                            ConnectTimeout = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.ConnectRetry:
                            ConnectRetry = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.ConfigCheckSeconds:
                            ConfigCheckSeconds = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.Version:
                            DefaultVersion = OptionKeys.ParseVersion(key, value);
                            break;
                        case OptionKeys.User:
                            User = value;
                            break;
                        case OptionKeys.Password:
                            Password = value;
                            break;
                        case OptionKeys.TieBreaker:
                            TieBreaker = value;
                            break;
                        case OptionKeys.Ssl:
                            Ssl = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.SslHost:
                            SslHost = value;
                            break;
                        case OptionKeys.Proxy:
                            Proxy = OptionKeys.ParseProxy(key, value);
                            break;
                        case OptionKeys.DefaultDatabase:
                            DefaultDatabase = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.SslProtocols:
                            SslProtocols = OptionKeys.ParseSslProtocols(key, value);
                            break;
                        // Deprecated options we ignore...
                        case OptionKeys.HighPrioritySocketThreads:
                        case OptionKeys.PreserveAsyncOrder:
                        case OptionKeys.ResponseTimeout:
                        case OptionKeys.WriteBuffer:
                            break;
                        default:
                            if (!string.IsNullOrEmpty(key) && key[0] == '$')
                            {
                                var cmdName = option.Substring(1, idx - 1);
                                if (Enum.TryParse(cmdName, true, out RedisCommand cmd))
                                {
                                    map ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                                    map[cmdName] = value;
                                }
                            }
                            else
                            {
                                if (!ignoreUnknown) OptionKeys.Unknown(key);
                            }
                            break;
                    }
                }
                else
                {
                    if (Format.TryParseEndPoint(option, out var ep) && !EndPoints.Contains(ep))
                    {
                        EndPoints.Add(ep);
                    }
                }
            }
            if (map != null && map.Count != 0)
            {
                CommandMap = CommandMap.Create(map);
            }
            return this;
        }
    }
}
