using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Parsing the raw text of <c>CLIENT LIST</c>, independent of any server.
/// </summary>
public class ClientInfoParseUnitTests
{
    [Fact]
    public void UnbracketedIPv6AddressIsParsed()
    {
        // the server does not bracket IPv6 in "addr", so the port is the text after the *last* colon (#1422)
        const string Raw = "id=8897064 addr=fd40:8017:11:7032:7011:600:ac18:bc:54265 fd=60 name=i-230510-t age=1548 idle=40 flags=P db=0 sub=2 psub=0 multi=-1 qbuf=0 qbuf-free=0 argv-mem=0 obl=0 oll=0 omem=0 tot-mem=20496 ow=0 owmem=0 events=r cmd=ping user=default numops=31\n";

        Assert.True(ClientInfo.TryParse(Raw, out var clients));
        var client = Assert.Single(clients);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("fd40:8017:11:7032:7011:600:ac18:bc"), 54265), client.Address);
        Assert.Equal(8897064, client.Id);
        Assert.Equal("i-230510-t", client.Name);
    }

    [Fact]
    public void IPv4AddressIsParsed()
    {
        const string Raw = "id=3 addr=127.0.0.1:52555 fd=8 name= age=855 idle=0 flags=N db=0 sub=0 psub=0 multi=-1 qbuf=0 qbuf-free=0 obl=0 oll=0 omem=0 events=r cmd=client\n";

        Assert.True(ClientInfo.TryParse(Raw, out var clients));
        var client = Assert.Single(clients);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 52555), client.Address);
    }
}
