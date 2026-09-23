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
        internal static RespOutOfBandResult Dispatch(ReadOnlySpan<byte> frame, ConnectionMultiplexer multiplexer)
        {
            var reader = new RespReader(frame);
            if (!(reader.SafeTryMoveNext() & reader.IsAggregate & !reader.IsStreaming)) return RespOutOfBandResult.NotRecognized;
            if (reader.AggregateLength() < 2) return RespOutOfBandResult.NotRecognized;
            if (!(reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError)) return RespOutOfBandResult.NotRecognized;

            PhysicalConnection.PushKind kind;
            unsafe
            {
                if (!reader.TryParseScalar(&PhysicalConnection.PushKindMetadata.TryParse, out kind)) kind = PhysicalConnection.PushKind.None;
            }

            switch (kind)
            {
                case PhysicalConnection.PushKind.Message:
                case PhysicalConnection.PushKind.SMessage:
                    // [kind, channel, payload] - the channel is both what was subscribed and what matched
                    var options = kind == PhysicalConnection.PushKind.SMessage
                        ? RedisChannel.RedisChannelOptions.Sharded
                        : RedisChannel.RedisChannelOptions.None;
                    return Deliver(ref reader, multiplexer, options, patterned: false);

                case PhysicalConnection.PushKind.PMessage:
                    // [kind, pattern, channel, payload] - two channels, and they are not interchangeable:
                    // handlers are registered against the PATTERN, while the caller is told which channel
                    // actually matched
                    return Deliver(ref reader, multiplexer, RedisChannel.RedisChannelOptions.Pattern, patterned: true);

                case PhysicalConnection.PushKind.Subscribe:
                case PhysicalConnection.PushKind.PSubscribe:
                case PhysicalConnection.PushKind.SSubscribe:
                case PhysicalConnection.PushKind.Unsubscribe:
                case PhysicalConnection.PushKind.PUnsubscribe:
                case PhysicalConnection.PushKind.SUnsubscribe:
                    // these ANSWER a command we sent - in RESP3 a confirmation is a push - so ordinary
                    // matching has to complete it. Consuming it here would strand the subscribe call.
                    return RespOutOfBandResult.MatchToCommand;

                case PhysicalConnection.PushKind.Invalidate:
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
            if (!TryReadChannel(ref reader, options, out var subscription)) return RespOutOfBandResult.NotRecognized;

            var channel = subscription;
            if (patterned && !TryReadChannel(ref reader, RedisChannel.RedisChannelOptions.None, out channel))
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

        private static bool TryReadChannel(ref RespReader reader, RedisChannel.RedisChannelOptions options, out RedisChannel channel)
        {
            channel = default;
            if (!(reader.SafeTryMoveNext() & reader.IsInlineScalar)) return false;
            if (reader.Prefix is not (RespPrefix.BulkString or RespPrefix.SimpleString)) return false;

            var value = reader.ReadRedisValue();
            var bytes = (byte[]?)value;
            if (bytes is null) return false;

            channel = new RedisChannel(bytes, options);
            return true;
        }
    }
}
