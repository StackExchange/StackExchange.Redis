using System;

namespace StackExchange.Redis.KeyspaceIsolation
{
    /// <summary>
    ///     Provides the <see cref="WithChannelPrefix"/> extension method to <see cref="ISubscriber"/>.
    /// </summary>
    public static class SubscriberExtensions
    {
        /// <summary>
        ///     Creates a new <see cref="ISubscriber"/> instance that provides an isolated channel namespace
        ///     of the specified underlying subscriber instance.
        /// </summary>
        /// <param name="subscriber">
        ///     The underlying subscriber instance that the returned instance shall use.
        /// </param>
        /// <param name="channelPrefix">
        ///     The prefix that defines a channel namespace isolation for the returned subscriber instance.
        /// </param>
        /// <returns>
        ///     A new <see cref="ISubscriber"/> instance that invokes the specified underlying
        ///     <paramref name="subscriber"/> but prepends the specified <paramref name="channelPrefix"/>
        ///     to all channel names and thus forms a logical channel namespace isolation.
        /// </returns>
        public static ISubscriber WithChannelPrefix(this ISubscriber subscriber, RedisChannel channelPrefix)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (channelPrefix.IsNullOrEmpty)
            {
                throw new ArgumentNullException(nameof(channelPrefix));
            }

            if (subscriber is KeyPrefixedSubscriber wrapper)
            {
                // combine the channel prefix in advance to minimize indirection
                channelPrefix = wrapper.ToInner(channelPrefix);
                subscriber = wrapper.Inner;
            }

            return new KeyPrefixedSubscriber(subscriber, channelPrefix!);
        }
    }
}
