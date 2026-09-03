using System.Net;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Deriving which <c>moving-endpoint-type</c> to ask for.
/// </summary>
/// <remarks>
/// Worth testing exhaustively because it decides what a server sends us during a handoff, and getting the
/// scope wrong means being handed an address we cannot reach while getting the form wrong means being handed one
/// we cannot validate.
/// </remarks>
public class MaintenanceEndpointTypeResolverTests
{
    [Theory]
    // private, so the internal forms; TLS decides ip versus fqdn
    [InlineData("10.0.0.1", false, MaintenanceEndpointType.InternalIp)]
    [InlineData("10.0.0.1", true, MaintenanceEndpointType.InternalFqdn)]
    // public, so the external forms
    [InlineData("34.253.226.6", false, MaintenanceEndpointType.ExternalIp)]
    [InlineData("34.253.226.6", true, MaintenanceEndpointType.ExternalFqdn)]
    public void ScopeComesFromTheAddressAndFormFromTls(string address, bool encrypted, MaintenanceEndpointType expected)
        => Assert.Equal(expected, MaintenanceEndpointTypeResolver.Derive(IPAddress.Parse(address), encrypted));

    [Fact]
    public void NoAddressMeansAskForNothing()
    {
        // A tunnel, a custom transport or a Unix domain socket gives us nothing to classify. "none" is the
        // honest answer: we ask for no address and reconnect the way we originally connected, rather than
        // guessing at a scope we cannot determine.
        Assert.Equal(MaintenanceEndpointType.None, MaintenanceEndpointTypeResolver.Derive(null, isEncrypted: false));
        Assert.Equal(MaintenanceEndpointType.None, MaintenanceEndpointTypeResolver.Derive(null, isEncrypted: true));
    }

    [Theory]
    [InlineData("10.0.0.1", true)]           // RFC1918 10/8
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]         // RFC1918 172.16/12 - lower edge
    [InlineData("172.31.255.254", true)]     // upper edge
    [InlineData("172.15.0.1", false)]        // just outside
    [InlineData("172.32.0.1", false)]        // just outside
    [InlineData("192.168.1.1", true)]        // RFC1918 192.168/16
    [InlineData("192.169.1.1", false)]       // adjacent, and public
    [InlineData("169.254.1.1", true)]        // link-local
    [InlineData("127.0.0.1", true)]          // loopback
    [InlineData("::1", true)]                // IPv6 loopback
    [InlineData("fe80::1", true)]            // IPv6 link-local
    [InlineData("fc00::1", true)]            // IPv6 unique-local, lower edge of fc00::/7
    [InlineData("fdff::1", true)]            // upper edge
    [InlineData("fe00::1", false)]           // outside fc00::/7
    [InlineData("::ffff:10.0.0.1", true)]    // IPv4-mapped private: must be unwrapped, or it reads as public
    [InlineData("::ffff:34.253.226.6", false)] // IPv4-mapped public
    [InlineData("2001:4860:4860::8888", false)] // public IPv6
    [InlineData("34.253.226.6", false)]      // public IPv4
    [InlineData("100.64.0.1", false)]        // CGNAT: deliberately *not* private - see the remarks
    public void ReservedRangesAreClassified(string address, bool expected)
        => Assert.Equal(expected, MaintenanceEndpointTypeResolver.IsPrivateOrReserved(IPAddress.Parse(address)));
}
