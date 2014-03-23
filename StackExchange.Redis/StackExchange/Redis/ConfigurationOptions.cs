using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The options relevant to a set of redis connections
    /// </summary>
    public sealed class ConfigurationOptions : ICloneable
    {
        internal const string DefaultTieBreaker = "__Booksleeve_TieBreak", DefaultConfigurationChannel = "__Booksleeve_MasterChanged";

        private const string AllowAdminPrefix = "allowAdmin=", SyncTimeoutPrefix = "syncTimeout=",
                                ServiceNamePrefix = "serviceName=", ClientNamePrefix = "name=", KeepAlivePrefix = "keepAlive=",
                        VersionPrefix = "version=", ConnectTimeoutPrefix = "connectTimeout=", PasswordPrefix = "password=",
                        TieBreakerPrefix = "tiebreaker=", WriteBufferPrefix = "writeBuffer=", SslHostPrefix = "sslHost=",
                        ConfigChannelPrefix = "configChannel=", AbortOnConnectFailPrefix = "abortConnect=", ResolveDnsPrefix = "resolveDns=",
                        ChannelPrefixPrefix = "channelPrefix=";

        private readonly EndPointCollection endpoints = new EndPointCollection();

        /// <summary>
        /// Automatically encodes and decodes channels
        /// </summary>
        public RedisChannel ChannelPrefix { get;set; }

        private bool? allowAdmin, abortOnConnectFail, resolveDns;

        private string clientName, serviceName, password, tieBreaker, sslHost, configChannel;
        private Version defaultVersion;

        private int? keepAlive, syncTimeout, connectTimeout, writeBuffer;
        /// <summary>
        /// Create a new ConfigurationOptions instance
        /// </summary>
        public ConfigurationOptions()
        {
            CommandMap = CommandMap.Default;
        }



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
        /// Gets or sets the SocketManager instance to be used with these options; if this is null a per-multiplexer
        /// SocketManager is created automatically.
        /// </summary>
        public SocketManager SocketManager {  get;set; }

        /// <summary>
        /// Indicates whether admin operations should be allowed
        /// </summary>
        public bool AllowAdmin { get { return allowAdmin.GetValueOrDefault(); } set { allowAdmin = value; } }

        /// <summary>
        /// Indicates whether endpoints should be resolved via DNS before connecting
        /// </summary>
        public bool ResolveDns { get { return resolveDns.GetValueOrDefault(); } set { resolveDns = value; } }

        /// <summary>
        /// The client name to user for all connections
        /// </summary>
        public string ClientName { get { return clientName; } set { clientName = value; } }

        /// <summary>
        /// The command-map associated with this configuration
        /// </summary>
        public CommandMap CommandMap { get; set; }

        /// <summary>
        /// Channel to use for broadcasting and listening for configuration change notification
        /// </summary>
        public string ConfigurationChannel { get { return configChannel ?? DefaultConfigurationChannel; } set { configChannel = value; } }

        /// <summary>
        /// Specifies the time in milliseconds that should be allowed for connection
        /// </summary>
        public int ConnectTimeout { get { return connectTimeout ?? SyncTimeout; } set { connectTimeout = value; } }

        /// <summary>
        /// The server version to assume
        /// </summary>
        public Version DefaultVersion { get { return defaultVersion ?? RedisFeatures.v2_0_0; } set { defaultVersion = value; } }

        /// <summary>
        /// The endpoints defined for this configuration
        /// </summary>
        public EndPointCollection EndPoints { get { return endpoints; } }

        /// <summary>
        /// Specifies the time in seconds at which connections should be pinged to ensure validity
        /// </summary>
        public int KeepAlive { get { return keepAlive.GetValueOrDefault(-1); } set { keepAlive = value; } }

        /// <summary>
        /// The password to use to authenticate with the server
        /// </summary>
        public string Password { get { return password; } set { password = value; } }

        /// <summary>
        /// The service name used to resolve a service via sentinel
        /// </summary>
        public string ServiceName { get { return serviceName; } set { serviceName = value; } }

        /// <summary>
        /// The target-host to use when validating SSL certificate; setting a value here enables SSL mode
        /// </summary>
        public string SslHost { get { return sslHost; } set { sslHost = value; } }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for synchronous operations
        /// </summary>
        public int SyncTimeout { get { return syncTimeout.GetValueOrDefault(1000); } set { syncTimeout = value; } }

        /// <summary>
        /// Tie-breaker used to choose between masters (must match the endpoint exactly)
        /// </summary>
        public string TieBreaker { get { return tieBreaker ?? DefaultTieBreaker; } set { tieBreaker = value; } }
        /// <summary>
        /// The size of the output buffer to use
        /// </summary>
        public int WriteBuffer { get { return writeBuffer.GetValueOrDefault(4096); } set { writeBuffer = value; } }

        internal LocalCertificateSelectionCallback CertificateSelectionCallback { get { return CertificateSelection; } }

        // these just rip out the underlying handlers, bypassing the event accessors - needed when creating the SSL stream
        internal RemoteCertificateValidationCallback CertificateValidationCallback { get { return CertificateValidation; } }

        /// <summary>
        /// Gets or sets whether connect/configuration timeouts should be explicitly notified via a TimeoutException
        /// </summary>
        public bool AbortOnConnectFail { get { return abortOnConnectFail ?? true; } set { abortOnConnectFail = value; } }

        /// <summary>
        /// Parse the configuration from a comma-delimited configuration string
        /// </summary>
        public static ConfigurationOptions Parse(string configuration)
        {
            var options = new ConfigurationOptions();
            options.DoParse(configuration);
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
                sslHost = sslHost,
                configChannel = configChannel,
                abortOnConnectFail = abortOnConnectFail,
                resolveDns = resolveDns,
                CommandMap = CommandMap,
                CertificateValidation = CertificateValidation,
                CertificateSelection = CertificateSelection,
                ChannelPrefix = ChannelPrefix.Clone(),
                SocketManager = SocketManager,
            };
            foreach (var item in endpoints)
                options.endpoints.Add(item);
            return options;

        }

        /// <summary>
        /// Returns the effective configuration string for this configuration
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var endpoint in endpoints)
            {
                Append(sb, Format.ToString(endpoint));
            }
            Append(sb, ClientNamePrefix, clientName);
            Append(sb, ServiceNamePrefix, serviceName);
            Append(sb, KeepAlivePrefix, keepAlive);
            Append(sb, SyncTimeoutPrefix, syncTimeout);
            Append(sb, AllowAdminPrefix, allowAdmin);
            Append(sb, VersionPrefix, defaultVersion);
            Append(sb, ConnectTimeoutPrefix, connectTimeout);
            Append(sb, PasswordPrefix, password);
            Append(sb, TieBreakerPrefix, tieBreaker);
            Append(sb, WriteBufferPrefix, writeBuffer);
            Append(sb, SslHostPrefix, sslHost);
            Append(sb, ConfigChannelPrefix, configChannel);
            Append(sb, AbortOnConnectFailPrefix, abortOnConnectFail);
            Append(sb, ResolveDnsPrefix, resolveDns);
            Append(sb, ChannelPrefixPrefix, (string)ChannelPrefix);
            CommandMap.AppendDeltas(sb);
            return sb.ToString();
        }

        internal bool HasDnsEndPoints()
        {
            foreach (var endpoint in endpoints) if (endpoint is DnsEndPoint) return true;
            return false;
        }

        internal async Task ResolveEndPointsAsync(ConnectionMultiplexer multiplexer, TextWriter log)
        {
            Dictionary<string, IPAddress> cache = new Dictionary<string, IPAddress>(StringComparer.InvariantCultureIgnoreCase);
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
                            var ips = await Dns.GetHostAddressesAsync(dns.Host).ObserveErrors().ForAwait();
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
            if (value == null) return;
            string s = value.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (sb.Length != 0) sb.Append(',');
                sb.Append(prefix).Append(s);
            }
        }

        static bool IsOption(string option, string prefix)
        {
            return option.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase);
        }
        void Clear()
        {
            clientName = serviceName = password = tieBreaker = sslHost =  configChannel = null;
            keepAlive = syncTimeout = connectTimeout = writeBuffer = null;
            allowAdmin = abortOnConnectFail = resolveDns = null;
            defaultVersion = null;
            endpoints.Clear();
            CertificateSelection = null;
            CertificateValidation = null;
            CommandMap = CommandMap.Default;
            ChannelPrefix = default(RedisChannel);
            SocketManager = null;
        }

        object ICloneable.Clone() { return Clone(); }

        private void DoParse(string configuration)
        {
            Clear();
            if (!string.IsNullOrWhiteSpace(configuration))
            {
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
                        var value = option.Substring(idx + 1).Trim();
                        if (IsOption(option, SyncTimeoutPrefix))
                        {
                            int tmp;
                            if (Format.TryParseInt32(value.Trim(), out tmp) && tmp > 0) SyncTimeout = tmp;
                        }
                        else if (IsOption(option, AllowAdminPrefix))
                        {
                            bool tmp;
                            if (Format.TryParseBoolean(value.Trim(), out tmp)) AllowAdmin = tmp;
                        }
                        else if (IsOption(option, AbortOnConnectFailPrefix))
                        {
                            bool tmp;
                            if (Format.TryParseBoolean(value.Trim(), out tmp)) AbortOnConnectFail = tmp;
                        }
                        else if (IsOption(option, ResolveDnsPrefix))
                        {
                            bool tmp;
                            if (Format.TryParseBoolean(value.Trim(), out tmp)) ResolveDns = tmp;
                        }
                        else if (IsOption(option, ServiceNamePrefix))
                        {
                            ServiceName = value.Trim();
                        }
                        else if (IsOption(option, ClientNamePrefix))
                        {
                            ClientName = value.Trim();
                        }
                        else if (IsOption(option, ChannelPrefixPrefix))
                        {
                            ChannelPrefix = value.Trim();
                        }
                        else if (IsOption(option, ConfigChannelPrefix))
                        {
                            ConfigurationChannel = value.Trim();
                        }
                        else if (IsOption(option, KeepAlivePrefix))
                        {
                            int tmp;
                            if (Format.TryParseInt32(value.Trim(), out tmp)) KeepAlive = tmp;
                        }
                        else if (IsOption(option, ConnectTimeoutPrefix))
                        {
                            int tmp;
                            if (Format.TryParseInt32(value.Trim(), out tmp)) ConnectTimeout = tmp;
                        }
                        else if (IsOption(option, VersionPrefix))
                        {
                            Version tmp;
                            if (Version.TryParse(value.Trim(), out tmp)) DefaultVersion = tmp;
                        }
                        else if (IsOption(option, PasswordPrefix))
                        {
                            Password = value.Trim();
                        }
                        else if (IsOption(option, TieBreakerPrefix))
                        {
                            TieBreaker = value.Trim();
                        }
                        else if (IsOption(option, SslHostPrefix))
                        {
                            SslHost = value.Trim();
                        }
                        else if (IsOption(option, WriteBufferPrefix))
                        {
                            int tmp;
                            if (Format.TryParseInt32(value.Trim(), out tmp)) WriteBuffer = tmp;
                        }
                        else if(option[0]=='$')
                        {
                            RedisCommand cmd;
                            option = option.Substring(1, idx-1);
                            if (Enum.TryParse<RedisCommand>(option, true, out cmd))
                            {
                                if (map == null) map = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                                map[option] = value;
                            }
                        }
                        else
                        {
                            ConnectionMultiplexer.TraceWithoutContext("Unknown configuration option:" + option);
                        }
                    }
                    else
                    {
                        var ep = Format.TryParseEndPoint(option);
                        if (ep != null && !endpoints.Contains(ep)) endpoints.Add(ep);
                    }
                }
                this.CommandMap = CommandMap.Create(map);
            }
        }
    }
}
