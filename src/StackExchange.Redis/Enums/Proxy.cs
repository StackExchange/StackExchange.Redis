namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies the proxy that is being used to communicate to redis.
    /// </summary>
    public enum Proxy
    {
        /// <summary>
        /// Direct communication to the redis server(s).
        /// </summary>
        None,
        /// <summary>
        /// Communication via <a href="https://github.com/twitter/twemproxy">twemproxy</a>.
        /// </summary>
        Twemproxy,
        /// <summary>
        /// Communication via <a href="https://github.com/envoyproxy/envoy">envoyproxy</a>.
        /// </summary>
        Envoyproxy,
    }

    internal static class ProxyExtensions
    {
        /// <summary>
        /// Whether a proxy supports databases (e.g. database > 0).
        /// </summary>
        internal static bool SupportsDatabases(this Proxy proxy) => proxy switch
        {
            Proxy.Twemproxy => false,
            Proxy.Envoyproxy => false,
            _ => true
        };

        /// <summary>
        /// Whether a proxy supports pub/sub.
        /// </summary>
        internal static bool SupportsPubSub(this Proxy proxy) => proxy switch
        {
            Proxy.Twemproxy => false,
            Proxy.Envoyproxy => false,
            _ => true
        };

        /// <summary>
        /// Whether a proxy supports the <c>ConnectionMultiplexer.GetServer</c>.
        /// </summary>
        internal static bool SupportsServerApi(this Proxy proxy) => proxy switch
        {
            Proxy.Twemproxy => false,
            Proxy.Envoyproxy => false,
            _ => true
        };
    }
}
