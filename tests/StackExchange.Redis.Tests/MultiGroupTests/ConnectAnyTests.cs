using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

/// <summary>
/// <see cref="ConnectionMultiplexer.ConnectAny(string, System.IO.TextWriter?)"/> routes a single-group string to an
/// ordinary connection and a multi-group string (<c>,|,</c> delimited) to an Active-Active group, returning the
/// shared <see cref="IConnectionMultiplexer"/> abstraction either way. These assert routing only (no live server is
/// required), using unreachable endpoints with <c>abortConnect=false</c>.
/// </summary>
public class ConnectAnyTests
{
    [Fact]
    public async Task MultiGroupStringConnectsAsAGroup()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAnyAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,weight=2,member=Germany" +
            ",|," +
            "127.0.0.1:2,abortConnect=false,connectTimeout=200,weight=9,member=Canada");

        Assert.IsType<MultiGroupMultiplexer>(conn);
        var group = Assert.IsAssignableFrom<IConnectionGroup>(conn);

        var members = group.GetMembers();
        Assert.Equal(2, members.Length);
        Assert.Equal("Germany", members[0].Name);
        Assert.Equal(2d, members[0].Weight);
        Assert.Equal("Canada", members[1].Name);
        Assert.Equal(9d, members[1].Weight);
    }

    [Fact]
    public async Task SingleGroupStringConnectsAsAnOrdinaryConnection()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAnyAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200");

        Assert.IsNotType<MultiGroupMultiplexer>(conn);
        Assert.False(conn is IConnectionGroup);
    }
}
