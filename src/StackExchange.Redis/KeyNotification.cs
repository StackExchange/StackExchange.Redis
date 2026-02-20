using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite;
using static StackExchange.Redis.KeyNotificationChannels;
namespace StackExchange.Redis;

/// <summary>
/// Represents keyspace and keyevent notifications, with utility methods for accessing the component data. Additionally,
/// since notifications can be high volume, a range of utility APIs is provided for avoiding allocations, in particular
/// to assist in filtering and inspecting the key <em>without</em> performing string allocations and substring operations.
/// In particular, note that this allows use with the alt-lookup (span-based) APIs on dictionaries.
/// </summary>
public readonly ref struct KeyNotification
{
    // effectively we just wrap a channel, but: we've pre-validated that things make sense
    private readonly RedisChannel _channel;
    private readonly RedisValue _value;
    private readonly int _keyOffset; // used to efficiently strip key prefixes

    // this type has been designed with the intent of being able to move the entire thing alloc-free in some future
    // high-throughput callback, potentially with a ReadOnlySpan<byte> field for the key fragment; this is
    // not implemented currently, but is why this is a ref struct

    /// <summary>
    /// If the channel is either a keyspace or keyevent notification, resolve the key and event type.
    /// </summary>
    public static bool TryParse(scoped in RedisChannel channel, scoped in RedisValue value, out KeyNotification notification)
    {
        // validate that it looks reasonable
        var span = channel.Span;

        // KeySpaceStart and KeyEventStart are the same size, see KeyEventPrefix_KeySpacePrefix_Length_Matches
        if (span.Length >= KeySpacePrefix.Length + MinSuffixBytes)
        {
            // check that the prefix is valid, i.e. "__keyspace@" or "__keyevent@"
            var prefix = span.Slice(0, KeySpacePrefix.Length);
            var hash = prefix.HashCS();
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

    /// <summary>
    /// If the channel is either a keyspace or keyevent notification *with the requested prefix*, resolve the key and event type,
    /// and remove the prefix when reading the key.
    /// </summary>
    public static bool TryParse(scoped in ReadOnlySpan<byte> keyPrefix, scoped in RedisChannel channel, scoped in RedisValue value, out KeyNotification notification)
    {
        if (TryParse(in channel, in value, out notification) && notification.KeyStartsWith(keyPrefix))
        {
            notification = notification.WithKeySlice(keyPrefix.Length);
            return true;
        }

        notification = default;
        return false;
    }

    internal KeyNotification WithKeySlice(int keyPrefixLength)
    {
        KeyNotification result = this;
        Unsafe.AsRef(in result._keyOffset) = keyPrefixLength;
        return result;
    }

    private const int MinSuffixBytes = 5; // need "0__:x" or similar after prefix

    /// <summary>
    /// The channel associated with this notification.
    /// </summary>
    public RedisChannel GetChannel() => _channel;

    /// <summary>
    /// The payload associated with this notification.
    /// </summary>
    public RedisValue GetValue() => _value;

    internal KeyNotification(scoped in RedisChannel channel, scoped in RedisValue value)
    {
        _channel = channel;
        _value = value;
        _keyOffset = 0;
    }

    internal int KeyOffset => _keyOffset;

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
    /// the <see cref="GetKeyByteCount"/>, <see cref="GetKeyMaxCharCount"/>, and <see cref="TryCopyKey(Span{byte}, out int)"/> APIs can be used.</remarks>
    public RedisKey GetKey()
    {
        if (IsKeySpace)
        {
            // then the channel contains the key, and the payload contains the event-type
            return ChannelSuffix.Slice(_keyOffset).ToArray(); // create an isolated copy
        }

        if (IsKeyEvent)
        {
            // then the channel contains the event-type, and the payload contains the key
            byte[]? blob = _value;
            if (_keyOffset != 0 & blob is not null)
            {
                return blob.AsSpan(_keyOffset).ToArray();
            }
            return blob;
        }

        return RedisKey.Null;
    }

    /// <summary>
    /// Get the number of bytes in the key.
    /// </summary>
    /// <remarks>If a scratch-buffer is required, it may be preferable to use <see cref="GetKeyMaxByteCount"/>, which is less expensive.</remarks>
    public int GetKeyByteCount()
    {
        if (IsKeySpace)
        {
            return ChannelSuffix.Length - _keyOffset;
        }

        if (IsKeyEvent)
        {
            return _value.GetByteCount() - _keyOffset;
        }

        return 0;
    }

    /// <summary>
    /// Get the maximum number of bytes in the key.
    /// </summary>
    public int GetKeyMaxByteCount()
    {
        if (IsKeySpace)
        {
            return ChannelSuffix.Length - _keyOffset;
        }

        if (IsKeyEvent)
        {
            return _value.GetMaxByteCount() - _keyOffset;
        }

        return 0;
    }

    /// <summary>
    /// Get the maximum number of characters in the key, interpreting as UTF8.
    /// </summary>
    public int GetKeyMaxCharCount()
    {
        if (IsKeySpace)
        {
            return Encoding.UTF8.GetMaxCharCount(ChannelSuffix.Length - _keyOffset);
        }

        if (IsKeyEvent)
        {
            return _value.GetMaxCharCount() - _keyOffset;
        }

        return 0;
    }

    /// <summary>
    /// Get the number of characters in the key, interpreting as UTF8.
    /// </summary>
    /// <remarks>If a scratch-buffer is required, it may be preferable to use <see cref="GetKeyMaxCharCount"/>, which is less expensive.</remarks>
    public int GetKeyCharCount()
    {
        if (IsKeySpace)
        {
            return Encoding.UTF8.GetCharCount(ChannelSuffix.Slice(_keyOffset));
        }

        if (IsKeyEvent)
        {
            return _keyOffset == 0 ? _value.GetCharCount() : SlowMeasure(in this);
        }

        return 0;

        static int SlowMeasure(in KeyNotification value)
        {
            var span = value.GetKeySpan(out var lease, stackalloc byte[128]);
            var result = Encoding.UTF8.GetCharCount(span);
            Return(lease);
            return result;
        }
    }

    private ReadOnlySpan<byte> GetKeySpan(out byte[]? lease, Span<byte> buffer) // buffer typically stackalloc
    {
        lease = null;
        if (_value.TryGetSpan(out var direct))
        {
            return direct.Slice(_keyOffset);
        }
        var count = _value.GetMaxByteCount();
        if (count > buffer.Length)
        {
            buffer = lease = ArrayPool<byte>.Shared.Rent(count);
        }
        count = _value.CopyTo(buffer);
        return buffer.Slice(_keyOffset, count - _keyOffset);
    }

    private static void Return(byte[]? lease)
    {
        if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
    }

    /// <summary>
    /// Attempt to copy the bytes from the key to a buffer, returning the number of bytes written.
    /// </summary>
    public bool TryCopyKey(Span<byte> destination, out int bytesWritten)
    {
        if (IsKeySpace)
        {
            var suffix = ChannelSuffix.Slice(_keyOffset);
            bytesWritten = suffix.Length; // assume success
            if (bytesWritten <= destination.Length)
            {
                suffix.CopyTo(destination);
                return true;
            }
        }

        if (IsKeyEvent)
        {
            if (_value.TryGetSpan(out var direct))
            {
                bytesWritten = direct.Length - _keyOffset; // assume success
                if (bytesWritten <= destination.Length)
                {
                    direct.Slice(_keyOffset).CopyTo(destination);
                    return true;
                }
                bytesWritten = 0;
                return false;
            }

            if (_keyOffset == 0)
            {
                // get the value to do the hard work
                bytesWritten = _value.GetByteCount();
                if (bytesWritten <= destination.Length)
                {
                    _value.CopyTo(destination);
                    return true;
                }
                bytesWritten = 0;
                return false;
            }

            return SlowCopy(in this, destination, out bytesWritten);

            static bool SlowCopy(in KeyNotification value, Span<byte> destination, out int bytesWritten)
            {
                var span = value.GetKeySpan(out var lease, stackalloc byte[128]);
                bool result = span.TryCopyTo(destination);
                bytesWritten = result ? span.Length : 0;
                Return(lease);
                return result;
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>
    /// Attempt to copy the bytes from the key to a buffer, returning the number of bytes written.
    /// </summary>
    public bool TryCopyKey(Span<char> destination, out int charsWritten)
    {
        if (IsKeySpace)
        {
            var suffix = ChannelSuffix.Slice(_keyOffset);
            if (Encoding.UTF8.GetMaxCharCount(suffix.Length) <= destination.Length ||
                Encoding.UTF8.GetCharCount(suffix) <= destination.Length)
            {
                charsWritten = Encoding.UTF8.GetChars(suffix, destination);
                return true;
            }
        }

        if (IsKeyEvent)
        {
            if (_keyOffset == 0) // can use short-cut
            {
                if (_value.GetMaxCharCount() <= destination.Length || _value.GetCharCount() <= destination.Length)
                {
                    charsWritten = _value.CopyTo(destination);
                    return true;
                }
            }
            var span = GetKeySpan(out var lease, stackalloc byte[128]);
            charsWritten = 0;
            bool result = false;
            if (Encoding.UTF8.GetMaxCharCount(span.Length) <= destination.Length ||
                Encoding.UTF8.GetCharCount(span) <= destination.Length)
            {
                charsWritten = Encoding.UTF8.GetChars(span, destination);
                result = true;
            }
            Return(lease);
            return result;
        }

        charsWritten = 0;
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
    /// Indicates whether this notification is of the given type, specified as raw bytes.
    /// </summary>
    /// <remarks>This is especially useful for working with unknown event types, but repeated calls to this method will be more expensive than
    /// a single successful call to <see cref="Type"/>.</remarks>
    public bool IsType(ReadOnlySpan<byte> type)
    {
        if (IsKeySpace)
        {
            if (_value.TryGetSpan(out var direct))
            {
                return direct.SequenceEqual(type);
            }

            const int MAX_STACK = 64;
            byte[]? lease = null;
            var maxCount = _value.GetMaxByteCount();
            Span<byte> localCopy = maxCount <= MAX_STACK
                ? stackalloc byte[MAX_STACK]
                : (lease = ArrayPool<byte>.Shared.Rent(maxCount));
            var count = _value.CopyTo(localCopy);
            bool result = localCopy.Slice(0, count).SequenceEqual(type);
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
            return result;
        }

        if (IsKeyEvent)
        {
            return ChannelSuffix.SequenceEqual(type);
        }

        return false;
    }

    /// <summary>
    /// The type of notification associated with this event, if it is well-known - otherwise <see cref="KeyNotificationType.Unknown"/>.
    /// </summary>
    /// <remarks>Unexpected values can be processed manually from the <see cref="GetChannel()"/> and <see cref="GetValue()"/>.</remarks>
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
            return span.Length >= KeySpacePrefix.Length + MinSuffixBytes && KeySpacePrefix.Is(span.HashCS(), span.Slice(0, KeySpacePrefix.Length));
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
            return span.Length >= KeyEventPrefix.Length + MinSuffixBytes && KeyEventPrefix.Is(span.HashCS(), span.Slice(0, KeyEventPrefix.Length));
        }
    }

    /// <summary>
    /// Indicates whether the key associated with this notification starts with the specified prefix.
    /// </summary>
    /// <returns>This API is intended as a high-throughput filter API.</returns>
    public bool KeyStartsWith(ReadOnlySpan<byte> prefix) // intentionally leading people to the BLOB API
    {
        if (IsKeySpace)
        {
            return ChannelSuffix.Slice(_keyOffset).StartsWith(prefix);
        }

        if (IsKeyEvent)
        {
            if (_keyOffset == 0) return _value.StartsWith(prefix);

            var span = GetKeySpan(out var lease, stackalloc byte[128]);
            bool result = span.StartsWith(prefix);
            Return(lease);
            return result;
        }

        return false;
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
