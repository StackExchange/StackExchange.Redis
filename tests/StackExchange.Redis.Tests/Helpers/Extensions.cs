using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;

namespace StackExchange.Redis.Tests.Helpers;

public static class Extensions
{
    private static string VersionInfo { get; }

    static Extensions()
    {
        VersionInfo = $"Running under {RuntimeInformation.FrameworkDescription} ({Environment.Version})";
    }

    public static void WriteFrameworkVersion(this ITestOutputHelper output) => output.WriteLine(VersionInfo);

    public static ConfigurationOptions WithoutSubscriptions(this ConfigurationOptions options)
    {
        options.CommandMap = CommandMap.Create(new HashSet<string>() { nameof(RedisCommand.SUBSCRIBE) }, available: false);
        return options;
    }
}
