using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// The publish/subscribe commands.
/// </summary>
/// <remarks><inheritdoc cref="HyperLogLog" path="/remarks"/></remarks>
public static partial class PubSub
{
    /// <summary>PUBLISH, or SPUBLISH for a sharded channel; the reply is how many clients received it.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="channel">The channel to publish to.</param>
    /// <param name="message">The payload.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>The channel picks the command</b>, which is why there is one method rather than two:
    /// a sharded channel is <c>SPUBLISH</c> and an ordinary one is <c>PUBLISH</c>, and a caller choosing
    /// the spelling separately from the channel could only get them out of step. A multi-node channel
    /// throws, as it always has - it describes a subscription that exists on every node, so there is no
    /// single node a publish would mean.
    /// </para>
    /// <para>
    /// <b>Routing comes from the channel bytes</b>, which the frame already folds into a slot the same way
    /// it folds a key. That is what sharded pub/sub requires - <c>SPUBLISH</c> is delivered by the shard
    /// owning the channel's slot and by nobody else - and it is harmless for ordinary <c>PUBLISH</c>,
    /// which any node will propagate across the cluster bus. The shipped path instead prefers whichever
    /// server this client already holds a subscription on; see the note in <c>TransitionalDatabase</c>.
    /// </para>
    /// </remarks>
    public static ValueTask<long> PublishAsync(this in RespPubSub pubsub, RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        // asked BEFORE the frame is rendered, because the builder binds to the context it renders through;
        // and asked with the UNPREFIXED channel, because that is the key the subscription registry holds -
        // the prefix belongs to the wire, and the writer adds it
        var context = pubsub.Context;
        if (context.Executor?.ResolveForChannel(channel) is { } routed)
        {
            context = context.WithExecutor(routed);
        }

        return context.SendAsync<long>(
            $"{channel.GetPublishCommand()}{channel}{message}", flags, cancellationToken: cancellationToken);
    }
}
