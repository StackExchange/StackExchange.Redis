using System;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ServerTypeMetadataUnitTests
{
    [Theory]
    [InlineData("standalone", (int)ServerType.Standalone)]
    [InlineData("cluster", (int)ServerType.Cluster)]
    [InlineData("sentinel", (int)ServerType.Sentinel)]
    public void TryParse_CharSpan_KnownServerTypes(string value, int expected)
    {
        Assert.True(ServerTypeMetadata.TryParse(value.AsSpan(), out var actual));
        Assert.Equal(expected, (int)actual);
    }

    [Theory]
    [InlineData("twemproxy")]
    [InlineData("envoyproxy")]
    public void TryParse_CharSpan_IgnoresNonAutoConfiguredTypes(string value)
    {
        Assert.False(ServerTypeMetadata.TryParse(value.AsSpan(), out _));
    }
}
