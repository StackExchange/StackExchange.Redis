using System;
using System.Buffers.Text;
using System.Diagnostics;
using static StackExchange.Redis.KeyNotificationChannels;
namespace StackExchange.Redis;

/// <summary>
/// Represents keyspace and keyevent notifications.
/// </summary>
public readonly struct KeyNotification
{
    /// <summary>
    /// If the channel is either a keyspace or keyevent notification, parsed the data.
    /// </summary>
    public static bool TryParse(in RedisChannel channel, in RedisValue value, out KeyNotification notification)
    {
        // validate that it looks reasonable
        var span = channel.Span;

        // KeySpaceStart and KeyEventStart are the same size, see KeyEventPrefix_KeySpacePrefix_Length_Matches
        if (span.Length >= KeySpacePrefix.Length + MinSuffixBytes)
        {
            // check that the prefix is valid, i.e. "__keyspace@" or "__keyevent@"
            var prefix = span.Slice(0, KeySpacePrefix.Length);
            var hash = prefix.Hash64();
            switch (hash)
            {
                case KeySpacePrefix.Hash when KeySpacePrefix.Is(hash, prefix):
                case KeyEventPrefix.Hash when KeyEventPrefix.Is(hash, prefix):
                    // check that there is *something* non-empty after the prefix, with __: as the suffix (we don't verify *what*)
                    if (span.Slice(KeySpacePrefix.Length).IndexOf("__:"u8) > 0)
                    {
                        notification = new KeyNotification(in channel, in value);
                        return true;
                    }

                    break;
            }
        }

        notification = default;
        return false;
    }

    private const int MinSuffixBytes = 5; // need "0__:x" or similar after prefix

    /// <summary>
    /// The channel associated with this notification.
    /// </summary>
    public RedisChannel Channel => _channel;

    /// <summary>
    /// The payload associated with this notification.
    /// </summary>
    public RedisValue Value => _value;

    // effectively we just wrap a channel, but: we've pre-validated that things make sense
    private readonly RedisChannel _channel;
    private readonly RedisValue _value;

    internal KeyNotification(in RedisChannel channel, in RedisValue value)
    {
        _channel = channel;
        _value = value;
    }

    /// <summary>
    /// The database the key is in. If the database cannot be parsed, <c>-1</c> is returned.
    /// </summary>
    public int Database
    {
        get
        {
            // prevalidated format, so we can just skip past the prefix (except for the default value)
            if (_channel.IsNull) return -1;
            var span = _channel.Span.Slice(KeySpacePrefix.Length); // also works for KeyEventPrefix
            var end = span.IndexOf((byte)'_'); // expecting "__:foo" - we'll just stop at the underscore
            if (end <= 0) return -1;

            span = span.Slice(0, end);
            return Utf8Parser.TryParse(span, out int database, out var bytes)
                && bytes == end ? database : -1;
        }
    }

    /// <summary>
    /// The key associated with this event.
    /// </summary>
    /// <remarks>Note that this will allocate a copy of the key bytes; to avoid allocations,
    /// the <see cref="KeyByteCount"/> and <see cref="TryCopyKey(Span{byte}, out int)"/> APIs can be used.</remarks>
    public RedisKey GetKey()
    {
        if (IsKeySpace)
        {
            // then the channel contains the key, and the payload contains the event-type
            return ChannelSuffix.ToArray(); // create an isolated copy
        }

        if (IsKeyEvent)
        {
            // then the channel contains the event-type, and the payload contains the key
            return (byte[]?)Value; // todo: this could probably side-step
        }

        return RedisKey.Null;
    }

    /// <summary>
    /// Get the number of bytes in the key.
    /// </summary>
    public int KeyByteCount
    {
        get
        {
            if (IsKeySpace)
            {
                return ChannelSuffix.Length;
            }

            if (IsKeyEvent)
            {
                return _value.GetByteCount();
            }

            return 0;
        }
    }

    /// <summary>
    /// Attempt to copy the bytes from the key to a buffer, returning the number of bytes written.
    /// </summary>
    public bool TryCopyKey(Span<byte> destination, out int bytesWritten)
    {
        if (IsKeySpace)
        {
            var suffix = ChannelSuffix;
            bytesWritten = suffix.Length; // assume success
            if (bytesWritten <= destination.Length)
            {
                suffix.CopyTo(destination);
                return true;
            }
        }

        if (IsKeyEvent)
        {
            bytesWritten = _value.GetByteCount();
            if (bytesWritten <= destination.Length)
            {
                var tmp = _value.CopyTo(destination);
                Debug.Assert(tmp == bytesWritten);
                return true;
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>
    /// Get the portion of the channel after the "__{keyspace|keyevent}@{db}__:".
    /// </summary>
    private ReadOnlySpan<byte> ChannelSuffix
    {
        get
        {
            var span = _channel.Span;
            var index = span.IndexOf("__:"u8);
            return index > 0 ? span.Slice(index + 3) : default;
        }
    }

    /// <summary>
    /// The type of notification associated with this event, if it is well-known - otherwise <see cref="KeyNotificationType.Unknown"/>.
    /// </summary>
    /// <remarks>Unexpected values can be processed manually from the <see cref="Channel"/> and <see cref="Value"/>.</remarks>
    public KeyNotificationType Type
    {
        get
        {
            if (IsKeySpace)
            {
                // then the channel contains the key, and the payload contains the event-type
                var count = _value.GetByteCount();
                if (count >= KeyNotificationTypeFastHash.MinBytes & count <= KeyNotificationTypeFastHash.MaxBytes)
                {
                    if (_value.TryGetSpan(out var direct))
                    {
                        return KeyNotificationTypeFastHash.Parse(direct);
                    }
                    else
                    {
                        Span<byte> localCopy = stackalloc byte[KeyNotificationTypeFastHash.MaxBytes];
                        return KeyNotificationTypeFastHash.Parse(localCopy.Slice(0, _value.CopyTo(localCopy)));
                    }
                }
            }

            if (IsKeyEvent)
            {
                // then the channel contains the event-type, and the payload contains the key
                return KeyNotificationTypeFastHash.Parse(ChannelSuffix);
            }
            return KeyNotificationType.Unknown;
        }
    }

    /// <summary>
    /// Indicates whether this notification originated from a keyspace notification, for example <c>__keyspace@4__:mykey</c> with payload <c>set</c>.
    /// </summary>
    public bool IsKeySpace
    {
        get
        {
            var span = _channel.Span;
            return span.Length >= KeySpacePrefix.Length + MinSuffixBytes && KeySpacePrefix.Is(span.Hash64(), span.Slice(0, KeySpacePrefix.Length));
        }
    }

    /// <summary>
    /// Indicates whether this notification originated from a keyevent notification, for example <c>__keyevent@4__:set</c> with payload <c>mykey</c>.
    /// </summary>
    public bool IsKeyEvent
    {
        get
        {
            var span = _channel.Span;
            return span.Length >= KeyEventPrefix.Length + MinSuffixBytes && KeyEventPrefix.Is(span.Hash64(), span.Slice(0, KeyEventPrefix.Length));
        }
    }
}

internal static partial class KeyNotificationChannels
{
    [FastHash("__keyspace@")]
    internal static partial class KeySpacePrefix
    {
    }

    [FastHash("__keyevent@")]
    internal static partial class KeyEventPrefix
    {
    }
}
