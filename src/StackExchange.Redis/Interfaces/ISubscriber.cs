using System;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// A redis connection used as the subscriber in a pub/sub scenario
    /// </summary>
    public interface ISubscriber : IRedis
    {
        /// <summary>
        /// Indicate exactly which redis server we are talking to
        /// </summary>
        /// <param name="channel">The channel to identify the server endpoint by.</param>
        /// <param name="flags">The command flags to use.</param>
        EndPoint IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicate exactly which redis server we are talking to
        /// </summary>
        /// <param name="channel">The channel to identify the server endpoint by.</param>
        /// <param name="flags">The command flags to use.</param>
        Task<EndPoint> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicates whether the instance can communicate with the server;
        /// if a channel is specified, the existing subscription map is queried to
        /// resolve the server responsible for that subscription - otherwise the
        /// server is chosen arbitrarily from the masters.
        /// </summary>
        /// <param name="channel">The channel to identify the server endpoint by.</param>
        bool IsConnected(RedisChannel channel = default(RedisChannel));

        /// <summary>
        /// Posts a message to the given channel.
        /// </summary>
        /// <param name="channel">The channel to publish to.</param>
        /// <param name="message">The message to publish.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of clients that received the message.</returns>
        /// <remarks>https://redis.io/commands/publish</remarks>
        long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Posts a message to the given channel.
        /// </summary>
        /// <param name="channel">The channel to publish to.</param>
        /// <param name="message">The message to publish.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>the number of clients that received the message.</returns>
        /// <remarks>https://redis.io/commands/publish</remarks>
        Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a message to the preferred/active node is broadcast, without any guarantee of ordered handling.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/subscribe</remarks>
        /// <remarks>https://redis.io/commands/psubscribe</remarks>
        void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a message to the preferred/active node is broadcast, as a queue that guarantees ordered handling.
        /// </summary>
        /// <param name="channel">The redis channel to subscribe to.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>A channel that represents this source</returns>
        /// <remarks>https://redis.io/commands/subscribe</remarks>
        /// <remarks>https://redis.io/commands/psubscribe</remarks>
        ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a change to the preferred/active node is broadcast.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/subscribe</remarks>
        /// <remarks>https://redis.io/commands/psubscribe</remarks>
        Task SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a change to the preferred/active node is broadcast, as a channel.
        /// </summary>
        /// <param name="channel">The redis channel to subscribe to.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>A channel that represents this source</returns>
        /// <remarks>https://redis.io/commands/subscribe</remarks>
        /// <remarks>https://redis.io/commands/psubscribe</remarks>
        Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicate to which redis server we are actively subscribed for a given channel; returns null if
        /// the channel is not actively subscribed
        /// </summary>
        /// <param name="channel">The channel to check which server endpoint was subscribed on.</param>
        EndPoint SubscribedEndpoint(RedisChannel channel);

        /// <summary>
        /// Unsubscribe from a specified message channel; note; if no handler is specified, the subscription is cancelled regardless
        /// of the subscribers; if a handler is specified, the subscription is only cancelled if this handler is the
        /// last handler remaining against the channel
        /// </summary>
        /// <param name="channel">The channel that was subscribed to.</param>
        /// <param name="handler">The handler to no longer invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/unsubscribe</remarks>
        /// <remarks>https://redis.io/commands/punsubscribe</remarks>
        void Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Unsubscribe all subscriptions on this instance
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/unsubscribe</remarks>
        /// <remarks>https://redis.io/commands/punsubscribe</remarks>
        void UnsubscribeAll(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Unsubscribe all subscriptions on this instance
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/unsubscribe</remarks>
        /// <remarks>https://redis.io/commands/punsubscribe</remarks>
        Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Unsubscribe from a specified message channel; note; if no handler is specified, the subscription is cancelled regardless
        /// of the subscribers; if a handler is specified, the subscription is only cancelled if this handler is the
        /// last handler remaining against the channel
        /// </summary>
        /// <param name="channel">The channel that was subscribed to.</param>
        /// <param name="handler">The handler to no longer invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>https://redis.io/commands/unsubscribe</remarks>
        /// <remarks>https://redis.io/commands/punsubscribe</remarks>
        Task UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler = null, CommandFlags flags = CommandFlags.None);
    }
}
