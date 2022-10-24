using System;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// A redis connection used as the subscriber in a pub/sub scenario.
    /// </summary>
    public interface ISubscriber : IRedis
    {
        /// <summary>
        /// Indicate exactly which redis server we are talking to.
        /// </summary>
        /// <param name="channel">The channel to identify the server endpoint by.</param>
        /// <param name="flags">The command flags to use.</param>
        EndPoint? IdentifyEndpoint(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="IdentifyEndpoint(RedisChannel, CommandFlags)"/>
        Task<EndPoint?> IdentifyEndpointAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicates whether the instance can communicate with the server.
        /// If a channel is specified, the existing subscription map is queried to
        /// resolve the server responsible for that subscription - otherwise the
        /// server is chosen arbitrarily from the primaries.
        /// </summary>
        /// <param name="channel">The channel to identify the server endpoint by.</param>
        /// <returns><see langword="true" /> if connected, <see langword="false"/> otherwise.</returns>
        bool IsConnected(RedisChannel channel = default);

        /// <summary>
        /// Posts a message to the given channel.
        /// </summary>
        /// <param name="channel">The channel to publish to.</param>
        /// <param name="message">The message to publish.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>
        /// The number of clients that received the message *on the destination server*,
        /// note that this doesn't mean much in a cluster as clients can get the message through other nodes.
        /// </returns>
        /// <remarks><seealso href="https://redis.io/commands/publish"/></remarks>
        long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="Publish(RedisChannel, RedisValue, CommandFlags)"/>
        Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a message to the preferred/active node is broadcast, without any guarantee of ordered handling.
        /// </summary>
        /// <param name="channel">The channel to subscribe to.</param>
        /// <param name="handler">The handler to invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>
        /// <seealso href="https://redis.io/commands/subscribe"/>,
        /// <seealso href="https://redis.io/commands/psubscribe"/>
        /// </remarks>
        void Subscribe(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="Subscribe(RedisChannel, Action{RedisChannel, RedisValue}, CommandFlags)"/>
        Task SubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue> handler, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Subscribe to perform some operation when a message to the preferred/active node is broadcast, as a queue that guarantees ordered handling.
        /// </summary>
        /// <param name="channel">The redis channel to subscribe to.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>A channel that represents this source</returns>
        /// <remarks>
        /// <seealso href="https://redis.io/commands/subscribe"/>,
        /// <seealso href="https://redis.io/commands/psubscribe"/>
        /// </remarks>
        ChannelMessageQueue Subscribe(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="Subscribe(RedisChannel, CommandFlags)"/>
        Task<ChannelMessageQueue> SubscribeAsync(RedisChannel channel, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicate to which redis server we are actively subscribed for a given channel.
        /// </summary>
        /// <param name="channel">The channel to check which server endpoint was subscribed on.</param>
        /// <returns>The subscribed endpoint for the given <paramref name="channel"/>, <see langword="null"/> if the channel is not actively subscribed.</returns>
        EndPoint? SubscribedEndpoint(RedisChannel channel);

        /// <summary>
        /// Unsubscribe from a specified message channel.
        /// Note: if no handler is specified, the subscription is canceled regardless of the subscribers.
        /// If a handler is specified, the subscription is only canceled if this handler is the last handler remaining against the channel.
        /// </summary>
        /// <param name="channel">The channel that was subscribed to.</param>
        /// <param name="handler">The handler to no longer invoke when a message is received on <paramref name="channel"/>.</param>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>
        /// <seealso href="https://redis.io/commands/unsubscribe"/>,
        /// <seealso href="https://redis.io/commands/punsubscribe"/>
        /// </remarks>
        void Unsubscribe(RedisChannel channel, Action<RedisChannel, RedisValue>? handler = null, CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="Unsubscribe(RedisChannel, Action{RedisChannel, RedisValue}?, CommandFlags)"/>
        Task UnsubscribeAsync(RedisChannel channel, Action<RedisChannel, RedisValue>? handler = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Unsubscribe all subscriptions on this instance.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <remarks>
        /// <seealso href="https://redis.io/commands/unsubscribe"/>,
        /// <seealso href="https://redis.io/commands/punsubscribe"/>
        /// </remarks>
        void UnsubscribeAll(CommandFlags flags = CommandFlags.None);

        /// <inheritdoc cref="UnsubscribeAll(CommandFlags)"/>
        Task UnsubscribeAllAsync(CommandFlags flags = CommandFlags.None);
    }
}
