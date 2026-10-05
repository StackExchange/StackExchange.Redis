using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

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

    /// <summary>PUBSUB CHANNELS: the channels this server has subscribers for.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="pattern">Only report channels matching this, or everything when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>The answer is one node's</b>, not the deployment's: a cluster propagates a publish across the
    /// bus, but each node only knows the subscribers attached to it. Asking a different node is asking a
    /// different question.
    /// </remarks>
    public static ValueTask<RedisChannel[]> ChannelsAsync(this in RespPubSub pubsub, RedisChannel pattern = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        // the names come back prefixed, and a caller never used the prefixed form - it belongs to the
        // wire, exactly as it does on the way out
        var handler = ChannelArrayHandler.For(pubsub.Context.ChannelPrefix);
        return pattern.IsNullOrEmpty
            ? pubsub.Context.SendAsync($"{RedisCommand.PUBSUB}{RespLiterals.Channels}", flags, handler, cancellationToken)
            : pubsub.Context.SendAsync($"{RedisCommand.PUBSUB}{RespLiterals.Channels}{pattern}", flags, handler, cancellationToken);
    }

    /// <summary>PUBSUB NUMPAT: how many pattern subscriptions this server is serving.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="ChannelsAsync" path="/remarks"/></remarks>
    public static ValueTask<long> PatternCountAsync(this in RespPubSub pubsub, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => pubsub.Context.SendAsync<long>($"{RedisCommand.PUBSUB}{RespLiterals.NumPat}", flags, cancellationToken: cancellationToken);

    /// <summary>PUBSUB NUMSUB: how many subscribers this server has for one channel.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="channel">The channel to count.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="ChannelsAsync" path="/remarks"/></remarks>
    public static ValueTask<long> SubscriberCountAsync(this in RespPubSub pubsub, RedisChannel channel, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => pubsub.Context.SendAsync($"{RedisCommand.PUBSUB}{RespLiterals.NumSub}{channel}", flags, NumSubHandler.Instance, cancellationToken);

    /// <summary>SUBSCRIBE, PSUBSCRIBE or SSUBSCRIBE, chosen by what kind of channel this is.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="channel">The channel or pattern to subscribe to.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>Internal: subscribing is not a command, it is a registration</b>, and the thing that owns the
    /// registration is the multiplexer - handlers, queues, and which server currently holds the
    /// subscription all outlive this call. So the public way to subscribe stays
    /// <see cref="ISubscriber"/>, and this is how that reaches the server. See design notes D2.5.
    /// </para>
    /// <para>
    /// <b>The context decides the connection, not this.</b> Under RESP2 a subscription needs its own
    /// socket and under RESP3 it does not, which is <c>RespNewCore.SubscriptionContext</c>'s business;
    /// what arrives here is already the right one.
    /// </para>
    /// <para>
    /// Answers the server's own count of what this connection is now subscribed to, which is the third
    /// element of the confirmation and the number the shipped <c>TrackSubscriptionsProcessor</c> records.
    /// </para>
    /// </remarks>
    internal static ValueTask<long> SubscribeAsync(
        this in RespPubSub pubsub,
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => pubsub.Context.SendAsync(
            $"{SubscribeCommand(channel, subscribe: true)}{channel}",
            flags,
            SubscriptionConfirmationHandler.Instance,
            cancellationToken);

    /// <summary>UNSUBSCRIBE, PUNSUBSCRIBE or SUNSUBSCRIBE, chosen the same way.</summary>
    /// <param name="pubsub">The pub/sub command group.</param>
    /// <param name="channel">The channel or pattern to stop receiving.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="SubscribeAsync" path="/remarks"/></remarks>
    internal static ValueTask<long> UnsubscribeAsync(
        this in RespPubSub pubsub,
        RedisChannel channel,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => pubsub.Context.SendAsync(
            $"{SubscribeCommand(channel, subscribe: false)}{channel}",
            flags,
            SubscriptionConfirmationHandler.Instance,
            cancellationToken);

    /// <summary>Which of the six spellings this channel wants.</summary>
    /// <param name="channel">The channel, whose options carry the answer.</param>
    /// <param name="subscribe">Subscribing rather than unsubscribing.</param>
    /// <remarks>
    /// <b>The same mapping as <c>Subscription.GetSubscriptionMessage</c></b>, and the same two options
    /// masked out of the question: <c>KeyRouted</c> and <c>IgnoreChannelPrefix</c> change where a
    /// subscription goes and how its name is written, not which command says it. A sharded channel is the
    /// one that genuinely is a different command, because <c>SSUBSCRIBE</c> is slot-scoped.
    /// </remarks>
    internal static RedisCommand SubscribeCommand(in RedisChannel channel, bool subscribe)
    {
        const RedisChannel.RedisChannelOptions OptionsMask = ~(
            RedisChannel.RedisChannelOptions.KeyRouted | RedisChannel.RedisChannelOptions.IgnoreChannelPrefix);

        return (channel.Options & OptionsMask) switch
        {
            RedisChannel.RedisChannelOptions.None or RedisChannel.RedisChannelOptions.MultiNode =>
                subscribe ? RedisCommand.SUBSCRIBE : RedisCommand.UNSUBSCRIBE,
            RedisChannel.RedisChannelOptions.Pattern or
                RedisChannel.RedisChannelOptions.Pattern | RedisChannel.RedisChannelOptions.MultiNode =>
                subscribe ? RedisCommand.PSUBSCRIBE : RedisCommand.PUNSUBSCRIBE,
            RedisChannel.RedisChannelOptions.Sharded =>
                subscribe ? RedisCommand.SSUBSCRIBE : RedisCommand.SUNSUBSCRIBE,
            _ => throw new ArgumentException(
                $"Unable to determine pub/sub operation for '{(subscribe ? "Subscribe" : "Unsubscribe")}' against '{channel.Options}'"),
        };
    }

    /// <summary>Reads a subscribe/unsubscribe confirmation: the kind, the channel, and the count.</summary>
    /// <remarks>
    /// <b>The count is the only part not already known</b> - the caller chose the command and the channel -
    /// and it is how many subscriptions this CONNECTION now holds, not how many subscribers the channel
    /// has. Under RESP3 the confirmation arrives as a push frame, which the dispatcher deliberately hands
    /// back for ordinary matching rather than consuming; this reads it either way.
    /// </remarks>
    private sealed class SubscriptionConfirmationHandler : IRespHandler<long>
    {
        internal static readonly SubscriptionConfirmationHandler Instance = new();

        public long Parse(ref RespReader reader)
        {
            if (reader.IsAggregate && reader.AggregateLength() >= 3
                && reader.TryMoveNext() && reader.IsScalar // the kind
                && reader.TryMoveNext() // the channel, which may be nil on an empty unsubscribe
                && reader.TryMoveNext() && reader.IsScalar
                && reader.TryReadInt64(out var count))
            {
                return count;
            }

            throw new RespException("Unexpected subscription confirmation.");
        }
    }

    /// <summary>Reads <c>PUBSUB CHANNELS</c>: a flat array of channel names.</summary>
    /// <remarks>
    /// <b>Literal channels, never patterns.</b> What comes back is what somebody subscribed to, and a
    /// name containing <c>*</c> is a name rather than a pattern - reading them as patterns would make a
    /// channel called <c>news.*</c> compare equal to a subscription it has nothing to do with.
    /// </remarks>
    internal sealed class ChannelArrayHandler : IRespHandler<RedisChannel[]>
    {
        private static readonly ChannelArrayHandler Unprefixed = new(default);

        private readonly RedisChannel _prefix;

        private ChannelArrayHandler(RedisChannel prefix) => _prefix = prefix;

        /// <summary>The handler for a given prefix; the common case of none is a singleton.</summary>
        /// <param name="prefix">The configured channel prefix, if any.</param>
        internal static ChannelArrayHandler For(in RedisChannel prefix)
            => prefix.IsNullOrEmpty ? Unprefixed : new(prefix);

        public RedisChannel[] Parse(ref RespReader reader)
        {
            if (reader.IsNull || !reader.IsAggregate) return [];

            var prefix = _prefix;
            return reader.ReadPastArray(
                ref prefix,
                static (ref RedisChannel prefix, ref RespReader reader)
                    => RespChannels.AsRedisChannel(prefix.Span, in reader, RedisChannel.RedisChannelOptions.None),
                scalar: true) ?? [];
        }
    }

    /// <summary>Reads <c>PUBSUB NUMSUB</c>: name/count pairs, of which we asked for exactly one.</summary>
    internal sealed class NumSubHandler : IRespHandler<long>
    {
        internal static readonly NumSubHandler Instance = new();

        public long Parse(ref RespReader reader)
        {
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar // the name, which we already know
                && reader.TryMoveNext() && reader.IsScalar
                && reader.TryReadInt64(out var count)
                && !reader.TryMoveNext()) // one channel in, one pair out
            {
                return count;
            }

            throw new RespException("Unexpected PUBSUB NUMSUB reply.");
        }
    }
}
