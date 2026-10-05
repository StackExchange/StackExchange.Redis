using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RESPite.Transports;
using StackExchange.Redis.Configuration;

namespace StackExchange.Redis
{
    /// <summary>
    /// Establishes the transport for one connection: tunnel, socket, proxy, and TLS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One place, because there is one right answer and it is not obvious.</b> Getting from an endpoint
    /// to something you can write bytes at means asking the tunnel whether it owns the whole transport,
    /// then where to dial, then opening a socket, then letting the tunnel wrap it, and only then applying
    /// TLS - and the TLS step alone carries the host to verify against, two callback pairs including
    /// ambient ones, the protocol set and the revocation check.
    /// </para>
    /// <para>
    /// <b>The new core did none of this.</b> It opened a bare socket at the endpoint, which silently
    /// ignored proxies, tunnels and <c>ConfigurationOptions.Ssl</c>. Adding TLS to it directly would have
    /// produced a second copy of security-critical code free to drift from the first, which is the
    /// opposite of what replacing a core is for - hence a factory rather than a fix in place.
    /// </para>
    /// <para>
    /// <see cref="DuplexTransport"/> is the boundary deliberately: a buffer writer with a synchronous
    /// flush and a receiver. Everything below it - sockets, streams, encryption - belongs here, and
    /// everything above it is protocol.
    /// </para>
    /// </remarks>
    internal static class RespTransportFactory
    {
        /// <summary>A connected transport, and the two facts about HOW it connected that outlive the socket.</summary>
        /// <param name="transport">The transport itself.</param>
        /// <param name="remoteAddress">The address actually reached, when it was an IP one.</param>
        /// <param name="isEncrypted">Whether the stream ended up encrypted.</param>
        /// <remarks>
        /// <b>Reported rather than inferred, because only the factory knows.</b> The address REACHED is not
        /// the endpoint dialled - that is usually a name - and whether the result is encrypted can be the
        /// tunnel's decision rather than the configuration's. Both are needed to classify this connection
        /// for <c>moving-endpoint-type</c>, which decides which address a server announces when it moves
        /// a shard: guessing it internal when it is external sends a client somewhere it cannot reach.
        /// </remarks>
        internal readonly struct ConnectedTransport(DuplexTransport transport, IPAddress? remoteAddress, bool isEncrypted)
        {
            internal DuplexTransport Transport { get; } = transport;

            internal IPAddress? RemoteAddress { get; } = remoteAddress;

            internal bool IsEncrypted { get; } = isEncrypted;
        }

        /// <summary>Connect, and hand back something that can be written to.</summary>
        /// <param name="endpoint">The logical endpoint being connected to.</param>
        /// <param name="config">The configuration carrying the tunnel and TLS intent.</param>
        /// <param name="connectionType">Which connection this is; tunnels are told.</param>
        /// <param name="onAuthSuspect">Told when a TLS failure looks like an authentication problem.</param>
        /// <param name="log">The connect log, told about TLS: when it starts, what it negotiated, and why it failed.</param>
        /// <param name="cancellationToken">Cancels the connect attempt.</param>
        internal static async Task<ConnectedTransport> ConnectAsync(
            EndPoint endpoint,
            ConfigurationOptions config,
            ConnectionType connectionType,
            Action<Exception>? onAuthSuspect = null,
            ILogger? log = null,
            CancellationToken cancellationToken = default)
        {
            var tunnel = config.Tunnel;

            // widest form first: a tunnel may own connect AND TLS end to end, in which case no socket is
            // created at all. It must then report encryption itself - the library cannot apply an intent
            // to a transport it did not build, so a mismatch is failed rather than assumed.
            if (tunnel is not null)
            {
#pragma warning disable SER004 // the transport hook is experimental; this is one of its two callers
                var owned = await tunnel.ConnectTransportAsync(
                    endpoint, connectionType, new TlsOptions(config), cancellationToken).ConfigureAwait(false);
#pragma warning restore SER004

                if (owned is not null)
                {
                    if (config.Ssl && !owned.IsEncrypted)
                    {
                        await owned.DisposeAsync().ConfigureAwait(false);
                        throw new RedisConnectionException(
                            ConnectionFailureType.AuthenticationFailure,
                            CommandFlags.CommandRetryNever,
                            "TLS was requested, but the transport supplied by the tunnel is not encrypted.");
                    }

                    // the tunnel owns the whole transport, so it is also the only thing that knows
                    // whether it is encrypted - and there is no socket here to read an address from
                    return new ConnectedTransport(owned, null, owned.IsEncrypted);
                }
            }

            // ...otherwise the tunnel only says WHERE to dial, which is how a proxy is expressed - and
            // NULL there means "do not dial at all", not "dial the original endpoint". A tunnel that
            // supplies the whole connection itself answers null here and hands back a stream from
            // BeforeAuthenticateAsync; coalescing to the endpoint instead sends us to an address that
            // exists only as a name, which is exactly what an in-process server is
            EndPoint? connectTo = endpoint;
            if (tunnel is not null)
            {
                connectTo = await tunnel.GetSocketConnectEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            // SocketManager.CreateSocket, not a hand-rolled socket: it picks the address family from the
            // endpoint, uses ProtocolType.Unspecified for a unix domain socket rather than Tcp, sets
            // NoDelay only where that is meaningful, and applies TCP keep-alive when configured. The
            // hand-rolled version this replaced said "Stream, Tcp, NoDelay=true" unconditionally, which
            // cannot connect to a unix socket at all.
#pragma warning disable CS0618 // the TYPE is obsolete for public consumers; this static is the live path
            var socket = connectTo is null ? null : SocketManager.CreateSocket(connectTo, config.TcpKeepAlive);
#pragma warning restore CS0618
            try
            {
                if (socket is not null)
                {
                    // the ORIGINAL endpoint, not the one being dialled: the hook is told which server this
                    // connection is for, and a proxy address is not it
                    config.BeforeSocketConnect?.Invoke(endpoint, connectionType, socket);
                    if (tunnel is not null)
                    {
                        await tunnel.BeforeSocketConnectAsync(endpoint, connectionType, socket, cancellationToken).ConfigureAwait(false);
                    }

                    await ConnectSocketAsync(socket, connectTo!).ConfigureAwait(false);
                }

                // a tunnel may hand back its own stream - over our socket for a SOCKS or CONNECT proxy,
                // or instead of one entirely
                var stream = tunnel is null
                    ? null
                    : await tunnel.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken).ConfigureAwait(false);

                if (stream is null)
                {
                    if (socket is null)
                    {
                        throw new RedisConnectionException(
                            ConnectionFailureType.UnableToConnect,
                            CommandFlags.CommandRetryNever,
                            "The tunnel declined to connect a socket and supplied no stream of its own.");
                    }

                    stream = new NetworkStream(socket, ownsSocket: true);
                }

                var encrypted = config.Ssl;
                if (encrypted)
                {
                    // the same events, under the same ids, as the shipped connection: a TLS problem is the
                    // commonest thing a connect log is pasted into an issue to diagnose
                    log?.LogInformationConfiguringTLS();
                    var ssl = await AuthenticateAsync(
                        stream,
                        endpoint,
                        config,
                        ex =>
                        {
                            onAuthSuspect?.Invoke(ex);
                            log?.LogErrorConnectionIssue(ex, ex.Message);
                        }).ConfigureAwait(false);
#if NET
                    log?.LogInformationTLSConnectionEstablished(ssl.SslProtocol, ssl.NegotiatedCipherSuite);
#else
                    log?.LogInformationTLSConnectionEstablished(ssl.SslProtocol);
#endif
                    stream = ssl;
                }

                return new ConnectedTransport(
                    new StreamDuplexTransport(stream),
                    (socket?.RemoteEndPoint as IPEndPoint)?.Address,
                    encrypted);
            }
            catch
            {
                socket?.Dispose();
                throw;
            }
        }

        /// <summary>The issuer to trust from <c>SERedis_IssuerCertPath</c>, when the environment names one.</summary>
        internal static RemoteCertificateValidationCallback? GetAmbientIssuerCertificateCallback()
        {
            try
            {
                var issuerPath = Environment.GetEnvironmentVariable("SERedis_IssuerCertPath");
                if (!string.IsNullOrEmpty(issuerPath)) return ConfigurationOptions.TrustIssuerCallback(issuerPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }

        /// <summary>The client certificate from <c>SERedis_ClientCertPfxPath</c>, when the environment names one.</summary>
        internal static LocalCertificateSelectionCallback? GetAmbientClientCertificateCallback()
        {
            try
            {
                var certificatePath = Environment.GetEnvironmentVariable("SERedis_ClientCertPfxPath");
                if (!string.IsNullOrEmpty(certificatePath) && File.Exists(certificatePath))
                {
                    var password = Environment.GetEnvironmentVariable("SERedis_ClientCertPassword");
                    var pfxStorageFlags = Environment.GetEnvironmentVariable("SERedis_ClientCertStorageFlags");
                    X509KeyStorageFlags storageFlags = X509KeyStorageFlags.DefaultKeySet;
                    if (!string.IsNullOrEmpty(pfxStorageFlags) && Enum.TryParse<X509KeyStorageFlags>(pfxStorageFlags, true, out var typedFlags))
                    {
                        storageFlags = typedFlags;
                    }

                    return ConfigurationOptions.CreatePfxUserCertificateCallback(certificatePath, password, storageFlags);
                }

#if NET
                certificatePath = Environment.GetEnvironmentVariable("SERedis_ClientCertPemPath");
                if (!string.IsNullOrEmpty(certificatePath) && File.Exists(certificatePath))
                {
                    var passwordPath = Environment.GetEnvironmentVariable("SERedis_ClientCertPasswordPath");
                    return ConfigurationOptions.CreatePemUserCertificateCallback(certificatePath, passwordPath);
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }

        /// <summary>Wrap a stream in TLS: construct the <see cref="SslStream"/> and complete the handshake.</summary>
        /// <param name="stream">The stream to encrypt.</param>
        /// <param name="endpoint">Stands in for the host to verify against when none is configured.</param>
        /// <param name="config">Carries the host, callbacks, protocols and revocation setting.</param>
        /// <param name="onAuthSuspect">Told when the handshake fails, before the exception propagates.</param>
        /// <remarks>
        /// <para>
        /// <b>The handshake and nothing else</b>, deliberately: it does not dispose on failure and does not
        /// record anything. Both callers already have their own cleanup and their own idea of how a
        /// connection failure should be reported - the shipped path maps it onto
        /// <c>RecordConnectionFailed</c> and logs what was negotiated - and folding that in here would
        /// force one of them to change behaviour to share a decision they agree on anyway.
        /// </para>
        /// <para>
        /// What IS shared is the part worth having one copy of: which callbacks apply, that the ambient
        /// ones are the fallback, <see cref="EncryptionPolicy.RequireEncryption"/>, and which overload of
        /// <c>AuthenticateAsClientAsync</c> is used on which target.
        /// </para>
        /// </remarks>
        internal static async Task<SslStream> AuthenticateAsync(
            Stream stream, EndPoint endpoint, ConfigurationOptions config, Action<Exception>? onAuthSuspect)
        {
            // ConfigurationOptions.ResolveTlsHostName, not a second copy of the rule. This used to read
            // `SslHost, or the endpoint's host if that is blank`, which is what the shipped connection
            // used to do and what #3250 replaced: an endpoint that already carries a DNS name must use
            // ITS name for SNI, and only a non-DNS endpoint falls back to inferring one from the
            // configured set. Cluster shards exposed over TLS/SNI are the case that breaks otherwise -
            // every shard presented with the same inferred host, so every shard but one fails
            // validation. Two copies of a security decision is the drift this boundary exists to avoid.
            var host = config.ResolveTlsHostName(endpoint);

            var validate = config.CertificateValidationCallback ?? GetAmbientIssuerCertificateCallback();
            var select = config.CertificateSelectionCallback ?? GetAmbientClientCertificateCallback();
            var ssl = new SslStream(stream, false, validate, select, EncryptionPolicy.RequireEncryption);

            try
            {
#if NET
                var options = config.SslClientAuthenticationOptions?.Invoke(host!);
                if (options is not null)
                {
                    await ssl.AuthenticateAsClientAsync(options).ConfigureAwait(false);
                }
                else
                {
                    await ssl.AuthenticateAsClientAsync(host!, config.SslProtocols, config.CheckCertificateRevocation).ConfigureAwait(false);
                }
#else
                await ssl.AuthenticateAsClientAsync(host!, config.SslProtocols, config.CheckCertificateRevocation).ConfigureAwait(false);
#endif
            }
            catch (Exception ex)
            {
                // "the handshake failed" is far less useful than "the handshake failed and it looks like
                // an auth problem"; the caller decides what to do with the exception itself
                onAuthSuspect?.Invoke(ex);
                throw;
            }

            return ssl;
        }

        private static Task ConnectSocketAsync(Socket socket, EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => socket.ConnectAsync(dns.Host, dns.Port),
            _ => socket.ConnectAsync(endpoint),
        };
    }
}
