using System;
using System.Buffers;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis
{
    /// <summary>
    /// Reading a channel name off the wire, which is not simply reading a string.
    /// </summary>
    /// <remarks>
    /// <b>The configured channel prefix is applied on the way out and stripped on the way in</b>, so a
    /// caller sees the names it used. Shared rather than written per core, because a path that forgets
    /// the stripping does not fail loudly: the name simply does not match the subscription registry, and
    /// deliveries stop arriving for anyone who configured a prefix.
    /// </remarks>
    internal static partial class RespChannels
    {
        /// <summary>Read a channel name, removing the configured prefix if it carries one.</summary>
        /// <param name="channelPrefix">The configured prefix; empty when none is configured.</param>
        /// <param name="reader">Positioned on the channel name.</param>
        /// <param name="options">What kind of channel this is - pattern, sharded, plain.</param>
        internal static RedisChannel AsRedisChannel(ReadOnlySpan<byte> channelPrefix, in RespReader reader, RedisChannel.RedisChannelOptions options)
        {
            if (channelPrefix.IsEmpty)
            {
                // no channel-prefix enabled, just use as-is
                return new RedisChannel(reader.ReadByteArray(), options);
            }

            byte[] lease = [];
            var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(ref lease, stackalloc byte[256]);

            if (span.StartsWith(channelPrefix))
            {
                // we have a channel-prefix, and it matches; strip it
                span = span.Slice(channelPrefix.Length);
            }
            else if (IsServerDefinedChannel(span))
            {
                // Server-defined channels should ignore our channel-prefix rules.
                // we shouldn't get unexpected events, so to get here: we've received a notification
                // on a channel that doesn't match our prefix; this *should* be limited to
                // key notifications (see: IgnoreChannelPrefix), but: we need to be sure

                // leave alone
            }
            else
            {
                // no idea what this is
                span = default;
            }

            RedisChannel channel = span.IsEmpty ? default : new(span.ToArray(), options);
            if (lease.Length != 0) ArrayPool<byte>.Shared.Return(lease);
            return channel;
        }

        private static bool IsServerDefinedChannel(ReadOnlySpan<byte> span)
        {
            var hash = AsciiHash.HashCS(span);
            return hash switch
            {
                KeyspaceChannelPrefix.HashCS when span.StartsWith(KeyspaceChannelPrefix.U8) => true,
                KeyeventChannelPrefix.HashCS when span.StartsWith(KeyeventChannelPrefix.U8) => true,
                SubkeyspaceChannelPrefix.HashCS when span.StartsWith(SubkeyspaceChannelPrefix.U8) => true,
                SubkeyeventChannelPrefix.HashCS when span.StartsWith(SubkeyeventChannelPrefix.U8) => true,
                SubkeyspaceItemChannelPrefix.HashCS when span.StartsWith(SubkeyspaceItemChannelPrefix.U8) => true,
                SubkeyspaceEventChannelPrefix.HashCS when span.StartsWith(SubkeyspaceEventChannelPrefix.U8) => true,
                _ => false,
            };
        }

        [AsciiHash("__keyspace@")]
        private static partial class KeyspaceChannelPrefix { }

        [AsciiHash("__keyevent@")]
        private static partial class KeyeventChannelPrefix { }

        [AsciiHash("__subkeyspace@")]
        private static partial class SubkeyspaceChannelPrefix { }

        [AsciiHash("__subkeyevent@")]
        private static partial class SubkeyeventChannelPrefix { }

        [AsciiHash("__subkeyspaceitem@")]
        private static partial class SubkeyspaceItemChannelPrefix { }

        [AsciiHash("__subkeyspaceevent@")]
        private static partial class SubkeyspaceEventChannelPrefix { }
    }
}
