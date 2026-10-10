using System;
using System.Net;
using RESPite.Messages;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis
{
    /// <summary>
    /// Turns an out-of-band frame into a delivery, a verdict, or nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same frames the shipped reader handles, decided the same way</b> - but answering with a
    /// verdict instead of acting on the connection, because the new core's connection has no bridge to
    /// reach into. What it does share is the part worth sharing: <c>PushKindMetadata.TryParse</c>, which
    /// is generated from the kind names, so the two paths cannot disagree about what a frame is called.
    /// </para>
    /// <para>
    /// <b>Deliberately partial.</b> Deliveries, confirmations, the configuration broadcast and cache
    /// invalidation are here; maintenance notifications (<c>MOVING</c> and friends, which arrive on this
    /// same path) and an unsolicited <c>SUNSUBSCRIBE</c> - a slot migrating out from under a subscription
    /// - are not, and answer NotRecognized so they are dropped rather than mishandled. Both want their own
    /// tests, and the migration case wants the "is one of ours pending?" question answered properly rather
    /// than guessed.
    /// </para>
    /// </remarks>
    internal static class RespPushDispatch
    {
        /// <summary>Inspect a frame and say what should happen to it.</summary>
        /// <param name="frame">The complete frame, including its prefix.</param>
        /// <param name="multiplexer">Receives any delivery.</param>
        /// <param name="endpoint">Which server this arrived from; needed to act on a stranded subscription.</param>
        internal static RespOutOfBandResult Dispatch(
            ReadOnlySpan<byte> frame,
            ConnectionMultiplexer multiplexer,
            EndPoint? endpoint = null)
        {
            var reader = new RespReader(frame);
            if (!(reader.SafeTryMoveNext() & reader.IsAggregate & !reader.IsStreaming)) return RespOutOfBandResult.NotRecognized;
            if (reader.AggregateLength() < 2) return RespOutOfBandResult.NotRecognized;
            if (!(reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError)) return RespOutOfBandResult.NotRecognized;

            PushKind kind;
            unsafe
            {
                if (!reader.TryParseScalar(&PushKindMetadata.TryParse, out kind)) kind = PushKind.None;
            }

            // BEFORE the pub/sub switch, exactly as the shipped reader does it: a maintenance notification's
            // second element is not a channel name, so anything that reads it as one rejects the frame.
            if (kind is >= PushKind.Moving and <= PushKind.SlotMigrated)
            {
                if (endpoint is null) return RespOutOfBandResult.NotRecognized;

                MaintenanceNotificationReader.ReadMaintenanceNotification(
                    multiplexer,
                    multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false),
                    isConnected: multiplexer.ConnectionsIfCreated?.IsConnected(endpoint) == true,
                    currentAddress: multiplexer.ConnectionsIfCreated?.RemoteAddress(endpoint),
                    kind,
                    ref reader);

                return RespOutOfBandResult.Handled;
            }

            switch (kind)
            {
                case PushKind.Message:
                case PushKind.SMessage:
                    // [kind, channel, payload] - the channel is both what was subscribed and what matched
                    var options = kind == PushKind.SMessage
                        ? RedisChannel.RedisChannelOptions.Sharded
                        : RedisChannel.RedisChannelOptions.None;
                    return Deliver(ref reader, multiplexer, options, patterned: false);

                case PushKind.PMessage:
                    // [kind, pattern, channel, payload] - two channels, and they are not interchangeable:
                    // handlers are registered against the PATTERN, while the caller is told which channel
                    // actually matched
                    return Deliver(ref reader, multiplexer, RedisChannel.RedisChannelOptions.Pattern, patterned: true);

                case PushKind.Subscribe:
                case PushKind.PSubscribe:
                case PushKind.SSubscribe:
                    // these ANSWER a command we sent - in RESP3 a confirmation is a push - so ordinary
                    // matching has to complete it. Consuming it here would strand the subscribe call.
                    return RespOutOfBandResult.MatchToCommand;

                case PushKind.SUnsubscribe
                    when TryResubscribeStranded(reader, multiplexer, endpoint):
                    // ...and so does a sharded unsubscribe we ASKED for. This is the other one: a slot
                    // migrating away makes the node drop its shard channels and say so unprompted, and
                    // with no command of ours to match it to, matching alone left this client believing
                    // it was still subscribed on a node that had stopped delivering.
                    //
                    // The v3 core did this in `PhysicalConnection.Read`, which while both cores existed
                    // never saw it: deliveries arrived on THIS core's connection. Same decision,
                    // same routine, same reasoning - including resubscribing via the OUTGOING node,
                    // which is the only one we know has the new route.
                    return RespOutOfBandResult.Handled;

                case PushKind.Unsubscribe:
                case PushKind.PUnsubscribe:
                case PushKind.SUnsubscribe:
                    return RespOutOfBandResult.MatchToCommand;

                case PushKind.Invalidate:
                    // not pub/sub either: the second element is an array of keys, or a null meaning "all of
                    // them" - so it is dispatched before anything tries to read a channel name
                    ApplyInvalidation(multiplexer.ClientCache, ref reader);
                    return RespOutOfBandResult.Handled;

                default:
                    return RespOutOfBandResult.NotRecognized;
            }
        }

        private static RespOutOfBandResult Deliver(
            ref RespReader reader,
            ConnectionMultiplexer multiplexer,
            RedisChannel.RedisChannelOptions options,
            bool patterned)
        {
            // the names arrive prefixed, and the registry is keyed by the names the CALLER used - so a
            // delivery read without stripping matches nothing, and a deployment with a channel prefix
            // simply stops receiving. It fails silently, which is why it is worth saying out loud
            var prefix = multiplexer.ChannelPrefix.AsSpan();
            if (!TryReadChannel(ref reader, prefix, options, out var subscription)) return RespOutOfBandResult.NotRecognized;

            var channel = subscription;
            if (patterned && !TryReadChannel(ref reader, prefix, RedisChannel.RedisChannelOptions.None, out channel))
            {
                return RespOutOfBandResult.NotRecognized;
            }

            if (!reader.SafeTryMoveNext()) return RespOutOfBandResult.NotRecognized;

            // before the handlers, because this one is not a handler's business: the configuration broadcast
            // is how a peer tells us the topology moved, and it is not kept in the pub/sub registry - so
            // nothing would be listening for it, and the reconfigure it asks for would simply not happen
            if (!patterned) CheckConfigurationBroadcast(multiplexer, in subscription, in reader);

            DeliverPayload(multiplexer, in subscription, in channel, in reader);
            return RespOutOfBandResult.Handled;
        }

        /// <summary>
        /// Hand one delivery to the registered handlers.
        /// </summary>
        /// <param name="multiplexer">Owns the handlers.</param>
        /// <param name="subscription">What was subscribed - the pattern, for a pattern subscription.</param>
        /// <param name="channel">What actually matched.</param>
        /// <param name="reader">Positioned on the payload.</param>
        /// <remarks>
        /// <b>One delivery can carry several messages.</b> A payload that is an array is not one value that
        /// happens to be a list: it is several messages batched into a frame, and each is delivered
        /// separately (see StackExchange.Redis#2507). Reading it as a single value hands the handler
        /// something it cannot use - so this is shared with the shipped reader rather than written twice,
        /// because it is exactly the sort of detail that drifts.
        /// </remarks>
        internal static void DeliverPayload(
            ConnectionMultiplexer multiplexer,
            in RedisChannel subscription,
            in RedisChannel channel,
            in RespReader reader)
        {
            switch (reader.Prefix)
            {
                case RespPrefix.BulkString:
                case RespPrefix.SimpleString:
                    multiplexer.OnMessage(subscription, channel, reader.ReadRedisValue());
                    break;
                case RespPrefix.Array:
                    var iter = reader.AggregateChildren();
                    while (iter.MoveNext())
                    {
                        multiplexer.OnMessage(subscription, channel, iter.Value.ReadRedisValue());
                    }

                    break;
            }
        }

        /// <summary>
        /// Apply a <c>CLIENT TRACKING</c> invalidation to the client-side cache, if there is one.
        /// </summary>
        /// <param name="cache">The cache to apply it to; nothing to do if there is none.</param>
        /// <param name="reader">Positioned on the <c>invalidate</c> token; the payload follows.</param>
        /// <remarks>
        /// A null payload means a flush - <c>FLUSHALL</c>/<c>FLUSHDB</c>, and also the moment tracking is
        /// turned off - and is the one invalidation that cannot be filtered by prefix, so it is never safe
        /// to ignore. Otherwise it is an array, because one write can name several keys: <c>MSET a b c</c>
        /// arrives as a single push. Anything unreadable over-flushes rather than quietly keeping entries
        /// the server has just said are wrong.
        /// </remarks>
        internal static void ApplyInvalidation(RespClientCache? cache, ref RespReader reader)
        {
            if (cache is null || !reader.SafeTryMoveNext()) return;

            if (reader.IsNull || !reader.IsAggregate || reader.IsStreaming)
            {
                cache.OnFlush();
                return;
            }

            var count = reader.AggregateLength();
            for (var i = 0; i < count; i++)
            {
                if (!reader.SafeTryMoveNext() || !reader.TryGetSpan(out var key))
                {
                    // a key we cannot see is a key we cannot evict, and we already know it changed
                    cache.OnFlush();
                    return;
                }

                cache.OnInvalidate(key); // allocation-free: the key never leaves this span
            }
        }

        /// <summary>
        /// Act on a peer's "the configuration changed" broadcast, if that is what this is.
        /// </summary>
        /// <remarks>
        /// The payload names who to blame, or <c>*</c> for "everyone" - and it is only ever a hint: a
        /// malformed one costs us the name, not the reconfigure, so it is parsed leniently and the
        /// reconfigure happens either way.
        /// </remarks>
        private static void CheckConfigurationBroadcast(
            ConnectionMultiplexer multiplexer,
            in RedisChannel channel,
            in RespReader reader)
        {
            var configChanged = multiplexer.ConfigurationChangedChannel;
            if (configChanged is null || !channel.Span.SequenceEqual(configChanged)) return;
            if (reader.Prefix is not (RespPrefix.BulkString or RespPrefix.SimpleString)) return;

            EndPoint? blame = null;
            if (!reader.Is("*"u8))
            {
                try
                {
                    _ = Format.TryParseEndPoint(reader.ReadString(), out blame);
                }
                catch
                {
                    // identifying the source is a nicety; not being able to is not a reason to ignore it
                }
            }

            multiplexer.ReconfigureIfNeeded(blame, true, "broadcast");
        }

        /// <summary>Act on a sharded unsubscribe the server sent unprompted, because a slot moved.</summary>
        /// <param name="reader">A COPY of the reader, positioned on the push kind.</param>
        /// <param name="multiplexer">Owns the subscription registry.</param>
        /// <param name="endpoint">Which server sent it - the OUTGOING node of the migration.</param>
        /// <returns><c>true</c> if this was unsolicited and has been acted on.</returns>
        /// <remarks>
        /// <para>
        /// Read from a COPY of the reader: when this answers false the frame is handed on to the matching
        /// layer, which parses it from the start, so consuming elements here would corrupt what it sees.
        /// <c>RespReader</c> is a struct over the same buffer, so a copy reads without disturbing it.
        /// </para>
        /// <para>
        /// <b>"Unsolicited" is the whole distinction, and a send in flight is how it is told.</b> A
        /// sharded unsubscribe of OUR OWN has one by definition - that is what
        /// <c>Subscription.HasSendInFlight</c> records - and must be left for matching to complete, or a
        /// caller who unsubscribed gets resubscribed. The shipped core asks the same question of its own
        /// outstanding commands (<c>PeekChannelMessage</c>).
        /// </para>
        /// </remarks>
        private static bool TryResubscribeStranded(RespReader reader, ConnectionMultiplexer multiplexer, EndPoint? endpoint)
        {
            if (endpoint is null) return false;
            if (!TryReadChannel(
                ref reader,
                multiplexer.ChannelPrefix.AsSpan(),
                RedisChannel.RedisChannelOptions.Sharded,
                out var channel))
            {
                return false;
            }

            if (!multiplexer.TryGetSubscription(channel, out var subscription)) return false;
            if (subscription.HasSendInFlight) return false;
            if (multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false) is not { } server)
            {
                return false;
            }

            multiplexer.DefaultSubscriber.ResubscribeToServer(subscription, channel, server, cause: "sunsubscribe");
            return true;
        }

        private static bool TryReadChannel(
            ref RespReader reader,
            ReadOnlySpan<byte> channelPrefix,
            RedisChannel.RedisChannelOptions options,
            out RedisChannel channel)
        {
            channel = default;
            if (!(reader.SafeTryMoveNext() & reader.IsInlineScalar)) return false;
            if (reader.Prefix is not (RespPrefix.BulkString or RespPrefix.SimpleString)) return false;

            channel = RespChannels.AsRedisChannel(channelPrefix, in reader, options);
            return !channel.IsNull;
        }
    }
}
