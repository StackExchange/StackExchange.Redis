using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace StackExchange.Redis.Configuration;

/// <summary>
/// A read-only view over just the TLS-relevant parts of a <see cref="ConfigurationOptions"/>, for
/// transports that own TLS themselves (see <see cref="Tunnel.ConnectTransportAsync"/>): a transport
/// cannot honour an intent it cannot see, but it has no business with the rest of the configuration.
/// </summary>
/// <remarks>
/// This wraps the configuration rather than copying it: the accessors proxy through, so the cost of
/// passing this around is one reference, and nothing here can mutate the configuration or reach
/// anything beyond TLS. A <c>default</c> instance reports "no TLS".
/// </remarks>
[Experimental(RESPite.Experiments.Transport, UrlFormat = RESPite.Experiments.UrlFormat)]
public readonly struct TlsOptions
{
    private readonly ConfigurationOptions? _options;
    private readonly bool _forceTls;

    /// <summary>
    /// Create a view over the TLS settings of the supplied configuration.
    /// </summary>
    public TlsOptions(ConfigurationOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), forceTls: false)
    {
    }

    private TlsOptions(ConfigurationOptions? options, bool forceTls)
    {
        _options = options;
        _forceTls = forceTls;
    }

    /// <summary>
    /// Whether TLS is required for this connection; corresponds to <see cref="ConfigurationOptions.Ssl"/>.
    /// A transport that returns <c>false</c> from
    /// <see cref="RESPite.Transports.DuplexTransport.IsEncrypted"/> when this is set will not be used.
    /// </summary>
    public bool IsEnabled => _forceTls || (_options?.Ssl ?? false);

    /// <summary>
    /// The same view, but with TLS required regardless of what the configuration says - for the intermediate
    /// tunnel that cleared <see cref="ConfigurationOptions.Ssl"/> for its own reasons but is chaining to a
    /// tail that owns the handshake. Everything else (host, protocols, callbacks) is unchanged.
    /// </summary>
    internal TlsOptions WithTls() => new(_options, forceTls: true);

    /// <summary>
    /// The configured TLS host name, used for SNI and certificate validation; corresponds to
    /// <see cref="ConfigurationOptions.SslHost"/>. Usually prefer <see cref="ResolveHost"/>, which
    /// applies the same endpoint fallback the library's own TLS path uses.
    /// </summary>
    public string? SslHost => _options?.SslHost;

    /// <summary>
    /// The permitted TLS protocol versions, or <c>null</c> for the platform default; corresponds to
    /// <see cref="ConfigurationOptions.SslProtocols"/>.
    /// </summary>
    public SslProtocols? SslProtocols => _options?.SslProtocols;

    /// <summary>
    /// Whether the certificate revocation list should be checked during authentication; corresponds to
    /// <see cref="ConfigurationOptions.CheckCertificateRevocation"/>.
    /// </summary>
    public bool CheckCertificateRevocation => _options?.CheckCertificateRevocation ?? false;

    /// <summary>
    /// The callback that validates the server certificate, or <c>null</c> for the platform default.
    /// </summary>
    /// <remarks>This includes the ambient environment fallback (<c>SERedis_IssuerCertPath</c>), so a
    /// transport that uses this behaves as the library's own TLS path does.</remarks>
    public RemoteCertificateValidationCallback? CertificateValidationCallback
        => _options is null ? null : _options.CertificateValidationCallback ?? PhysicalConnection.GetAmbientIssuerCertificateCallback();

    /// <summary>
    /// The callback that selects the client certificate, or <c>null</c> for the platform default.
    /// </summary>
    /// <remarks>This includes the ambient environment fallback (<c>SERedis_ClientCertPfxPath</c> etc), so
    /// a transport that uses this behaves as the library's own TLS path does.</remarks>
    public LocalCertificateSelectionCallback? CertificateSelectionCallback
        => _options is null ? null : _options.CertificateSelectionCallback ?? PhysicalConnection.GetAmbientClientCertificateCallback();

#if NET
    /// <summary>
    /// The caller-supplied authentication options for the given host, if any; corresponds to
    /// <see cref="ConfigurationOptions.SslClientAuthenticationOptions"/>. When this returns non-null it
    /// supersedes the individual values here, exactly as it does on the library's own TLS path.
    /// </summary>
    public SslClientAuthenticationOptions? GetSslClientAuthenticationOptions(string host)
        => _options?.SslClientAuthenticationOptions?.Invoke(host);
#endif

    /// <summary>
    /// The TLS host name to use for the given endpoint: the configured <see cref="SslHost"/> if there is
    /// one, otherwise the host portion of the endpoint - which is what the library's own TLS path does.
    /// </summary>
    public string ResolveHost(EndPoint endpoint)
    {
        var host = SslHost;
        return host.IsNullOrWhiteSpace() ? Format.ToStringHostOnly(endpoint) : host!;
    }
}
