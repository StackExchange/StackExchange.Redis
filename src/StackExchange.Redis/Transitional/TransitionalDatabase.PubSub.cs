using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// <c>PUBLISH</c>, where it has moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last two members SER352 was counting, and the ones that took the longest to be sure about.
    /// Publishing looks like it belongs to <see cref="ISubscriber"/> - it is the only pub/sub member on
    /// <see cref="IDatabaseAsync"/>, and forwarding to the subscriber would have implemented both in a
    /// line. It would also have been wrong: <see cref="IBatch"/> and <see cref="ITransaction"/> inherit
    /// this class, <c>PUBLISH</c> inside a <c>MULTI</c> is legal, and a forward to the subscriber would
    /// have run it immediately instead of queueing it - a publish that fired even when the transaction
    /// was later discarded.
    /// </para>
    /// <para>
    /// <b>One routing difference from the shipped path, deliberately.</b> There, a publish is sent to
    /// whichever server this client already holds a subscription to for that channel
    /// (<c>GetSubscribedServer</c>), falling back to normal selection. Here it routes on the channel's
    /// slot, like a key. For <c>SPUBLISH</c> that is the stronger rule and the required one - sharded
    /// pub/sub is delivered only by the shard owning the slot, so the slot decides and a subscription
    /// elsewhere cannot override it. For ordinary <c>PUBLISH</c> the two differ only in which node
    /// receives it first, because any node propagates it across the cluster bus.
    /// </para>
    /// </remarks>
    internal partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.PubSub.PublishAsync(channel, message, flags));

        /// <inheritdoc/>
        public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None)
            => _inner.PubSub.PublishAsync(channel, message, flags).AsTask(AsyncState, flags);
    }
}
