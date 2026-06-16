using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Configuration;

namespace StackExchange.Redis
{
    public sealed class BufferOptions
    {
        public MemoryPool<byte>? MemoryPool { get; init; }

        public int BufferSize { get; init; }

        public float BufferGrowthFactor { get; init; }
    }

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
                if (Format.TryParseVersion(value, out Version? tmp))
                {
                    return tmp;
                }
                throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a version value; the value '{value}' is not recognised.");
            }

            internal static Proxy ParseProxy(string key, string value)
            {
                if (!Enum.TryParse(value, true, out Proxy tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a proxy value; the value '{value}' is not recognised.");
                return tmp;
            }

            internal static SslProtocols ParseSslProtocols(string key, string? value)
            {
                // Flags expect commas as separators, but we need to use '|' since commas are already used in the connection string to mean something else
                value = value?.Replace("|", ",");

                if (!Enum.TryParse(value, true, out SslProtocols tmp)) throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires an SslProtocol value (multiple values separated by '|'); the value '{value}' is not recognised.");

                return tmp;
            }

            internal static RedisProtocol ParseRedisProtocol(string key, string value)
            {
                if (TryParseRedisProtocol(value, out var protocol)) return protocol;
                throw new ArgumentOutOfRangeException(key, $"Keyword '{key}' requires a RedisProtocol value or a known protocol version number; the value '{value}' is not recognised.");
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
                CheckCertificateRevocation = "checkCertificateRevocation",
                Tunnel = "tunnel",
                SetClientLibrary = "setlib",
                Protocol = "protocol",
                HighIntegrity = "highIntegrity",
                TcpKeepAlive = "tcpKeepAlive";

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
                ResponseTimeout,
                ServiceName,
                Ssl,
                SslHost,
                SslProtocols,
                SyncTimeout,
                TieBreaker,
                Version,
                WriteBuffer,
                CheckCertificateRevocation,
                Tunnel,
                SetClientLibrary,
                Protocol,
                HighIntegrity,
                TcpKeepAlive,
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

        [Flags]
        private enum OptionFlags : ulong
        {
            None = 0,
            AllowAdminHasValue = 1UL << 0,
            AllowAdminValue = 1UL << 1,
            AbortOnConnectFailHasValue = 1UL << 2,
            AbortOnConnectFailValue = 1UL << 3,
            ResolveDnsHasValue = 1UL << 4,
            ResolveDnsValue = 1UL << 5,
            SslHasValue = 1UL << 6,
            SslValue = 1UL << 7,
            CheckCertificateRevocationHasValue = 1UL << 8,
            CheckCertificateRevocationValue = 1UL << 9,
            HeartbeatConsistencyChecksHasValue = 1UL << 10,
            HeartbeatConsistencyChecksValue = 1UL << 11,
            IncludeDetailInExceptionsHasValue = 1UL << 12,
            IncludeDetailInExceptionsValue = 1UL << 13,
            IncludePerformanceCountersInExceptionsHasValue = 1UL << 14,
            IncludePerformanceCountersInExceptionsValue = 1UL << 15,
            SetClientLibraryHasValue = 1UL << 16,
            SetClientLibraryValue = 1UL << 17,
            HighIntegrityHasValue = 1UL << 18,
            HighIntegrityValue = 1UL << 19,
            TcpKeepAliveHasValue = 1UL << 20,
            TcpKeepAliveValue = 1UL << 21,
            HeartbeatIntervalHasValue = 1UL << 22,
            KeepAliveHasValue = 1UL << 23,
            AsyncTimeoutHasValue = 1UL << 24,
            SyncTimeoutHasValue = 1UL << 25,
            ConnectTimeoutHasValue = 1UL << 26,
            ResponseTimeoutHasValue = 1UL << 27,
            ConnectRetryHasValue = 1UL << 28,
            ConfigCheckSecondsHasValue = 1UL << 29,
            ProxyHasValue = 1UL << 30,
            DefaultDatabaseHasValue = 1UL << 31,
            SslProtocolsHasValue = 1UL << 32,
            ProtocolHasValue = 1UL << 33,
            AllowSimulateConnectionFailure = 1UL << 34,
        }

        private OptionFlags optionFlags;

        private string? tieBreaker, sslHost, configChannel, user, password;

        private TimeSpan heartbeatInterval;

        private CommandMap? commandMap;

        private Version? defaultVersion;

        private int keepAlive, asyncTimeout, syncTimeout, connectTimeout, responseTimeout, connectRetry, configCheckSeconds, defaultDatabase;

        private Proxy proxy;

        private IReconnectRetryPolicy? reconnectRetryPolicy;

        private BacklogPolicy? backlogPolicy;

        private ILoggerFactory? loggerFactory;

        private SslProtocols sslProtocols;

        private RedisProtocol _protocol;

        private bool HasValue(OptionFlags hasValue) => (optionFlags & hasValue) != 0;

        private bool IsSet(OptionFlags value) => (optionFlags & value) != 0;

        private void SetBooleanWithValue(OptionFlags hasValue, OptionFlags valueFlag, bool value)
        {
            optionFlags |= hasValue;
            if (value)
            {
                optionFlags |= valueFlag;
            }
            else
            {
                optionFlags &= ~valueFlag;
            }
        }

        private void SetFlag(OptionFlags flag, bool value)
        {
            if (value)
            {
                optionFlags |= flag;
            }
            else
            {
                optionFlags &= ~flag;
            }
        }

        private T? Get<T>(OptionFlags hasValue, in T value) where T : struct
            => HasValue(hasValue) ? value : null;

        private void Set<T>(OptionFlags hasValue, ref T storage, T? value) where T : struct
        {
            if (value.HasValue)
            {
                SetWithValue(hasValue, ref storage, value.GetValueOrDefault());
            }
            else
            {
                optionFlags &= ~hasValue;
                storage = default;
            }
        }

        private void SetWithValue<T>(OptionFlags hasValue, ref T storage, T value) where T : struct
        {
            optionFlags |= hasValue;
            storage = value;
        }

        /// <summary>
        /// A LocalCertificateSelectionCallback delegate responsible for selecting the certificate used for authentication; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly", Justification = "Existing compatibility")]
        public event LocalCertificateSelectionCallback? CertificateSelection;

        /// <summary>
        /// A RemoteCertificateValidationCallback delegate responsible for validating the certificate supplied by the remote party; note
        /// that this cannot be specified in the configuration-string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly", Justification = "Existing compatibility")]
        public event RemoteCertificateValidationCallback? CertificateValidation;

        /// <summary>
        /// The default (not explicitly configured) options for this connection, fetched based on our parsed endpoints.
        /// </summary>
        public DefaultOptionsProvider Defaults
        {
            get => defaultOptions ??= DefaultOptionsProvider.GetProvider(EndPoints);
            set => defaultOptions = value;
        }

        /// <summary>
        /// Allows modification of a <see cref="Socket"/> between creation and connection.
        /// Passed in is the endpoint we're connecting to, which type of connection it is, and the socket itself.
        /// For example, a specific local IP endpoint could be bound, linger time altered, etc.
        /// </summary>
        public Action<EndPoint, ConnectionType, Socket>? BeforeSocketConnect { get; set; }

        public BufferOptions? RequestBufferOptions { get; set; }

        public BufferOptions? ResponseBufferOptions { get; set; }

        public ArrayPool<byte>? ResponseArrayPool { get; set; }

        internal Func<ConnectionMultiplexer, Action<string>, Task> AfterConnectAsync => Defaults.AfterConnectAsync;

        /// <summary>
        /// Gets or sets whether connect/configuration timeouts should be explicitly notified via a TimeoutException.
        /// </summary>
        public bool AbortOnConnectFail
        {
            get => HasValue(OptionFlags.AbortOnConnectFailHasValue) ? IsSet(OptionFlags.AbortOnConnectFailValue) : Defaults.AbortOnConnectFail;
            set => SetBooleanWithValue(OptionFlags.AbortOnConnectFailHasValue, OptionFlags.AbortOnConnectFailValue, value);
        }

        /// <summary>
        /// Indicates whether admin operations should be allowed.
        /// </summary>
        public bool AllowAdmin
        {
            get => HasValue(OptionFlags.AllowAdminHasValue) ? IsSet(OptionFlags.AllowAdminValue) : Defaults.AllowAdmin;
            set => SetBooleanWithValue(OptionFlags.AllowAdminHasValue, OptionFlags.AllowAdminValue, value);
        }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for asynchronous operations (defaults to SyncTimeout).
        /// </summary>
        public int AsyncTimeout
        {
            get => HasValue(OptionFlags.AsyncTimeoutHasValue) ? asyncTimeout : SyncTimeout;
            set => SetWithValue(OptionFlags.AsyncTimeoutHasValue, ref asyncTimeout, value);
        }

        /// <summary>
        /// Indicates whether the connection should be encrypted.
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
        /// Gets or sets whether the library should identify itself by library-name/version when possible.
        /// </summary>
        public bool SetClientLibrary
        {
            get => HasValue(OptionFlags.SetClientLibraryHasValue) ? IsSet(OptionFlags.SetClientLibraryValue) : Defaults.SetClientLibrary;
            set => SetBooleanWithValue(OptionFlags.SetClientLibraryHasValue, OptionFlags.SetClientLibraryValue, value);
        }

        /// <summary>
        /// Gets or sets the library name to use for CLIENT SETINFO lib-name calls to Redis during handshake.
        /// Defaults to "SE.Redis".
        /// </summary>
        /// <remarks>If the value is null, empty or whitespace, then the value from the options-provider is used;
        /// to disable the library name feature, use <see cref="SetClientLibrary"/> instead.</remarks>
        public string? LibraryName { get; set; }

        /// <summary>
        /// Automatically encodes and decodes channels.
        /// </summary>
        public RedisChannel ChannelPrefix { get; set; }

        /// <summary>
        /// A Boolean value that specifies whether the certificate revocation list is checked during authentication.
        /// </summary>
        public bool CheckCertificateRevocation
        {
            get => HasValue(OptionFlags.CheckCertificateRevocationHasValue) ? IsSet(OptionFlags.CheckCertificateRevocationValue) : Defaults.CheckCertificateRevocation;
            set => SetBooleanWithValue(OptionFlags.CheckCertificateRevocationHasValue, OptionFlags.CheckCertificateRevocationValue, value);
        }

        /// <summary>
        /// A Boolean value that specifies whether to use per-command validation of strict protocol validity.
        /// This sends an additional command after EVERY command which incurs measurable overhead.
        /// </summary>
        /// <remarks>
        /// The regular RESP protocol does not include correlation identifiers between requests and responses; in exceptional
        /// scenarios, protocol desynchronization can occur, which may not be noticed immediately; this option adds additional data
        /// to ensure that this cannot occur, at the cost of some (small) additional bandwidth usage.
        /// </remarks>
        public bool HighIntegrity
        {
            get => HasValue(OptionFlags.HighIntegrityHasValue) ? IsSet(OptionFlags.HighIntegrityValue) : Defaults.HighIntegrity;
            set => SetBooleanWithValue(OptionFlags.HighIntegrityHasValue, OptionFlags.HighIntegrityValue, value);
        }

        /// <summary>
        /// Create a certificate validation check that checks against the supplied issuer even when not known by the machine.
        /// </summary>
        /// <param name="issuerCertificatePath">The file system path to find the certificate at.</param>
        public void TrustIssuer(string issuerCertificatePath) => CertificateValidationCallback = TrustIssuerCallback(issuerCertificatePath);

#if NET
        /// <summary>
        /// Supply a user certificate from a PEM file pair and enable TLS.
        /// </summary>
        /// <param name="userCertificatePath">The path for the the user certificate (commonly a .crt file).</param>
        /// <param name="userKeyPath">The path for the the user key (commonly a .key file).</param>
        public void SetUserPemCertificate(string userCertificatePath, string? userKeyPath = null)
        {
            CertificateSelectionCallback = CreatePemUserCertificateCallback(userCertificatePath, userKeyPath);
            Ssl = true;
        }
#endif

        /// <summary>
        /// Supply a user certificate from a PFX file and optional password and enable TLS.
        /// </summary>
        /// <param name="userCertificatePath">The path for the the user certificate (commonly a .pfx file).</param>
        /// <param name="password">The password for the certificate file.</param>
        public void SetUserPfxCertificate(string userCertificatePath, string? password = null)
        {
            CertificateSelectionCallback = CreatePfxUserCertificateCallback(userCertificatePath, password);
            Ssl = true;
        }

#if NET
        internal static LocalCertificateSelectionCallback CreatePemUserCertificateCallback(string userCertificatePath, string? userKeyPath)
        {
            // PEM handshakes not universally supported and causes a runtime error about ephemeral certificates; to avoid, export as PFX
            using var pem = X509Certificate2.CreateFromPemFile(userCertificatePath, userKeyPath);
#pragma warning disable SYSLIB0057 // X509 loading
            var pfx = new X509Certificate2(pem.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // X509 loading

            return (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) => pfx;
        }
#endif

        internal static LocalCertificateSelectionCallback CreatePfxUserCertificateCallback(string userCertificatePath, string? password, X509KeyStorageFlags storageFlags = X509KeyStorageFlags.DefaultKeySet)
        {
#pragma warning disable SYSLIB0057 // X509 loading
            var pfx = new X509Certificate2(userCertificatePath, password ?? "", storageFlags);
#pragma warning restore SYSLIB0057 // X509 loading
            return (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) => pfx;
        }

        /// <summary>
        /// Create a certificate validation check that checks against the supplied issuer even when not known by the machine.
        /// </summary>
        /// <param name="issuer">The issuer to trust.</param>
        public void TrustIssuer(X509Certificate2 issuer) => CertificateValidationCallback = TrustIssuerCallback(issuer);

        internal static RemoteCertificateValidationCallback TrustIssuerCallback(string issuerCertificatePath)
#pragma warning disable SYSLIB0057 // X509 loading
            => TrustIssuerCallback(new X509Certificate2(issuerCertificatePath));
#pragma warning restore SYSLIB0057 // X509 loading

        private static RemoteCertificateValidationCallback TrustIssuerCallback(X509Certificate2 issuer)
        {
            if (issuer == null) throw new ArgumentNullException(nameof(issuer));

            return (object _, X509Certificate? certificate, X509Chain? certificateChain, SslPolicyErrors sslPolicyError) =>
            {
                // If we're already valid, there's nothing further to check
                if (sslPolicyError == SslPolicyErrors.None)
                {
                    return true;
                }
                // If we're not valid due to chain errors - check against the trusted issuer
                // Note that we're only proceeding at all here if the *only* issue is chain errors (not more flags in SslPolicyErrors)
                return sslPolicyError == SslPolicyErrors.RemoteCertificateChainErrors
                       && certificate is X509Certificate2 v2
                       && CheckTrustedIssuer(v2, certificateChain, issuer);
            };
        }

        private static readonly Oid _serverAuthOid = new Oid("1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.1");

        private static bool CheckTrustedIssuer(X509Certificate2 certificateToValidate, X509Chain? chainToValidate, X509Certificate2 authority)
        {
            // Reference:
            // https://stackoverflow.com/questions/6497040/how-do-i-validate-that-a-certificate-was-created-by-a-particular-certification-a
            // https://github.com/stewartadam/dotnet-x509-certificate-verification
            using X509Chain chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.VerificationTime = chainToValidate?.ChainPolicy?.VerificationTime ?? DateTime.Now;
            chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 0);
            // Ensure entended key usage checks are run and that we're observing a server TLS certificate
            chain.ChainPolicy.ApplicationPolicy.Add(_serverAuthOid);

            chain.ChainPolicy.ExtraStore.Add(authority);
            try
            {
                // This only verifies that the chain is valid, but with AllowUnknownCertificateAuthority could trust
                // self-signed or partial chained certificates
                bool chainIsVerified;
                try
                {
                    chainIsVerified = chain.Build(certificateToValidate);
                }
                catch (ArgumentException ex) when ((ex.ParamName ?? ex.Message) == "certificate" && Runtime.IsMono)
                {
                    // work around Mono cert limitation; report as rejected rather than fault
                    // (note also the likely .ctor mixup re param-name vs message)
                    chainIsVerified = false;
                }
                if (chainIsVerified)
                {
                    // Our method is "TrustIssuer", which means any intermediate cert we're being told to trust
                    // is a valid thing to trust, up until it's a root CA
                    bool found = false;
                    byte[] authorityData = authority.RawData;
                    foreach (var chainElement in chain.ChainElements)
                    {
                        using var chainCert = chainElement.Certificate;
                        if (!found)
                        {
#if NET8_0_OR_GREATER
                            if (chainCert.RawDataMemory.Span.SequenceEqual(authorityData))
#else
                            if (chainCert.RawData.SequenceEqual(authorityData))
#endif
                            {
                                found = true;
                            }
                        }
                    }
                    return found;
                }
            }
            catch (CryptographicException)
            {
                // We specifically don't want to throw during validation here and would rather exit out gracefully
            }

            // If we didn't find the trusted issuer in the chain at all - we do not trust the result.
            return false;
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
            get => HasValue(OptionFlags.ConnectRetryHasValue) ? connectRetry : Defaults.ConnectRetry;
            set => SetWithValue(OptionFlags.ConnectRetryHasValue, ref connectRetry, value);
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
            get
            {
                if (HasValue(OptionFlags.ConnectTimeoutHasValue)) return connectTimeout;
                var defaultTimeout = Defaults.ConnectTimeout;
                return defaultTimeout.HasValue ? (int)defaultTimeout.GetValueOrDefault().TotalMilliseconds : Math.Max(5000, SyncTimeout);
            }
            set => SetWithValue(OptionFlags.ConnectTimeoutHasValue, ref connectTimeout, value);
        }

        /// <summary>
        /// Specifies the default database to be used when calling <see cref="ConnectionMultiplexer.GetDatabase(int, object)"/> without any parameters.
        /// </summary>
        public int? DefaultDatabase
        {
            get => Get(OptionFlags.DefaultDatabaseHasValue, in defaultDatabase);
            set => Set(OptionFlags.DefaultDatabaseHasValue, ref defaultDatabase, value);
        }

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
        /// Whether to enable ECHO checks on every heartbeat to ensure network stream consistency.
        /// This is a rare measure to react to any potential network traffic drops ASAP, terminating the connection.
        /// </summary>
        public bool HeartbeatConsistencyChecks
        {
            get => HasValue(OptionFlags.HeartbeatConsistencyChecksHasValue) ? IsSet(OptionFlags.HeartbeatConsistencyChecksValue) : Defaults.HeartbeatConsistencyChecks;
            set => SetBooleanWithValue(OptionFlags.HeartbeatConsistencyChecksHasValue, OptionFlags.HeartbeatConsistencyChecksValue, value);
        }

        /// <summary>
        /// Controls how often the connection heartbeats. A heartbeat includes:
        /// - Evaluating if any messages have timed out.
        /// - Evaluating connection status (checking for failures).
        /// - Sending a server message to keep the connection alive if needed.
        /// </summary>
        /// <remarks>
        /// This defaults to 1000 milliseconds and should not be changed for most use cases.
        /// If for example you want to evaluate whether commands have violated the <see cref="AsyncTimeout"/> at a lower fidelity
        /// than 1000 milliseconds, you could lower this value.
        /// Be aware setting this very low incurs additional overhead of evaluating the above more often.
        /// </remarks>
        public TimeSpan HeartbeatInterval
        {
            get => HasValue(OptionFlags.HeartbeatIntervalHasValue) ? heartbeatInterval : Defaults.HeartbeatInterval;
            set => SetWithValue(OptionFlags.HeartbeatIntervalHasValue, ref heartbeatInterval, value);
        }

        /// <summary>
        /// Use ThreadPriority.AboveNormal for SocketManager reader and writer threads (true by default).
        /// If <see langword="false"/>, <see cref="ThreadPriority.Normal"/> will be used.
        /// </summary>
        [Obsolete($"This setting no longer has any effect, please use {nameof(SocketManager.SocketManagerOptions)}.{nameof(SocketManager.SocketManagerOptions.UseHighPrioritySocketThreads)} instead - this setting will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public bool HighPrioritySocketThreads
        {
            get => false;
            set { }
        }

        /// <summary>
        /// Whether exceptions include identifiable details (key names, additional .Data annotations).
        /// </summary>
        public bool IncludeDetailInExceptions
        {
            get => HasValue(OptionFlags.IncludeDetailInExceptionsHasValue) ? IsSet(OptionFlags.IncludeDetailInExceptionsValue) : Defaults.IncludeDetailInExceptions;
            set => SetBooleanWithValue(OptionFlags.IncludeDetailInExceptionsHasValue, OptionFlags.IncludeDetailInExceptionsValue, value);
        }

        /// <summary>
        /// Whether exceptions include performance counter details.
        /// </summary>
        /// <remarks>
        /// CPU usage, etc - note that this can be problematic on some platforms.
        /// </remarks>
        public bool IncludePerformanceCountersInExceptions
        {
            get => HasValue(OptionFlags.IncludePerformanceCountersInExceptionsHasValue) ? IsSet(OptionFlags.IncludePerformanceCountersInExceptionsValue) : Defaults.IncludePerformanceCountersInExceptions;
            set => SetBooleanWithValue(OptionFlags.IncludePerformanceCountersInExceptionsHasValue, OptionFlags.IncludePerformanceCountersInExceptionsValue, value);
        }

        /// <summary>
        /// Specifies the time in seconds at which connections should be pinged to ensure validity.
        /// -1 Defaults to 60 Seconds.
        /// </summary>
        public int KeepAlive
        {
            get => HasValue(OptionFlags.KeepAliveHasValue) ? keepAlive : (int)Defaults.KeepAliveInterval.TotalSeconds;
            set => SetWithValue(OptionFlags.KeepAliveHasValue, ref keepAlive, value);
        }

        /// <summary>
        /// Gets or sets whether to enable TCP keep-alive when appropriate (endpoint- and platform-dependent).
        /// </summary>
        public bool TcpKeepAlive
        {
            get => HasValue(OptionFlags.TcpKeepAliveHasValue) ? IsSet(OptionFlags.TcpKeepAliveValue) : Defaults.TcpKeepAlive;
            set => SetBooleanWithValue(OptionFlags.TcpKeepAliveHasValue, OptionFlags.TcpKeepAliveValue, value);
        }

        /// <summary>
        /// The <see cref="ILoggerFactory"/> to get loggers for connection events.
        /// Note: changes here only affect <see cref="ConnectionMultiplexer"/>s created after.
        /// </summary>
        public ILoggerFactory? LoggerFactory
        {
            get => loggerFactory ?? Defaults.LoggerFactory;
            set => loggerFactory = value;
        }

        /// <summary>
        /// The username to use to authenticate with the server.
        /// </summary>
        public string? User
        {
            get => user ?? Defaults.User;
            set => user = value;
        }

        /// <summary>
        /// The password to use to authenticate with the server.
        /// </summary>
        public string? Password
        {
            get => password ?? Defaults.Password;
            set => password = value;
        }

        /// <summary>
        /// Specifies whether asynchronous operations should be invoked in a way that guarantees their original delivery order.
        /// </summary>
        [Obsolete("Not supported; if you require ordered pub/sub, please see " + nameof(ChannelMessageQueue) + " - this will be removed in 3.0.", false)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
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
            get => HasValue(OptionFlags.ProxyHasValue) ? proxy : Defaults.Proxy;
            set => SetWithValue(OptionFlags.ProxyHasValue, ref proxy, value);
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
            get => HasValue(OptionFlags.ResolveDnsHasValue) ? IsSet(OptionFlags.ResolveDnsValue) : Defaults.ResolveDns;
            set => SetBooleanWithValue(OptionFlags.ResolveDnsHasValue, OptionFlags.ResolveDnsValue, value);
        }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for responses before concluding that the socket is unhealthy.
        /// </summary>
        [Obsolete("This setting no longer has any effect, and should not be used - will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
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
        [Obsolete("SocketManager is no longer used by StackExchange.Redis")]
        public SocketManager? SocketManager { get; set; }

#if NET
        /// <summary>
        /// A <see cref="SslClientAuthenticationOptions"/> provider for a given host, for custom TLS connection options.
        /// Note: this overrides *all* other TLS and certificate settings, only for advanced use cases.
        /// </summary>
        public Func<string, SslClientAuthenticationOptions>? SslClientAuthenticationOptions { get; set; }
#endif

        /// <summary>
        /// Indicates whether the connection should be encrypted.
        /// </summary>
        public bool Ssl
        {
            get => HasValue(OptionFlags.SslHasValue) ? IsSet(OptionFlags.SslValue) : Defaults.GetDefaultSsl(EndPoints);
            set => SetBooleanWithValue(OptionFlags.SslHasValue, OptionFlags.SslValue, value);
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
        public SslProtocols? SslProtocols
        {
            get => Get(OptionFlags.SslProtocolsHasValue, in sslProtocols);
            set => Set(OptionFlags.SslProtocolsHasValue, ref sslProtocols, value);
        }

        /// <summary>
        /// Specifies the time in milliseconds that the system should allow for synchronous operations (defaults to 5 seconds).
        /// </summary>
        public int SyncTimeout
        {
            get => HasValue(OptionFlags.SyncTimeoutHasValue) ? syncTimeout : (int)Defaults.SyncTimeout.TotalMilliseconds;
            set => SetWithValue(OptionFlags.SyncTimeoutHasValue, ref syncTimeout, value);
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
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
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
            get => HasValue(OptionFlags.ConfigCheckSecondsHasValue) ? configCheckSeconds : (int)Defaults.ConfigCheckInterval.TotalSeconds;
            set => SetWithValue(OptionFlags.ConfigCheckSecondsHasValue, ref configCheckSeconds, value);
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
            optionFlags = this.optionFlags,
            ClientName = ClientName,
            ServiceName = ServiceName,
            keepAlive = keepAlive,
            syncTimeout = syncTimeout,
            asyncTimeout = asyncTimeout,
            defaultVersion = defaultVersion,
            connectTimeout = connectTimeout,
            user = user,
            password = password,
            tieBreaker = tieBreaker,
            sslHost = sslHost,
            configChannel = configChannel,
            proxy = proxy,
            commandMap = commandMap,
            CertificateValidationCallback = CertificateValidationCallback,
            CertificateSelectionCallback = CertificateSelectionCallback,
            ChannelPrefix = ChannelPrefix.Clone(),
#pragma warning disable CS0618 // Type or member is obsolete
            SocketManager = SocketManager,
#pragma warning restore CS0618 // Type or member is obsolete
            connectRetry = connectRetry,
            configCheckSeconds = configCheckSeconds,
            responseTimeout = responseTimeout,
            defaultDatabase = defaultDatabase,
            reconnectRetryPolicy = reconnectRetryPolicy,
            backlogPolicy = backlogPolicy,
            sslProtocols = sslProtocols,
            RequestBufferOptions = RequestBufferOptions,
            ResponseBufferOptions = ResponseBufferOptions,
            ResponseArrayPool = ResponseArrayPool,
            BeforeSocketConnect = BeforeSocketConnect,
            EndPoints = EndPoints.Clone(),
            LoggerFactory = LoggerFactory,
#if NET
            SslClientAuthenticationOptions = SslClientAuthenticationOptions,
#endif
            Tunnel = Tunnel,
            LibraryName = LibraryName,
            _protocol = _protocol,
            heartbeatInterval = heartbeatInterval,
            WriteMode = WriteMode,
#if DEBUG
            OutputLog = OutputLog,
#endif
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
            Append(sb, OptionKeys.KeepAlive, OptionFlags.KeepAliveHasValue, in keepAlive);
            Append(sb, OptionKeys.SyncTimeout, OptionFlags.SyncTimeoutHasValue, in syncTimeout);
            Append(sb, OptionKeys.AsyncTimeout, OptionFlags.AsyncTimeoutHasValue, in asyncTimeout);
            Append(sb, OptionKeys.AllowAdmin, OptionFlags.AllowAdminHasValue, OptionFlags.AllowAdminValue);
            Append(sb, OptionKeys.Version, defaultVersion);
            Append(sb, OptionKeys.ConnectTimeout, OptionFlags.ConnectTimeoutHasValue, in connectTimeout);
            Append(sb, OptionKeys.User, user);
            Append(sb, OptionKeys.Password, (includePassword || string.IsNullOrEmpty(password)) ? password : "*****");
            Append(sb, OptionKeys.TieBreaker, tieBreaker);
            Append(sb, OptionKeys.Ssl, OptionFlags.SslHasValue, OptionFlags.SslValue);
            if (HasValue(OptionFlags.SslProtocolsHasValue)) Append(sb, OptionKeys.SslProtocols, sslProtocols.ToString().Replace(',', '|'));
            Append(sb, OptionKeys.CheckCertificateRevocation, OptionFlags.CheckCertificateRevocationHasValue, OptionFlags.CheckCertificateRevocationValue);
            Append(sb, OptionKeys.SslHost, sslHost);
            Append(sb, OptionKeys.ConfigChannel, configChannel);
            Append(sb, OptionKeys.AbortOnConnectFail, OptionFlags.AbortOnConnectFailHasValue, OptionFlags.AbortOnConnectFailValue);
            Append(sb, OptionKeys.ResolveDns, OptionFlags.ResolveDnsHasValue, OptionFlags.ResolveDnsValue);
            Append(sb, OptionKeys.ChannelPrefix, (string?)ChannelPrefix);
            Append(sb, OptionKeys.ConnectRetry, OptionFlags.ConnectRetryHasValue, in connectRetry);
            Append(sb, OptionKeys.Proxy, OptionFlags.ProxyHasValue, in proxy);
            Append(sb, OptionKeys.ConfigCheckSeconds, OptionFlags.ConfigCheckSecondsHasValue, in configCheckSeconds);
            Append(sb, OptionKeys.ResponseTimeout, OptionFlags.ResponseTimeoutHasValue, in responseTimeout);
            Append(sb, OptionKeys.DefaultDatabase, OptionFlags.DefaultDatabaseHasValue, in defaultDatabase);
            Append(sb, OptionKeys.SetClientLibrary, OptionFlags.SetClientLibraryHasValue, OptionFlags.SetClientLibraryValue);
            Append(sb, OptionKeys.HighIntegrity, OptionFlags.HighIntegrityHasValue, OptionFlags.HighIntegrityValue);
            if (HasValue(OptionFlags.ProtocolHasValue)) Append(sb, OptionKeys.Protocol, FormatProtocol(_protocol));
            Append(sb, OptionKeys.TcpKeepAlive, OptionFlags.TcpKeepAliveHasValue, OptionFlags.TcpKeepAliveValue);
            if (Tunnel is { IsInbuilt: true } tunnel)
            {
                Append(sb, OptionKeys.Tunnel, tunnel.ToString());
            }
            commandMap?.AppendDeltas(sb);
            return sb.ToString();

            static string FormatProtocol(RedisProtocol protocol) => protocol switch {
                RedisProtocol.Resp2 => "resp2",
                RedisProtocol.Resp3 => "resp3",
                _ => protocol.ToString(),
            };
        }

        private static void Append(StringBuilder sb, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (sb.Length != 0) sb.Append(',');
                sb.Append(value);
            }
        }

        private static void Append(StringBuilder sb, string prefix, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendPrefix(sb, prefix);
                sb.Append(value);
            }
        }

        private static void Append(StringBuilder sb, string prefix, int value)
        {
            AppendPrefix(sb, prefix);
            sb.Append(Format.ToString(value));
        }

        private static void Append(StringBuilder sb, string prefix, bool value)
        {
            AppendPrefix(sb, prefix);
            sb.Append(value);
        }

        private static void Append(StringBuilder sb, string prefix, Version? value)
        {
            if (value is not null)
            {
                Append(sb, prefix, Format.ToString(value));
            }
        }

        private static void AppendPrefix(StringBuilder sb, string prefix)
        {
            if (sb.Length != 0) sb.Append(',');
            if (!string.IsNullOrEmpty(prefix))
            {
                sb.Append(prefix).Append('=');
            }
        }

        private void Append(StringBuilder sb, string prefix, OptionFlags hasValue, OptionFlags valueFlag)
        {
            if (HasValue(hasValue))
            {
                Append(sb, prefix, IsSet(valueFlag));
            }
        }

        private void Append(StringBuilder sb, string prefix, OptionFlags hasValue, in int value)
        {
            if (HasValue(hasValue))
            {
                Append(sb, prefix, value);
            }
        }

        private void Append(StringBuilder sb, string prefix, OptionFlags hasValue, in Proxy value)
        {
            if (HasValue(hasValue))
            {
                Append(sb, prefix, value.ToString());
            }
        }

        private void Clear()
        {
            defaultOptions = null;
            optionFlags = OptionFlags.None;
            ClientName = ServiceName = user = password = tieBreaker = sslHost = configChannel = null;
            keepAlive = syncTimeout = asyncTimeout = connectTimeout = responseTimeout = connectRetry = configCheckSeconds = defaultDatabase = 0;
            sslProtocols = default;
            defaultVersion = null;
            heartbeatInterval = default;
            EndPoints.Clear();
            commandMap = null;
            proxy = default;
            reconnectRetryPolicy = null;
            backlogPolicy = null;
            loggerFactory = null;

            CertificateSelection = null;
            CertificateValidation = null;
            RequestBufferOptions = null;
            ResponseBufferOptions = null;
            ResponseArrayPool = null;
            BeforeSocketConnect = null;
            ChannelPrefix = default;
            LibraryName = null;
#pragma warning disable CS0618 // Type or member is obsolete
            SocketManager = null;
#pragma warning restore CS0618 // Type or member is obsolete
#if NET
            SslClientAuthenticationOptions = null;
#endif
            Tunnel = null;
            _protocol = default;
            WriteMode = default;
#if DEBUG
            OutputLog = null;
#endif
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
                            ChannelPrefix = RedisChannel.Literal(value);
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
                            user = value;
                            break;
                        case OptionKeys.Password:
                            password = value;
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
                        case OptionKeys.SetClientLibrary:
                            SetClientLibrary = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.HighIntegrity:
                            HighIntegrity = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.TcpKeepAlive:
                            TcpKeepAlive = OptionKeys.ParseBoolean(key, value);
                            break;
                        case OptionKeys.Tunnel:
                            if (value.IsNullOrWhiteSpace())
                            {
                                Tunnel = null;
                            }
                            else
                            {
                                // For backwards compatibility with `http:address_with_port`.
                                if (value.StartsWith("http:") && !value.StartsWith("http://"))
                                {
                                    value = value.Insert(5, "//");
                                }

                                var uri = new Uri(value, UriKind.Absolute);
                                if (uri.Scheme != "http")
                                {
                                    throw new ArgumentException("Tunnel cannot be parsed: " + value);
                                }
                                if (!Format.TryParseEndPoint($"{uri.Host}:{uri.Port}", out var ep))
                                {
                                    throw new ArgumentException("HTTP tunnel cannot be parsed: " + value);
                                }
                                Tunnel = Tunnel.HttpProxy(ep);
                            }
                            break;
                        case OptionKeys.Protocol:
                            SetWithValue(OptionFlags.ProtocolHasValue, ref _protocol, OptionKeys.ParseRedisProtocol(key, value));
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

        /// <summary>
        /// Allows custom transport implementations, such as http-tunneling via a proxy.
        /// </summary>
        public Tunnel? Tunnel { get; set; }

        /// <summary>
        /// Specify the redis protocol type.
        /// </summary>
        public RedisProtocol? Protocol
        {
            get => HasValue(OptionFlags.ProtocolHasValue) ? _protocol : Defaults.Protocol;
            set => Set(OptionFlags.ProtocolHasValue, ref _protocol, value);
        }

        internal BufferedStreamWriter.WriteMode WriteMode { get; set; }
        internal bool AllowSimulateConnectionFailure
        {
            get => IsSet(OptionFlags.AllowSimulateConnectionFailure);
            set => SetFlag(OptionFlags.AllowSimulateConnectionFailure, value);
        } // for testing; **only** available via internal API

#if DEBUG
        internal Action<string>? OutputLog;
#endif
        internal bool TryResp3()
        {
            // if Protocol specified: fine, otherwise lean on the server version
            var protocol = Protocol;
            bool use3 = protocol is null
                ? new RedisFeatures(DefaultVersion).Resp3
                : protocol.GetValueOrDefault() >= RedisProtocol.Resp3;
            // either way, it requires HELLO
            return use3 && CommandMap.IsAvailable(RedisCommand.HELLO);
        }

        internal static bool TryParseRedisProtocol(string? value, out RedisProtocol protocol)
        {
            // accept raw integers too, but only trust them if we recognize them
            // (note we need to do this before enums, because Enum.TryParse will
            // accept integers as the raw value, which is not what we want here)
            if (value is not null)
            {
                if (Format.TryParseInt32(value, out int i32))
                {
                    switch (i32)
                    {
                        case 2:
                            protocol = RedisProtocol.Resp2;
                            return true;
                        case 3:
                            protocol = RedisProtocol.Resp3;
                            return true;
                    }
                }
                else
                {
                    if (Enum.TryParse(value, true, out protocol)) return true;
                }
            }
            protocol = default;
            return false;
        }
    }
}
