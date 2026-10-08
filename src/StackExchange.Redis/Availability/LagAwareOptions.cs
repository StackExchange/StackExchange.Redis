using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configures a health check against the Redis Enterprise database availability API, optionally asking
/// whether the database has caught up with its Active-Active peers; see <see cref="HealthCheck.LagAware"/>.
/// </summary>
/// <remarks>
/// Only <see cref="Credentials"/> is required. The REST endpoint, the database id and whether the database is
/// an Active-Active member are discovered, and no lag tolerance is sent unless one is set, so the cluster's
/// own setting applies. The options are read when the probe is created; later changes have no effect on it.
/// </remarks>
[Experimental(Experiments.LagAwareFailover, UrlFormat = Experiments.UrlFormat)]
public sealed class LagAwareOptions
{
    /// <summary>
    /// Supplies the cluster management credentials, called for every request so that they can rotate.
    /// </summary>
    /// <remarks>
    /// These are the cluster's REST API credentials, not the database's. A role with
    /// <c>management: db_viewer</c> is sufficient.
    /// </remarks>
    public Func<CancellationToken, ValueTask<NetworkCredential>>? Credentials { get; set; }

    /// <summary>
    /// The cluster REST API to ask; when <see langword="null"/> (the default), derived from the member's first
    /// configured endpoint: <c>https://{cluster}:9443/</c> for a database named <c>redis-{port}.{cluster}</c> (the
    /// form Redis Enterprise assigns), else <c>https://{host}:9443/</c>.
    /// </summary>
    /// <remarks>
    /// The cluster name matters: only the cluster's master answers every route the probe uses, and other nodes
    /// redirect to its internal address. Redirects are never followed; set this explicitly if the database is
    /// reached through a name that does not follow the pattern.
    /// </remarks>
    public Uri? RestEndpoint { get; set; }

    /// <summary>
    /// The database id (<c>uid</c>) on that cluster; when <see langword="null"/> (the default), it is found by
    /// matching the member's first configured endpoint against the cluster's databases.
    /// </summary>
    public int? DatabaseId { get; set; }

    /// <summary>
    /// Whether to ask the lag question; <see cref="LagCheckMode.Auto"/> (the default) asks it only of
    /// Active-Active databases, since the server reports any other database as unavailable when asked.
    /// </summary>
    public LagCheckMode LagCheck { get; set; }

    /// <summary>
    /// The lag tolerance to request; when <see langword="null"/> (the default), none is sent and the cluster's
    /// own <c>availability_lag_tolerance_ms</c> applies.
    /// </summary>
    public TimeSpan? LagTolerance { get; set; }

    /// <summary>
    /// Validates the REST API's certificate; when <see langword="null"/>, the platform's default validation
    /// applies.
    /// </summary>
    /// <remarks>
    /// The management certificate of a Redis Enterprise cluster is self-signed by the cluster unless one has
    /// been installed, so this is usually needed; see <see cref="TrustIssuer(string)"/>. Not supported on .NET
    /// Framework 4.6.1.
    /// </remarks>
    public RemoteCertificateValidationCallback? CertificateValidation { get; set; }

    /// <summary>
    /// Trust certificates issued by the given certificate (which may be the cluster's own self-signed
    /// certificate), as <see cref="ConfigurationOptions.TrustIssuer(string)"/> does for the data plane.
    /// </summary>
    public void TrustIssuer(string issuerCertificatePath)
        => CertificateValidation = ConfigurationOptions.TrustIssuerCallback(issuerCertificatePath);

    /// <summary>
    /// Trust certificates issued by the given certificate (which may be the cluster's own self-signed
    /// certificate), as <see cref="ConfigurationOptions.TrustIssuer(X509Certificate2)"/> does for the data plane.
    /// </summary>
    public void TrustIssuer(X509Certificate2 issuer)
        => CertificateValidation = ConfigurationOptions.TrustIssuerCallback(issuer);

    /// <summary>
    /// A client certificate to present to the REST API, for clusters that require mutual TLS on it; when
    /// <see langword="null"/> (the default), none. Not supported on .NET Framework 4.6.1.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; set; }

#if NET
    /// <summary>
    /// Present a client certificate loaded from a PEM file pair, as
    /// <see cref="ConfigurationOptions.SetUserPemCertificate(string, string?)"/> does for the data plane.
    /// </summary>
    /// <param name="userCertificatePath">The path for the user certificate (commonly a .crt file).</param>
    /// <param name="userKeyPath">The path for the user key (commonly a .key file).</param>
    public void SetUserPemCertificate(string userCertificatePath, string? userKeyPath = null)
        => ClientCertificate = ConfigurationOptions.LoadPemUserCertificate(userCertificatePath, userKeyPath);
#endif

    /// <summary>
    /// Present a client certificate loaded from a PFX file, as
    /// <see cref="ConfigurationOptions.SetUserPfxCertificate(string, string?)"/> does for the data plane.
    /// </summary>
    /// <param name="userCertificatePath">The path for the user certificate (commonly a .pfx file).</param>
    /// <param name="password">The password for the certificate file.</param>
    public void SetUserPfxCertificate(string userCertificatePath, string? password = null)
        => ClientCertificate = ConfigurationOptions.LoadPfxUserCertificate(userCertificatePath, password);

    // test seam: replaces the HTTP transport entirely, so tests can answer requests without a listener
    internal Func<HttpMessageHandler>? HttpMessageHandlerFactory { get; set; }
}

/// <summary>
/// Whether a <see cref="HealthCheck.LagAware"/> check asks the lag question.
/// </summary>
[Experimental(Experiments.LagAwareFailover, UrlFormat = Experiments.UrlFormat)]
public enum LagCheckMode
{
    /// <summary>
    /// Ask only if the database is an Active-Active member; otherwise check plain availability.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always ask. A database that is not an Active-Active member is then always reported unavailable.
    /// </summary>
    Enabled = 1,

    /// <summary>
    /// Never ask; check plain availability only.
    /// </summary>
    Disabled = 2,
}
