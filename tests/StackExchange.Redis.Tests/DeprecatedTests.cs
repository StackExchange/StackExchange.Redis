using System;
using System.Reflection;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Testing that things we deprecate still parse, but are otherwise defaults.
/// </summary>
public class DeprecatedTests(ITestOutputHelper output) : TestBase(output)
{
    // note: everything under test here is [Obsolete(..., error: true)], so it cannot be named directly - not
    // even via nameof, and #pragma cannot suppress an error; reflection is the only way to reach these members
    private static PropertyInfo AssertObsoleteAsError(string name)
    {
        var property = typeof(ConfigurationOptions).GetProperty(name)
            ?? throw new MissingMemberException(nameof(ConfigurationOptions), name);
        var obsolete = property.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.True(obsolete.IsError, $"{name} should be obsolete as an error");
        return property;
    }

    private static T Get<T>(PropertyInfo property, ConfigurationOptions options) => (T)property.GetValue(options)!;

    [Fact]
    public void HighPrioritySocketThreads()
    {
        var property = AssertObsoleteAsError("HighPrioritySocketThreads");

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.False(Get<bool>(property, options));

        options = ConfigurationOptions.Parse("highPriorityThreads=true");
        Assert.Equal("", options.ToString());
        Assert.False(Get<bool>(property, options));

        options = ConfigurationOptions.Parse("highPriorityThreads=false");
        Assert.Equal("", options.ToString());
        Assert.False(Get<bool>(property, options));
    }

    [Fact]
    public void PreserveAsyncOrder()
    {
        var property = AssertObsoleteAsError("PreserveAsyncOrder");

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.False(Get<bool>(property, options));

        options = ConfigurationOptions.Parse("preserveAsyncOrder=true");
        Assert.Equal("", options.ToString());
        Assert.False(Get<bool>(property, options));

        options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
        Assert.Equal("", options.ToString());
        Assert.False(Get<bool>(property, options));
    }

    [Fact]
    public void WriteBufferParse()
    {
        var property = AssertObsoleteAsError("WriteBuffer");

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.Equal(0, Get<int>(property, options));

        options = ConfigurationOptions.Parse("writeBuffer=8092");
        Assert.Equal(0, Get<int>(property, options));
    }

    [Fact]
    public void ResponseTimeout()
    {
        var property = AssertObsoleteAsError("ResponseTimeout");

        var options = ConfigurationOptions.Parse("name=Hello");
        Assert.Equal(0, Get<int>(property, options));

        options = ConfigurationOptions.Parse("responseTimeout=1000");
        Assert.Equal(0, Get<int>(property, options));
    }
}
