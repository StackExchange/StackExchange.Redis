using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class FeatureFlagTests
{
    [Fact]
    public void UnknownFlagToggle()
    {
        Assert.False(ConnectionMultiplexer.GetFeatureFlag("nope"));
        ConnectionMultiplexer.SetFeatureFlag("nope", true);
        Assert.False(ConnectionMultiplexer.GetFeatureFlag("nope"));
    }

    [Fact]
    public void KnownFlagToggle()
    {
        Assert.False(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
        ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true);
        Assert.True(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
        ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", false);
        Assert.False(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
    }

    [Fact]
    public void InlineSendsIsOnByDefault()
    {
        // the environment overrides the default (SEREDIS_INLINESENDS=0 turns it off), so only assert without it
        if (System.Environment.GetEnvironmentVariable("SEREDIS_INLINESENDS") is not null) return;
        Assert.True(ConnectionMultiplexer.GetFeatureFlag("inlinesends"));
    }
}
