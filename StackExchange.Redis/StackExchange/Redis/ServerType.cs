namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates the flavor of a particular redis server
    /// </summary>
    public enum ServerType
    {
        /// <summary>
        /// Classic redis-server server
        /// </summary>
        Standalone,
        /// <summary>
        /// Monitoring/configuration redis-sentinel server
        /// </summary>
        Sentinel,
        /// <summary>
        /// Distributed redis-cluster server
        /// </summary>
        Cluster
    }
}
