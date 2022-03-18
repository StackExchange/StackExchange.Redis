using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis.Tests
{
    public static class Skip
    {
        public static void Inconclusive(string message) => throw new SkipTestException(message);

        public static void IfNoConfig(string prop,
#if NETCOREAPP
            [NotNull]
#endif
            string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new SkipTestException($"Config.{prop} is not set, skipping test.");
            }
        }

        public static void IfNoConfig(string prop,
#if NETCOREAPP
            [NotNull]
#endif
            List<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                throw new SkipTestException($"Config.{prop} is not set, skipping test.");
            }
        }

        public static void IfMissingFeature(IConnectionMultiplexer conn, string feature, Func<RedisFeatures, bool> check)
        {
            var features = conn.GetServer(conn.GetEndPoints()[0]).Features;
            if (!check(features))
            {
                throw new SkipTestException($"'{feature}' is not supported on this server.")
                {
                    MissingFeatures = feature
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
}
