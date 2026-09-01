using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Which form of address a server should name when it tells us an endpoint is moving.
/// </summary>
/// <remarks>
/// Sent as the <c>moving-endpoint-type</c> parameter of the maintenance-notification opt-in, and it decides what
/// arrives in <see cref="Maintenance.PushMaintenanceEvent.NewEndPoint"/>.
/// <para>
/// Worth asking for rather than leaving to the server. Eleven observed <c>MOVING</c> notifications on Redis
/// Enterprise 8.0.22 all carried an explicit null, including ones where the server had already chosen the
/// replacement node - and every one of those was requested with a bare <c>ON</c>, so the working theory is that
/// the server default amounts to <see cref="None"/> and we were getting what we asked for.
/// </para>
/// <para>
/// The choice is not cosmetic where TLS is involved: a certificate that carries DNS names and no IP SAN cannot
/// validate an address, so a verifying client that is handed an IP cannot use it. Prefer the FQDN forms when
/// connecting with TLS.
/// </para>
/// </remarks>
[Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
public enum MaintenanceEndpointType
{
    /// <summary>
    /// Do not ask; let the server choose. This is the default, and matches what the client has always sent.
    /// </summary>
    /// <remarks>
    /// In practice this has been observed to mean "no address at all", so a client that wants a named
    /// replacement should ask for one explicitly.
    /// </remarks>
    ServerDefault = 0,

    /// <summary>
    /// Work out the right form per connection, and ask for it. The recommended setting.
    /// </summary>
    /// <remarks>
    /// Derived from two facts about the connection as established: whether the address we actually reached is
    /// private (so we want the internal forms) and whether the connection is encrypted (so we want the FQDN
    /// forms, since a certificate generally cannot be validated against a bare address). Where there is no
    /// socket address to classify - a tunnel, or a Unix domain socket - this resolves to
    /// <see cref="None"/> rather than guessing.
    /// </remarks>
    Auto,

    /// <summary>A private address, for a client inside the deployment's network.</summary>
    InternalIp,

    /// <summary>A private hostname, for a client inside the deployment's network.</summary>
    InternalFqdn,

    /// <summary>A public address. Note an address cannot be validated against a DNS-only certificate.</summary>
    ExternalIp,

    /// <summary>A public hostname. The right choice when connecting with TLS.</summary>
    ExternalFqdn,

    /// <summary>
    /// Explicitly ask for no address, so a handoff always goes back through the endpoint as configured.
    /// </summary>
    None,
}
