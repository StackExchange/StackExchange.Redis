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
    /// The cluster REST API to ask; when <see langword="null"/> (the default), <c>https://{host}:9443/</c>,
    /// where <c>{host}</c> is the member's first configured endpoint.
    /// </summary>
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
