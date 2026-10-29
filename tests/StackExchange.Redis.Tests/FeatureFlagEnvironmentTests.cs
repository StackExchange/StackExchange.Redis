using System.Collections.Generic;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>Feature flag <c>Foo</c> can be set from the environment as <c>SEREDIS_FOO</c>.</summary>
[Collection(NonParallelCollection.Name)]
public class FeatureFlagEnvironmentTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" YES ", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    public void AFlagFollowsItsVariable(string value, bool expected)
    {
        var original = ConnectionMultiplexer.GetFeatureFlag("SingleReadLoop");
        try
        {
            ConnectionMultiplexer.SetFeatureFlag("SingleReadLoop", !expected);
            ConnectionMultiplexer.ApplyEnvironmentFeatureFlags(name => name == "SEREDIS_SINGLEREADLOOP" ? value : null);
            Assert.Equal(expected, ConnectionMultiplexer.GetFeatureFlag("SingleReadLoop"));
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag("SingleReadLoop", original);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("maybe")]
    public void AnAbsentOrUnreadableValueLeavesTheFlagAlone(string? value)
    {
        var original = ConnectionMultiplexer.GetFeatureFlag("SingleReadLoop");
        try
        {
            foreach (var state in new[] { true, false })
            {
                ConnectionMultiplexer.SetFeatureFlag("SingleReadLoop", state);
                ConnectionMultiplexer.ApplyEnvironmentFeatureFlags(name => name == "SEREDIS_SINGLEREADLOOP" ? value : null);
                Assert.Equal(state, ConnectionMultiplexer.GetFeatureFlag("SingleReadLoop"));
            }
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag("SingleReadLoop", original);
        }
    }

    [Fact]
    public void EveryFlagIsLookedUpByItsUpperCaseName()
    {
        var asked = new List<string>();
        ConnectionMultiplexer.ApplyEnvironmentFeatureFlags(name =>
        {
            asked.Add(name);
            return null;
        });

        Assert.Contains("SEREDIS_DEDICATEDTHREADS", asked);
        Assert.Contains("SEREDIS_PREVENTTHREADTHEFT", asked);
        Assert.Contains("SEREDIS_SINGLEREADLOOP", asked);
        Assert.DoesNotContain("SEREDIS_NONE", asked);
    }
}
