using System;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates the flavor of a particular redis server.
    /// </summary>
    public enum ServerType
    {
        /// <summary>
        /// Classic redis-server server.
        /// </summary>
        [AsciiHash("standalone")]
        Standalone,

        /// <summary>
        /// Monitoring/configuration redis-sentinel server.
        /// </summary>
        [AsciiHash("sentinel")]
        Sentinel,

        /// <summary>
        /// Distributed redis-cluster server.
        /// </summary>
        [AsciiHash("cluster")]
        Cluster,

        /// <summary>
        /// Distributed redis installation via <a href="https://github.com/twitter/twemproxy">twemproxy</a>.
        /// </summary>
        [AsciiHash("")]
        Twemproxy,

        /// <summary>
        /// Redis cluster via <a href="https://github.com/envoyproxy/envoy">envoyproxy</a>.
        /// </summary>
        [AsciiHash("")]
        Envoyproxy,
    }

    /// <summary>
    /// Metadata and parsing methods for <see cref="ServerType"/>.
    /// </summary>
    internal static partial class ServerTypeMetadata
    {
        [AsciiHash]
        internal static partial bool TryParse(ReadOnlySpan<char> value, out ServerType serverType);

        internal static bool TryParse(string? val, out ServerType serverType)
        {
            if (val is not null) return TryParse(val.AsSpan().Trim(), out serverType);
            serverType = default;
            return false;
        }
    }

    internal static class ServerTypeExtensions
    {
        /// <summary>
        /// Whether a server type can have only a single primary, meaning an election if multiple are found.
        /// </summary>
        internal static bool HasSinglePrimary(this ServerType type) => type switch
        {
            ServerType.Envoyproxy => false,
            _ => true,
        };

        /// <summary>
        /// Whether a server type supports <see cref="ServerEndPoint.AutoConfigureAsync(PhysicalConnection?, Microsoft.Extensions.Logging.ILogger?, CommandFlags)"/>.
        /// </summary>
        internal static bool SupportsAutoConfigure(this ServerType type) => type switch
        {
            ServerType.Twemproxy => false,
            ServerType.Envoyproxy => false,
            _ => true,
        };
    }
}
