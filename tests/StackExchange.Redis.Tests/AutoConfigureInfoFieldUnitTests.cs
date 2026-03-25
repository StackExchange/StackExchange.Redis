using System;
using Xunit;

namespace StackExchange.Redis.Tests;

public class AutoConfigureInfoFieldUnitTests
{
    [Theory]
    [InlineData("role", (int)AutoConfigureInfoField.Role)]
    [InlineData("master_host", (int)AutoConfigureInfoField.MasterHost)]
    [InlineData("master_port", (int)AutoConfigureInfoField.MasterPort)]
    [InlineData("redis_version", (int)AutoConfigureInfoField.RedisVersion)]
    [InlineData("redis_mode", (int)AutoConfigureInfoField.RedisMode)]
    [InlineData("run_id", (int)AutoConfigureInfoField.RunId)]
    [InlineData("garnet_version", (int)AutoConfigureInfoField.GarnetVersion)]
    [InlineData("valkey_version", (int)AutoConfigureInfoField.ValkeyVersion)]
    public void TryParse_CharSpan_KnownFields(string value, int expected)
    {
        Assert.True(AutoConfigureInfoFieldMetadata.TryParse(value.AsSpan(), out var actual));
        Assert.Equal(expected, (int)actual);
    }

    [Fact]
    public void TryParse_CharSpan_UnknownField()
    {
        Assert.False(AutoConfigureInfoFieldMetadata.TryParse("server_name".AsSpan(), out _));
    }
}
