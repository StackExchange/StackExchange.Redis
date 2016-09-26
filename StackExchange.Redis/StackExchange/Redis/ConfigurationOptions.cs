using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{

    /// <summary>
    /// Specifies the proxy that is being used to communicate to redis
    /// </summary>
    public enum Proxy
    {
        /// <summary>
        /// Direct communication to the redis server(s)
        /// </summary>
        None,
        /// <summary>
        /// Communication via <a href="https://github.com/twitter/twemproxy">twemproxy</a>
        /// </summary>
        Twemproxy
    }

    /// <summary>
    /// The options relevant to a set of redis connections
    /// </summary>
    public sealed class ConfigurationOptions
#if !CORE_CLR
        : ICloneable
#endif
    {
        internal const string DefaultTieBreaker = "__Booksleeve_TieBreak", DefaultConfigurationChannel = "__Booksleeve_MasterChanged";

        private static class OptionKeys
        {
            public static int ParseInt32(string key, string value, int minValue = int.MinValue, int maxValue = int.MaxValue)
            {
                int tmp;
                if (!Format.TryParseInt32(value, out tmp)) throw new ArgumentOutOfRangeException("Keyword '" + key + "' requires an integer value");
                if (tmp < minValue) throw new ArgumentOutOfRangeException("Keyword '" + key + "' has a minimum value of " + minValue);
                if (tmp > maxValue) throw new ArgumentOutOfRangeException("Keyword '" + key + "' has a maximum value of " + maxValue);
                return tmp;
            }

            internal static bool ParseBoolean(string key, string value)
            {
                bool tmp;
                if (!Format.TryParseBoolean(value, out tmp)) throw new ArgumentOutOfRangeException("Keyword '" + key + "' requires a boolean value");
                return tmp;
            }
            internal static Version ParseVersion(string key, string value)
            {
                Version tmp;
                if (!System.Version.TryParse(value, out tmp)) throw new ArgumentOutOfRangeException("Keyword '" + key + "' requires a version value");
                return tmp;
            }
            internal static Proxy ParseProxy(string key, string value)
            {
                Proxy tmp;
                if (!Enum.TryParse(value, true, out tmp)) throw new ArgumentOutOfRangeException("Keyword '" + key + "' requires a proxy value");
                return tmp;
            }

            internal static void Unknown(string key)
            {
                throw new ArgumentException("Keyword '" + key + "' is not supported");
            }

            internal const string AllowAdmin = "allowAdmin", SyncTimeout = "syncTimeout",
                                ServiceName = "serviceName", ClientName = "name", KeepAlive = "keepAlive",
                        Version = "version", ConnectTimeout = "connectTimeout", Password = "password",
                        TieBreaker = "tiebreaker", WriteBuffer = "writeBuffer", Ssl = "ssl", SslHost = "sslHost", HighPrioritySocketThreads = "highPriorityThreads",
                        ConfigChannel = "configChannel", AbortOnConnectFail = "abortConnect", ResolveDns = "resolveDns",
                        ChannelPrefix = "channelPrefix", Proxy = "proxy", ConnectRetry = "connectRetry",
                        ConfigCheckSeconds = "configCheckSeconds", ResponseTimeout = "responseTimeout", DefaultDatabase = "defaultDatabase";
            private static readonly Dictionary<string, string> normalizedOptions = new[]
            {
                AllowAdmin, SyncTimeout,
                ServiceName, ClientName, KeepAlive,
                Version, ConnectTimeout, Password,
                TieBreaker, WriteBuffer, Ssl, SslHost, HighPrioritySocketThreads,
                ConfigChannel, AbortOnConnectFail, ResolveDns,
                ChannelPrefix, Proxy, ConnectRetry,
                ConfigCheckSeconds, DefaultDatabase,
            }.ToDictionary(x => x, StringComparer.OrdinalIgnoreCase);

            public static string TryNormalize(string value)
            {
                string tmp;
                if(value != null && normalizedOptions.TryGetValue(value, out tmp))
                {
                    return tmp ?? "";
                }
                return value ?? "";
            }
        }


        private readonly EndPointCollection endpoints = new EndPointCollection();

        private bool? allowAdmin, abortOnConnectFail, highPrioritySocketThreads, resolveDns, ssl;

        private string clientName, serviceName, password, tieBreaker, sslHost, configChannel;

        private CommandMap commandMap;

        private Version defaultVersion;

        private int? keepAlive, syncTimeout, connectTimeout, responseTimeout, writeBuffer, connectRetry, configCheckSeconds, defaultDatabase;

        private Proxy? proxy;

        /// <summary>
        /// A LocalCertificateSelectionCallback delegate responsible for selecting the certificate used for authentication; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event LocalCertificateSelectionCallback CertificateSelection;

        /// <summary>
        /// A RemoteCertificateValidationCallback delegate responsible for validating the certificate supplied by the remote party; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event RemoteCertificateValidationCallback CertificateValidation;

        /// <summary>
        /// Gets or sets whether connect/configuration timeouts should be explicitly notified via a TimeoutException
        /// </summary>
        public bool AbortOnConnectFail { get { return abortOnConnectFail ?? GetDefaultAbortOnConnectFailSetting(); } set { abortOnConnectFail = value; } }

        /// <summary>
        /// Indicates whether admin operations should be allowed
        /// </summary>
        public bool AllowAdmin { get { return allowAdmin.GetValueOrDefault(); } set { allowAdmin = value; } }

        /// <summary>
        /// Indicates whether the connection should be encrypted
        /// </summary>
        [Obsolete("Please use .Ssl instead of .UseSsl"),
#if !CORE_CLR
            Browsable(false),
#endif
            EditorBrowsable(EditorBrowsableState.Never)]
        public bool UseSsl { get { return Ssl; } set { Ssl = value; } }

        /// <summary>
        /// Indicates whether the connection should be encrypted
        /// </summary>
        public bool Ssl { get { return ssl.GetValueOrDefault(); } set { ssl = value; } }

        /// <summary>
        /// Automatically encodes and decodes channels
        /// </summary>
        public RedisChannel ChannelPrefix { get;set; }
        /// <summary>
        /// The client name to use for all connections
        /// </summary>
        public string ClientName { get { return clientName; } set { clientName = value; } }

        /// <summary>
        /// The number of times to repeat the initial connect cycle if no servers respond promptly
        /// </summary>
        public int ConnectRetry { get { return connectRetry ?? 3; } set { connectRetry = value; } }

        /// <summary>
        /// The command-map associated with this configuration
        /// </summary>
        public CommandMap CommandMap
        {
            get
            {
                if (commandMap != null) return commandMap;
                switch (Proxy)
                {
                    case Proxy.Twemproxy:
                        return CommandMap.Twemproxy;
                    default:
                        return CommandMap.Default;
                }
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                commandMap = value;
            }
        }

        /// <summary>
        /// Channel to use for broadcasting and listening for configuration change notification
        /// </summary>
        public string ConfigurationChannel { get { return configChannel ?? DefaultConfigurationChannel; } set { configChannel = value; } }

        /// <summary>
        /// Specifies the time in milliseconds that should be allowed for connection (defaults to 5 seconds unless SyncTimeout is higher)
        /// </summary>
        public int ConnectTimeout {
            get {
                if (connectTimeout.HasValue) return connectTimeout.GetValueOrDefault();
                return Math.Max(5000, SyncTimeout);
            }
            set { connectTimeout = value; }
        }

        /// <summary>
        /// The server version to assume
        /// </summary>
        public Version DefaultVersion { get { return defaultVersion ?? (IsAzureEndpoint() ? RedisFeatures.v3_0_0 : RedisFeatures.v2_0_0); } set { defaultVersion = value; } }

        /// <summary>
        /// The endpoints defined for this configuration
        /// </summary>
        public EndPointCollection EndPoints => endpoints;

        /// <summary>
        /// Use ThreadPriority.AboveNormal for SocketManager reader and writer threads (true by default). If false, ThreadPriority.Normal will be used.
        /// </summary>
        public bool HighPrioritySocketThreads { get { return highPrioritySocketThreads ?? true; } set { highPrioritySocketThreads = value; } }

        /// <summary>
        /// Specifies the time in seconds at which connections should be pinged to ensure validity
        /// </summary>
        public int KeepAlive { get { return keepAlive.GetValueOrDefault(-1); } set { keepAlive = value; } }

        /// <summary>
        /// The password to use to authenticate with the server
        /// </summary>
        public string Password { get { return password; } set { password = value; } }

        /// <summary>
        /// Indicates whether admin operations should be allowed
        /// </summary>
        public Proxy Proxy { get { return proxy.GetValueOrDefault(); } set { proxy = value; } }

        /// <summary>
        /// Indicates whether endpoints should be resolved via DNS before connecting.
        /// If enabled the ConnectionMultiplexer will not re-resolve DNS
        /// when attempting to re-connect after a connection failure.
        /// </summary>
        public bool ResolveDns { get { return resolveDns.GetValueOrDefault(); } set { resolveDns = value; } }

        /// <summary>
        /// The service name used to resolve a service via sentinel
        /// </summary>
        public string ServiceName { get { return serviceName; } set { serviceName = value; } }

        /// <summary>
        /// Gets or sets the SocketManager instance to be used with these options; if this is null a per-multiplexer
        /// SocketManager is created automatically.
        /// </summary>
        public SocketManager SocketManager {  get;set; }
        /// <summary>
        /// The target-host to use when validating SSL certificate; setting a value here enables SSL mode
        /// </summary>
        public string SslHost { get { return sslHost ?? InferSslHostFromEndpoints(); } set { sslHost = value; } }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for synchronous operations (defaults to 1 second)
        /// </summary>
        public int SyncTimeout { get { return syncTimeout.GetValueOrDefault(1000); } set { syncTimeout = value; } }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for responses before concluding that the socket is unhealthy
        /// (defaults to SyncTimeout)
        /// </summary>
        public int ResponseTimeout { get { return responseTimeout ?? SyncTimeout; } set { responseTimeout = value; } }

        /// <summary>
        /// Tie-breaker used to choose between masters (must match the endpoint exactly)
        /// </summary>
        public string TieBreaker { get { return tieBreaker ?? DefaultTieBreaker; } set { tieBreaker = value; } }
        /// <summary>
        /// The size of the output buffer to use
        /// </summary>
        public int WriteBuffer { get { return writeBuffer.GetValueOrDefault(4096); } set { writeBuffer = value; } }

        /// <summary>
        /// Specifies the default database to be used when calling ConnectionMultiplexer.GetDatabase() without any parameters
        /// </summary>
        public int? DefaultDatabase { get { return defaultDatabase; } set { defaultDatabase = value; } }

        internal LocalCertificateSelectionCallback CertificateSelectionCallback { get { return CertificateSelection; } private set { CertificateSelection = value; } }

        // these just rip out the underlying handlers, bypassing the event accessors - needed when creating the SSL stream
        internal RemoteCertificateValidationCallback CertificateValidationCallback { get { return CertificateValidation; } private set { CertificateValidation = value; } }

        /// <summary>
        /// Check configuration every n seconds (every minute by default)
        /// </summary>
        public int ConfigCheckSeconds { get { return configCheckSeconds.GetValueOrDefault(60); } set { configCheckSeconds = value; } }

        /// <summary>
        /// Parse the configuration from a comma-delimited configuration string
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="configuration"/> is empty.</exception>
        public static ConfigurationOptions Parse(string configuration)
        {    
            var options = new ConfigurationOptions();
            options.DoParse(configuration, false);
            return options;
        }
        /// <summary>
        /// Parse the configuration from a comma-delimited configuration string
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="configuration"/> is empty.</exception>
        public static ConfigurationOptions Parse(string configuration, bool ignoreUnknown)
        {
            var options = new ConfigurationOptions();
            options.DoParse(configuration, ignoreUnknown);
            return options;
        }

        /// <summary>
        /// Create a copy of the configuration
        /// </summary>
        public ConfigurationOptions Clone()
        {
            var options = new ConfigurationOptions
            {
                clientName = clientName,
                serviceName = serviceName,
                keepAlive = keepAlive,
                syncTimeout = syncTimeout,
                allowAdmin = allowAdmin,
                defaultVersion = defaultVersion,
                connectTimeout = connectTimeout,
                password = password,
                tieBreaker = tieBreaker,
                writeBuffer = writeBuffer,
                ssl = ssl,
                sslHost = sslHost,
                highPrioritySocketThreads = highPrioritySocketThreads,
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
				defaultDatabase = defaultDatabase,
            };
            foreach (var item in endpoints)
                options.endpoints.Add(item);
            return options;

        }

        /// <summary>
        /// Resolve the default port for any endpoints that did not have a port explicitly specified
        /// </summary>
        public void SetDefaultPorts()
        {
            endpoints.SetDefaultPorts(Ssl ? 6380 : 6379);
        }

        /// <summary>
        /// Returns the effective configuration string for this configuration, including Redis credentials.
        /// </summary>
        public override string ToString()
        {
            // include password to allow generation of configuration strings 
            // used for connecting multiplexer
            return ToString(includePassword: true);
        }

        /// <summary>
        /// Returns the effective configuration string for this configuration
        /// with the option to include or exclude the password from the string.
        /// </summary>
        public string ToString(bool includePassword)
        {
            var sb = new StringBuilder();
            foreach (var endpoint in endpoints)
            {
                Append(sb, Format.ToString(endpoint));
            }
            Append(sb, OptionKeys.ClientName, clientName);
            Append(sb, OptionKeys.ServiceName, serviceName);
            Append(sb, OptionKeys.KeepAlive, keepAlive);
            Append(sb, OptionKeys.SyncTimeout, syncTimeout);
            Append(sb, OptionKeys.AllowAdmin, allowAdmin);
            Append(sb, OptionKeys.Version, defaultVersion);
            Append(sb, OptionKeys.ConnectTimeout, connectTimeout);
            Append(sb, OptionKeys.Password, includePassword ? password : "*****");
            Append(sb, OptionKeys.TieBreaker, tieBreaker);
            Append(sb, OptionKeys.WriteBuffer, writeBuffer);
            Append(sb, OptionKeys.Ssl, ssl);
            Append(sb, OptionKeys.SslHost, sslHost); 
            Append(sb, OptionKeys.HighPrioritySocketThreads, highPrioritySocketThreads);
            Append(sb, OptionKeys.ConfigChannel, configChannel);
            Append(sb, OptionKeys.AbortOnConnectFail, abortOnConnectFail);
            Append(sb, OptionKeys.ResolveDns, resolveDns);
            Append(sb, OptionKeys.ChannelPrefix, (string)ChannelPrefix);
            Append(sb, OptionKeys.ConnectRetry, connectRetry);
            Append(sb, OptionKeys.Proxy, proxy);
            Append(sb, OptionKeys.ConfigCheckSeconds, configCheckSeconds);
            Append(sb, OptionKeys.ResponseTimeout, responseTimeout);
            Append(sb, OptionKeys.DefaultDatabase, defaultDatabase);
            commandMap?.AppendDeltas(sb);
            return sb.ToString();
        }

        internal bool HasDnsEndPoints()
        {
            foreach (var endpoint in endpoints) if (endpoint is DnsEndPoint) return true;
            return false;
        }

#pragma warning disable 1998 // NET40 is sync, not async, currently
        internal async Task ResolveEndPointsAsync(ConnectionMultiplexer multiplexer, TextWriter log)
        {
            Dictionary<string, IPAddress> cache = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < endpoints.Count; i++)
            {
                var dns = endpoints[i] as DnsEndPoint;
                if (dns != null)
                {
                    try
                    {
                        IPAddress ip;
                        if (dns.Host == ".")
                        {
                            endpoints[i] = new IPEndPoint(IPAddress.Loopback, dns.Port);
                        }
                        else if (cache.TryGetValue(dns.Host, out ip))
                        { // use cache
                            endpoints[i] = new IPEndPoint(ip, dns.Port);
                        }
                        else
                        {
                            multiplexer.LogLocked(log, "Using DNS to resolve '{0}'...", dns.Host);
#if NET40
                            var ips = Dns.GetHostAddresses(dns.Host);
#else
                            var ips = await Dns.GetHostAddressesAsync(dns.Host).ObserveErrors().ForAwait();
#endif
                            if (ips.Length == 1)
                            {
                                ip = ips[0];
                                multiplexer.LogLocked(log, "'{0}' => {1}", dns.Host, ip);
                                cache[dns.Host] = ip;
                                endpoints[i] = new IPEndPoint(ip, dns.Port);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        multiplexer.OnInternalError(ex);
                        multiplexer.LogLocked(log, ex.Message);
                    }
                }
            }
        }
#pragma warning restore 1998
        static void Append(StringBuilder sb, object value)
        {
            if (value == null) return;
            string s = Format.ToString(value);
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (sb.Length != 0) sb.Append(',');
                sb.Append(s);
            }
        }

        static void Append(StringBuilder sb, string prefix, object value)
        {
            string s = value?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (sb.Length != 0) sb.Append(',');
                if(!string.IsNullOrEmpty(prefix))
                {
                    sb.Append(prefix).Append('=');
                }
                sb.Append(s);
            }
        }

#if !CORE_CLR
        static bool IsOption(string option, string prefix)
        {
            return option.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase);
        }
#endif

        void Clear()
        {
            clientName = serviceName = password = tieBreaker = sslHost = configChannel = null;
            keepAlive = syncTimeout = connectTimeout = writeBuffer = connectRetry = configCheckSeconds = defaultDatabase = null;
            allowAdmin = abortOnConnectFail = highPrioritySocketThreads = resolveDns = ssl = null;
            defaultVersion = null;
            endpoints.Clear();
            commandMap = null;

            CertificateSelection = null;
            CertificateValidation = null;            
            ChannelPrefix = default(RedisChannel);
            SocketManager = null;
        }

#if !CORE_CLR
        object ICloneable.Clone() { return Clone(); }
#endif

        private void DoParse(string configuration, bool ignoreUnknown)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration))
            {
                throw new ArgumentException("is empty", configuration);
            }

            Clear();

            // break it down by commas
            var arr = configuration.Split(StringSplits.Comma);
            Dictionary<string, string> map = null;
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
                        case OptionKeys.SyncTimeout:
                            SyncTimeout = OptionKeys.ParseInt32(key, value, minValue: 1);
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
                        case OptionKeys.HighPrioritySocketThreads:
                            HighPrioritySocketThreads = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.WriteBuffer:
                            WriteBuffer = OptionKeys.ParseInt32(key, value);
                            break;
                        case OptionKeys.Proxy:
                            Proxy = OptionKeys.ParseProxy(key, value);
                            break;
                        case OptionKeys.ResponseTimeout:
                            ResponseTimeout = OptionKeys.ParseInt32(key, value, minValue: 1);
                            break;
                        case OptionKeys.DefaultDatabase:
                            defaultDatabase = OptionKeys.ParseInt32(key, value);
                            break;
                        default:
                            if (!string.IsNullOrEmpty(key) && key[0] == '$')
                            {
                                RedisCommand cmd;
                                var cmdName = option.Substring(1, idx - 1);
                                if (Enum.TryParse(cmdName, true, out cmd))
                                {
                                    if (map == null) map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    map[cmdName] = value;
                                }
                            }
                            else
                            {
                                if(!ignoreUnknown) OptionKeys.Unknown(key);
                            }
                            break;
                    }
                }
                else
                {
                    var ep = Format.TryParseEndPoint(option);
                    if (ep != null && !endpoints.Contains(ep)) endpoints.Add(ep);
                }
            }
            if (map != null && map.Count != 0)
            {
                CommandMap = CommandMap.Create(map);
            }
        }

        private bool GetDefaultAbortOnConnectFailSetting()
        {
            // Microsoft Azure team wants abortConnect=false by default
            if (IsAzureEndpoint())
                return false;

            return true;
        }

        private bool IsAzureEndpoint()
        {
            var result = false; 
            var dnsEndpoints = endpoints.Select(endpoint => endpoint as DnsEndPoint).Where(ep => ep != null);
            foreach(var ep in dnsEndpoints)
            {
                int firstDot = ep.Host.IndexOf('.');
                if (firstDot >= 0)
                {
                    var domain = ep.Host.Substring(firstDot).ToLowerInvariant();
                    switch(domain)
                    {
                        case ".redis.cache.windows.net":
                        case ".redis.cache.chinacloudapi.cn":
                        case ".redis.cache.usgovcloudapi.net":
                        case ".redis.cache.cloudapi.de":
                            return true;
                    }
                }
            }

            return result; 
        }

        private string InferSslHostFromEndpoints() {
            var dnsEndpoints = endpoints.Select(endpoint => endpoint as DnsEndPoint);
            string dnsHost = dnsEndpoints.FirstOrDefault()?.Host;
            if (dnsEndpoints.All(dnsEndpoint => (dnsEndpoint != null && dnsEndpoint.Host == dnsHost))) {
                return dnsHost;
            }

            return null;
        }
    }
}
