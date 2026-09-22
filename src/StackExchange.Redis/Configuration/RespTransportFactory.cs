using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>Connect, and hand back something that can be written to.</summary>
        /// <param name="endpoint">The logical endpoint being connected to.</param>
        /// <param name="config">The configuration carrying the tunnel and TLS intent.</param>
        /// <param name="connectionType">Which connection this is; tunnels are told.</param>
        /// <param name="onAuthSuspect">Told when a TLS failure looks like an authentication problem.</param>
        /// <param name="cancellationToken">Cancels the connect attempt.</param>
        internal static async Task<DuplexTransport> ConnectAsync(
            EndPoint endpoint,
            ConfigurationOptions config,
            ConnectionType connectionType,
            Action<Exception>? onAuthSuspect = null,
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

                    return owned;
                }
            }

            // ...otherwise the tunnel only says WHERE to dial, which is how a proxy is expressed
            var connectTo = tunnel is null
                ? endpoint
                : await tunnel.GetSocketConnectEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false) ?? endpoint;

            // SocketManager.CreateSocket, not a hand-rolled socket: it picks the address family from the
            // endpoint, uses ProtocolType.Unspecified for a unix domain socket rather than Tcp, sets
            // NoDelay only where that is meaningful, and applies TCP keep-alive when configured. The
            // hand-rolled version this replaced said "Stream, Tcp, NoDelay=true" unconditionally, which
            // cannot connect to a unix socket at all.
#pragma warning disable CS0618 // the TYPE is obsolete for public consumers; this static is the live path
            var socket = SocketManager.CreateSocket(connectTo, config.TcpKeepAlive);
#pragma warning restore CS0618
            try
            {
                if (tunnel is not null)
                {
                    await tunnel.BeforeSocketConnectAsync(connectTo, connectionType, socket, cancellationToken).ConfigureAwait(false);
                }

                await ConnectSocketAsync(socket, connectTo).ConfigureAwait(false);

                // a tunnel may hand back its own stream over our socket - a SOCKS or CONNECT proxy does
                var stream = tunnel is null
                    ? null
                    : await tunnel.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken).ConfigureAwait(false);

                stream ??= new NetworkStream(socket, ownsSocket: true);

                if (config.Ssl)
                {
                    stream = await AuthenticateAsync(stream, endpoint, config, onAuthSuspect).ConfigureAwait(false);
                }

                return new StreamDuplexTransport(stream);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
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
            var host = config.SslHost;
            if (host.IsNullOrWhiteSpace()) host = Format.ToStringHostOnly(endpoint);

            var validate = config.CertificateValidationCallback ?? PhysicalConnection.GetAmbientIssuerCertificateCallback();
            var select = config.CertificateSelectionCallback ?? PhysicalConnection.GetAmbientClientCertificateCallback();
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
