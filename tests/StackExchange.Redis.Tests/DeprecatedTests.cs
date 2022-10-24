using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Testing that things we deprecate still parse, but are otherwise defaults.
/// </summary>
public class DeprecatedTests : TestBase
{
    public DeprecatedTests(ITestOutputHelper output) : base(output) { }

#pragma warning disable CS0618 // Type or member is obsolete
    [Fact]
    public void HighPrioritySocketThreads()
    {
        Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.HighPrioritySocketThreads))!, typeof(ObsoleteAttribute)));

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.False(options.HighPrioritySocketThreads);

        options = ConfigurationOptions.Parse("highPriorityThreads=true");
        Assert.Equal("", options.ToString());
        Assert.False(options.HighPrioritySocketThreads);

        options = ConfigurationOptions.Parse("highPriorityThreads=false");
        Assert.Equal("", options.ToString());
        Assert.False(options.HighPrioritySocketThreads);
    }

    [Fact]
    public void PreserveAsyncOrder()
    {
        Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.PreserveAsyncOrder))!, typeof(ObsoleteAttribute)));

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.False(options.PreserveAsyncOrder);

        options = ConfigurationOptions.Parse("preserveAsyncOrder=true");
        Assert.Equal("", options.ToString());
        Assert.False(options.PreserveAsyncOrder);

        options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
        Assert.Equal("", options.ToString());
        Assert.False(options.PreserveAsyncOrder);
    }

    [Fact]
    public void WriteBufferParse()
    {
        Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.WriteBuffer))!, typeof(ObsoleteAttribute)));

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.Equal(0, options.WriteBuffer);

        options = ConfigurationOptions.Parse("writeBuffer=8092");
        Assert.Equal(0, options.WriteBuffer);
    }

    [Fact]
    public void ResponseTimeout()
    {
        Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.ResponseTimeout))!, typeof(ObsoleteAttribute)));

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.Equal(0, options.ResponseTimeout);

        options = ConfigurationOptions.Parse("responseTimeout=1000");
        Assert.Equal(0, options.ResponseTimeout);
    }
#pragma warning restore CS0618
}
