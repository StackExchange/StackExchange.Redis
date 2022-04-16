using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis.Tests;

public static class Skip
{
    public static void Inconclusive(string message) => throw new SkipTestException(message);

    public static void IfNoConfig(string prop, [NotNull] string? value)
    {
        if (value.IsNullOrEmpty())
        {
            throw new SkipTestException($"Config.{prop} is not set, skipping test.");
        }
    }

    public static void IfBelow(IConnectionMultiplexer conn, Version minVersion)
    {
        var serverVersion = conn.GetServer(conn.GetEndPoints()[0]).Version;
        if (minVersion > serverVersion)
        {
            throw new SkipTestException($"Requires server version {minVersion}, but server is only {serverVersion}.")
            {
                MissingFeatures = $"Server version >= {minVersion}."
            };
        }
    }

    internal static void IfMissingDatabase(IConnectionMultiplexer conn, int dbId)
    {
        var dbCount = conn.GetServer(conn.GetEndPoints()[0]).DatabaseCount;
        if (dbId >= dbCount) throw new SkipTestException($"Database '{dbId}' is not supported on this server.");
    }
}

public class SkipTestException : Exception
{
    public string? MissingFeatures { get; set; }

    public SkipTestException(string reason) : base(reason) { }
}
