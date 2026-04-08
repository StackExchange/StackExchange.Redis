using System;
using Xunit;

namespace StackExchange.Redis.Tests;

public class KnownRoleMetadataUnitTests
{
    [Theory]
    [InlineData("primary", false)]
    [InlineData("master", false)]
    [InlineData("replica", true)]
    [InlineData("slave", true)]
    public void TryParse_CharSpan_KnownRoles(string value, bool expected)
    {
        Assert.True(KnownRoleMetadata.TryParse(value.AsSpan(), out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParse_CharSpan_UnknownRole()
    {
        Assert.False(KnownRoleMetadata.TryParse("sentinel".AsSpan(), out _));
    }
}
