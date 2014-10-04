using System;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    /// <summary>
    ///     Provides the <see cref="WithKeyPrefix"/> extension method to <see cref="ISubscriber"/>.
    /// </summary>
    public static class SubscriberExtensions
    {
        /// <summary>
        ///     Creates a new <see cref="ISubscriber"/> instance that provides an isolated key space
        ///     of the specified underyling subscriber instance.
        /// </summary>
        /// <param name="subscriber">
        ///     The underlying subscriber instance that the returned instance shall use.
        /// </param>
        /// <param name="keyPrefix">
        ///     The prefix that defines a key space isolation for the returned subscriber instance.
        /// </param>
        /// <returns>
        ///     A new <see cref="ISubscriber"/> instance that invokes the specified underlying
        ///     <paramref name="subscriber"/> but prepends the specified <paramref name="keyPrefix"/>
        ///     to all key paramters and thus forms a logical key space isolation.
        /// </returns>
        public static ISubscriber WithKeyPrefix(this ISubscriber subscriber, RedisKey keyPrefix)
        {
            return ExtensionHelper.WithKeyPrefix(subscriber, "subscriber", keyPrefix, 
                (inner, prefix) => new SubscriberWrapper(inner, prefix));
        }
    }
}
