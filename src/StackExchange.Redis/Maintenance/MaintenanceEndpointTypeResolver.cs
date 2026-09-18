using System;
using System.Net;
using System.Net.Sockets;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// Chooses which <c>moving-endpoint-type</c> to ask a server for.
/// </summary>
/// <remarks>
/// Two questions, answered independently. *Scope* comes from the address we are actually connected to: a private
/// or otherwise reserved address means we are inside the deployment's network and want the internal forms.
/// *Form* comes from whether the connection is encrypted: TLS implies the FQDN variants, because a certificate
/// generally cannot be validated against a bare address - so a client handed an IP mid-handoff would be unable
/// to verify the endpoint it was told to move to.
/// <para>
/// <code>
///            | private/reserved | otherwise
///  TLS off   | internal-ip      | external-ip
///  TLS on    | internal-fqdn    | external-fqdn
/// </code>
/// </para>
/// <para>
/// Classifying the *connected* address matters, rather than the configured endpoint: the latter is usually a
/// hostname, and what decides whether we are inside the network is where it resolved to. Where there is no
/// socket address at all - a tunnel, a custom transport, a Unix domain socket - the honest answer is
/// <see cref="MaintenanceEndpointType.None"/>: we cannot classify, so we ask for no address and reconnect the
/// way we originally connected.
/// </para>
/// </remarks>
internal static class MaintenanceEndpointTypeResolver
{
    /// <summary>
    /// Derives the endpoint type for a connection.
    /// </summary>
    internal static MaintenanceEndpointType Derive(IPAddress? connectedAddress, bool isEncrypted) =>
        connectedAddress is null
            ? MaintenanceEndpointType.None
            : IsPrivateOrReserved(connectedAddress)
                ? (isEncrypted ? MaintenanceEndpointType.InternalFqdn : MaintenanceEndpointType.InternalIp)
                : (isEncrypted ? MaintenanceEndpointType.ExternalFqdn : MaintenanceEndpointType.ExternalIp);

    /// <summary>
    /// Whether an address is private, or otherwise not routable on the public internet.
    /// </summary>
    /// <remarks>
    /// Covers RFC1918 (10/8, 172.16/12, 192.168/16), loopback, IPv4 link-local (169.254/16), IPv6 unique-local
    /// (fc00::/7), IPv6 loopback and link-local, and IPv4-mapped IPv6 - which has to be unwrapped first, or an
    /// address like <c>::ffff:10.0.0.1</c> classifies as public.
    /// <para>
    /// CGNAT (100.64/10) is deliberately *not* treated as private. It is a genuine judgement call: it is not
    /// publicly routable, but a client behind it is not inside the deployment's network either, which is the
    /// question being asked here. Worth confirming against how the server classifies it before changing.
    /// </para>
    /// </remarks>
    internal static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address is null) throw new ArgumentNullException(nameof(address));

        // unwrap ::ffff:a.b.c.d, so the IPv4 rules below actually apply to it
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true, // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31, // 172.16.0.0/12
                192 => bytes[1] == 168, // 192.168.0.0/16
                169 => bytes[1] == 254, // 169.254.0.0/16, link-local
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IsIPv6UniqueLocal only exists from .NET 6, and this library targets down to net461 - so fc00::/7
            // is tested by hand. IsIPv6LinkLocal and IsIPv6SiteLocal are available everywhere.
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;

            var v6 = address.GetAddressBytes();
            if ((v6[0] & 0xFE) == 0xFC) return true; // fc00::/7, unique-local
        }

        return false;
    }
}
