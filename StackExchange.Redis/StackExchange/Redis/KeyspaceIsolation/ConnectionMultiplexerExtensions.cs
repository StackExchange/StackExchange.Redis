namespace StackExchange.Redis.KeyspaceIsolation
{
    /// <summary>
    ///     Provides the <see cref="WithPrefix"/> extension method to <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static class ConnectionMultiplexerExtensions
    {
        /// <summary>
        /// Wraps the <paramref name="inner"/> connection multiplexer to provide specific database and channels keyspace using the <paramref name="prefix"/>
        /// </summary>
        /// <param name="inner">The multiplexer to wrap</param>
        /// <param name="prefix">The prefix for keys and channel names</param>
        /// <remarks>
        /// The caller is responsible for disposing the wrapped <paramref name="inner"/> connection multiplexer
        /// </remarks>
        /// <seealso cref="DatabaseExtensions.WithKeyPrefix"/>
        /// <seealso cref="SubscriberExtensions.WithChannelPrefix"/>
        public static IConnectionMultiplexer WithPrefix(this IConnectionMultiplexer inner, string prefix)
        {
            return new ConnectionMultiplexerWrapper(inner, prefix);
        }
    }
}
