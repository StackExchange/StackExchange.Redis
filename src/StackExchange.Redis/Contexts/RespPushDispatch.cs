using System;
using System.Net;
using RESPite.Messages;

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
    /// <b>Deliberately partial.</b> Deliveries and confirmations are here; maintenance notifications
    /// (<c>MOVING</c> and friends, which arrive on this same path) and an unsolicited
    /// <c>SUNSUBSCRIBE</c> - a slot migrating out from under a subscription - are not, and answer
    /// NotRecognized so they are dropped rather than mishandled. Both want their own tests, and the
    /// migration case wants the "is one of ours pending?" question answered properly rather than guessed.
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
            var payload = reader.ReadRedisValue();

            // before the handlers, because this one is not a handler's business: the configuration broadcast
            // is how a peer tells us the topology moved, and it is not kept in the pub/sub registry - so
            // nothing would be listening for it, and the reconfigure it asks for would simply not happen
            if (!patterned) CheckConfigurationBroadcast(multiplexer, in subscription, in payload);

            multiplexer.OnMessage(subscription, channel, payload);
            return RespOutOfBandResult.Handled;
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
            in RedisValue payload)
        {
            var configChanged = multiplexer.ConfigurationChangedChannel;
            if (configChanged is null || !channel.Span.SequenceEqual(configChanged)) return;

            EndPoint? blame = null;
            if (payload != RedisLiterals.Wildcard)
            {
                try
                {
                    _ = Format.TryParseEndPoint((string?)payload, out blame);
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
