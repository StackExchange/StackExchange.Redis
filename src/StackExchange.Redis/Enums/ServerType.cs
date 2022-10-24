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
        Standalone,
        /// <summary>
        /// Monitoring/configuration redis-sentinel server.
        /// </summary>
        Sentinel,
        /// <summary>
        /// Distributed redis-cluster server.
        /// </summary>
        Cluster,
        /// <summary>
        /// Distributed redis installation via <a href="https://github.com/twitter/twemproxy">twemproxy</a>.
        /// </summary>
        Twemproxy,
        /// <summary>
        /// Redis cluster via <a href="https://github.com/envoyproxy/envoy">envoyproxy</a>.
        /// </summary>
        Envoyproxy,
    }

    internal static class ServerTypeExtensions
    {
        /// <summary>
        /// Whether a server type can have only a single primary, meaning an election if multiple are found.
        /// </summary>
        internal static bool HasSinglePrimary(this ServerType type) => type switch
        {
            ServerType.Envoyproxy => false,
            _ => true
        };

        /// <summary>
        /// Whether a server type supports <see cref="ServerEndPoint.AutoConfigureAsync(PhysicalConnection, LogProxy)"/>.
        /// </summary>
        internal static bool SupportsAutoConfigure(this ServerType type) => type switch
        {
            ServerType.Twemproxy => false,
            ServerType.Envoyproxy => false,
            _ => true
        };
    }
}
