using System;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace StackExchange.Redis.Tests;

public static class Skip
{
    public static void UnlessLongRunning()
    {
        Assert.SkipUnless(TestConfig.Current.RunLongRunning, "Skipping long-running test");
    }

    public static void IfNoConfig(string prop, [NotNull] string? value)
    {
        Assert.SkipWhen(value.IsNullOrEmpty(), $"Config.{prop} is not set, skipping test.");
    }

    internal static void IfMissingDatabase(IConnectionMultiplexer conn, int dbId)
    {
        var dbCount = conn.GetServer(conn.GetEndPoints()[0]).DatabaseCount;
        Assert.SkipWhen(dbId >= dbCount, $"Database '{dbId}' is not supported on this server.");
    }
}

public class SkipTestException(string reason) : Exception(reason)
{
    public string? MissingFeatures { get; set; }
}
