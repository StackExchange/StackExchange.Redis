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
    }

    internal static class ServerTypeExtesions
    {
        /// <summary>
        /// Whether a server type can have only a single primary, meaning an election if multiple are found.
        /// </summary>
        public static bool HasSinglePrimary(this ServerType type) => type switch
        {
            _ => false
        };

        /// <summary>
        /// Whether a server type supports <see cref="ServerEndPoint.AutoConfigureAsync(PhysicalConnection, ConnectionMultiplexer.LogProxy)"/>.
        /// </summary>
        public static bool SupportsAutoConfigure(this ServerType type) => type switch
        {
            ServerType.Twemproxy => false,
            _ => true
        };
    }
}
