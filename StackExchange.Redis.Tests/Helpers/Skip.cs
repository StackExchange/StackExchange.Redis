using System;

namespace StackExchange.Redis.Tests
{
    public static class Skip
    {
        public static void Inconclusive(string message) => throw new SkipTestException(message);

        public static void IfMissingFeature(ConnectionMultiplexer conn, string feature, Func<RedisFeatures, bool> check)
        {
            var features = conn.GetServer(conn.GetEndPoints()[0]).Features;
            if (!check(features))
            {
                MissingFeature(feature);
            }
        }

        public static void MissingFeature(string features)
        {
            throw new SkipTestException(features + " is not supported on this server.")
            {
                MissingFeatures = features
            };
        }

        //public static void NotSupported(string feature) => throw new SkipTestException(feature + " is not supported on this server");
    }

#pragma warning disable RCS1194 // Implement exception constructors.
    public class SkipTestException : Exception
    {
        public string MissingFeatures { get; set; }

        public SkipTestException(string reason) : base(reason) { }
    }
#pragma warning restore RCS1194 // Implement exception constructors.
}
