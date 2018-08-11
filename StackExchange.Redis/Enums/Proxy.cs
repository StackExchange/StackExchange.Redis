namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies the proxy that is being used to communicate to redis
    /// </summary>
    public enum Proxy
    {
        /// <summary>
        /// Direct communication to the redis server(s)
        /// </summary>
        None,
        /// <summary>
        /// Communication via <a href="https://github.com/twitter/twemproxy">twemproxy</a>
        /// </summary>
        Twemproxy
    }
}
