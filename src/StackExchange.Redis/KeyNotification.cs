using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite;
using static StackExchange.Redis.KeyNotificationChannels;

namespace StackExchange.Redis;

/// <summary>
/// Represents the type of keyspace notification channel.
/// </summary>
public enum KeyNotificationKind
{
    /// <summary>
    /// Unknown or invalid notification type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Standard keyspace notification: __keyspace@{db}__:{key} with payload containing the event.
    /// </summary>
    KeySpace = 1,

    /// <summary>
    /// Standard keyevent notification: __keyevent@{db}__:{event} with payload containing the key.
    /// </summary>
    KeyEvent = 2,

    /// <summary>
    /// Subkey keyspace notification: __subkeyspace@{db}__:{key} with payload containing event|subkey.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    SubKeySpace = 3,

    /// <summary>
    /// Subkey keyevent notification: __subkeyevent@{db}__:{event} with payload containing key|subkey.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    SubKeyEvent = 4,

    /// <summary>
    /// Subkey keyspaceitem notification: __subkeyspaceitem@{db}__:{key}\n{subkey} with payload containing the event.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    SubKeySpaceItem = 5,

    /// <summary>
    /// Subkey keyspaceevent notification: __subkeyspaceevent@{db}__:{event}|{key} with payload containing the subkey.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    SubKeySpaceEvent = 6,
}

/// <summary>
/// Represents keyspace and keyevent notifications, with utility methods for accessing the component data. Additionally,
/// since notifications can be high volume, a range of utility APIs is provided for avoiding allocations, in particular
/// to assist in filtering and inspecting the key <em>without</em> performing string allocations and substring operations.
/// In particular, note that this allows use with the alt-lookup (span-based) APIs on dictionaries.
/// </summary>
public readonly ref partial struct KeyNotification
{
    // effectively we just wrap a channel, but: we've pre-validated that things make sense
    private readonly RedisChannel _channel;
    private readonly RedisValue _value;
    private readonly int _keyOffset; // used to efficiently strip key prefixes
    private readonly KeyNotificationKind _kind; // the type of notification

    // this type has been designed with the intent of being able to move the entire thing alloc-free in some future
    // high-throughput callback, potentially with a ReadOnlySpan<byte> field for the key fragment; this is
    // not implemented currently, but is why this is a ref struct

    /// <summary>
    /// Gets the kind of keyspace notification this represents.
    /// </summary>
    public KeyNotificationKind Kind => _kind;

    /// <summary>
    /// Indicates whether this notification includes a sub-key (hash field).
    /// </summary>
    /// <remarks>This is true for SubKeySpace, SubKeyEvent, SubKeySpaceItem, and SubKeySpaceEvent notifications (Redis 8.8+).</remarks>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    public bool HasSubKey
    {
        get => _kind is KeyNotificationKind.SubKeySpace
                     or KeyNotificationKind.SubKeyEvent
                     or KeyNotificationKind.SubKeySpaceItem
                     or KeyNotificationKind.SubKeySpaceEvent;
    }

    /// <summary>
    /// If the channel is a keyspace, keyevent, subkeyspace, subkeyevent, subkeyspaceitem, or subkeyeventitem notification, resolve the key and event type.
    /// </summary>
    public static bool TryParse(scoped in RedisChannel channel, scoped in RedisValue value, out KeyNotification notification)
    {
        // validate that it looks reasonable
        var span = channel.Span;

        // Check for SubKeySpaceEvent prefix first (it's the longest: 20 chars)
        if (span.Length >= SubKeySpaceEventPrefix.Length + MinSuffixBytes)
        {
            var prefix = span.Slice(0, SubKeySpaceEventPrefix.Length);
            var hashCS = AsciiHash.HashCS(prefix);
            if (SubKeySpaceEventPrefix.IsCS(prefix, hashCS))
            {
                // __subkeyspaceevent@<db>__:<event>|<key> - check for __: followed by something
                if (span.Slice(SubKeySpaceEventPrefix.Length).IndexOf("__:"u8) > 0)
                {
                    notification = new KeyNotification(in channel, in value, KeyNotificationKind.SubKeySpaceEvent);
                    return true;
                }
            }
        }

        // Check for SubKeySpaceItem prefix (19 chars)
        if (span.Length >= SubKeySpaceItemPrefix.Length + MinSuffixBytes)
        {
            var prefix = span.Slice(0, SubKeySpaceItemPrefix.Length);
            var hashCS = AsciiHash.HashCS(prefix);
            if (SubKeySpaceItemPrefix.IsCS(prefix, hashCS))
            {
                // __subkeyspaceitem@<db>__:<key>\n<subkey> - check for __: followed by something
                if (span.Slice(SubKeySpaceItemPrefix.Length).IndexOf("__:"u8) > 0)
                {
                    notification = new KeyNotification(in channel, in value, KeyNotificationKind.SubKeySpaceItem);
                    return true;
                }
            }
        }

        // Check for the subkey prefixes (14 chars: __subkeyspace@, __subkeyevent@)
        if (span.Length >= SubKeySpacePrefix.Length + MinSuffixBytes)
        {
            var prefix = span.Slice(0, SubKeySpacePrefix.Length);
            var hashCS = AsciiHash.HashCS(prefix);
            switch (hashCS)
            {
                case SubKeySpacePrefix.HashCS when SubKeySpacePrefix.IsCS(prefix, hashCS):
                    // check that there is *something* non-empty after the prefix, with __: as the suffix
                    if (span.Slice(SubKeySpacePrefix.Length).IndexOf("__:"u8) > 0)
                    {
                        notification = new KeyNotification(in channel, in value, KeyNotificationKind.SubKeySpace);
                        return true;
                    }
                    break;

                case SubKeyEventPrefix.HashCS when SubKeyEventPrefix.IsCS(prefix, hashCS):
                    // check that there is *something* non-empty after the prefix, with __: as the suffix
                    if (span.Slice(SubKeySpacePrefix.Length).IndexOf("__:"u8) > 0)
                    {
                        notification = new KeyNotification(in channel, in value, KeyNotificationKind.SubKeyEvent);
                        return true;
                    }
                    break;
            }
        }

        // Check for basic keyspace/keyevent prefixes (11 chars: __keyspace@, __keyevent@)
        if (span.Length >= KeySpacePrefix.Length + MinSuffixBytes)
        {
            // check that the prefix is valid, i.e. "__keyspace@", "__keyevent@"
            var prefix = span.Slice(0, KeySpacePrefix.Length);
            var hashCS = AsciiHash.HashCS(prefix);
            switch (hashCS)
            {
                case KeyEventPrefix.HashCS when KeyEventPrefix.IsCS(prefix, hashCS):
                    // check that there is *something* non-empty after the prefix, with __: as the suffix (we don't verify *what*)
                    if (span.Slice(KeySpacePrefix.Length).IndexOf("__:"u8) > 0)
                    {
                        notification = new KeyNotification(in channel, in value, KeyNotificationKind.KeyEvent);
                        return true;
                    }
                    break;

                case KeySpacePrefix.HashCS when KeySpacePrefix.IsCS(prefix, hashCS):
                    // check that there is *something* non-empty after the prefix, with __: as the suffix (we don't verify *what*)
                    if (span.Slice(KeySpacePrefix.Length).IndexOf("__:"u8) > 0)
                    {
                        notification = new KeyNotification(in channel, in value, KeyNotificationKind.KeySpace);
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

    internal KeyNotification(scoped in RedisChannel channel, scoped in RedisValue value, KeyNotificationKind kind)
    {
        _channel = channel;
        _value = value;
        _keyOffset = 0;
        _kind = kind;
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

            // Determine the prefix length based on the notification kind
            var fullSpan = _channel.Span;
            int prefixLength = _kind switch
            {
                KeyNotificationKind.SubKeySpaceEvent => SubKeySpaceEventPrefix.Length,
                KeyNotificationKind.SubKeySpaceItem => SubKeySpaceItemPrefix.Length,
                KeyNotificationKind.SubKeySpace or KeyNotificationKind.SubKeyEvent => SubKeySpacePrefix.Length,
                _ => KeySpacePrefix.Length, // KeySpace, KeyEvent, and Unknown
            };

            var span = fullSpan.Slice(prefixLength);
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
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpace:
                // Channel contains the key, payload contains the event-type
                return ChannelSuffix.Slice(_keyOffset).ToArray();

            case KeyNotificationKind.KeyEvent:
                // Channel contains the event-type, payload contains the key
                byte[]? blob = _value;
                if (_keyOffset != 0 & blob is not null)
                {
                    return blob.AsSpan(_keyOffset).ToArray();
                }
                return blob;

            case KeyNotificationKind.SubKeyEvent:
                // __subkeyevent@<db>__:<event> with payload <key_len>:<key>|<len>:<subkey>
                var value = ExtractLengthPrefixedValue(_value, 0);
                if (value.IsNull) return RedisKey.Null;
                byte[]? bytes = value;
                if (_keyOffset != 0 && bytes is not null)
                {
                    return bytes.AsSpan(_keyOffset).ToArray();
                }
                return bytes;

            case KeyNotificationKind.SubKeySpaceItem:
                // __subkeyspaceitem@<db>__:<key>\n<subkey> with payload <event>
                var suffix = ChannelSuffix;
                var newlineIndex = suffix.IndexOf((byte)'\n');
                if (newlineIndex > 0)
                {
                    return suffix.Slice(_keyOffset, newlineIndex - _keyOffset).ToArray();
                }
                return RedisKey.Null;

            case KeyNotificationKind.SubKeySpaceEvent:
                // __subkeyspaceevent@<db>__:<event>|<key> with payload <len>:<subkey>
                var suffixEvent = ChannelSuffix;
                var pipeIndex = suffixEvent.IndexOf((byte)'|');
                if (pipeIndex >= 0 && pipeIndex + 1 < suffixEvent.Length)
                {
                    return suffixEvent.Slice(pipeIndex + 1 + _keyOffset).ToArray();
                }
                return RedisKey.Null;

            default:
                return RedisKey.Null;
        }
    }

    // Helper to extract a value prefixed with its length, e.g., "5:hello" -> "hello"
    internal static RedisValue ExtractLengthPrefixedValue(in RedisValue value, int offset)
    {
        if (value.TryGetSpan(out var span))
        {
            return ExtractLengthPrefixedValue(span.Slice(offset));
        }

        // Slower path for non-contiguous values
        const int MAX_STACK = 256;
        byte[]? lease = null;
        var maxCount = value.GetMaxByteCount();
        Span<byte> buffer = maxCount <= MAX_STACK
            ? stackalloc byte[MAX_STACK]
            : (lease = ArrayPool<byte>.Shared.Rent(maxCount));
        var bytesWritten = value.CopyTo(buffer);
        var result = ExtractLengthPrefixedValue(buffer.Slice(offset, bytesWritten - offset));
        if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        return result;
    }

    internal static RedisValue ExtractLengthPrefixedValue(ReadOnlySpan<byte> span)
    {
        var colonIndex = span.IndexOf((byte)':');
        if (colonIndex > 0 && Utf8Parser.TryParse(span.Slice(0, colonIndex), out int length, out _))
        {
            var startIndex = colonIndex + 1;
            if (startIndex + length <= span.Length)
            {
                return span.Slice(startIndex, length).ToArray();
            }
        }
        return RedisValue.Null;
    }

    /// <summary>
    /// Get the number of bytes in the key.
    /// </summary>
    /// <remarks>If a scratch-buffer is required, it may be preferable to use <see cref="GetKeyMaxByteCount"/>, which is less expensive.</remarks>
    public int GetKeyByteCount() => _kind switch
    {
        KeyNotificationKind.KeySpace or KeyNotificationKind.SubKeySpace => ChannelSuffix.Length - _keyOffset,
        KeyNotificationKind.KeyEvent or KeyNotificationKind.SubKeyEvent => _value.GetByteCount() - _keyOffset,
        _ => 0,
    };

    /// <summary>
    /// Get the maximum number of bytes in the key.
    /// </summary>
    public int GetKeyMaxByteCount() => _kind switch
    {
        KeyNotificationKind.KeySpace or KeyNotificationKind.SubKeySpace => ChannelSuffix.Length - _keyOffset,
        KeyNotificationKind.KeyEvent or KeyNotificationKind.SubKeyEvent => _value.GetMaxByteCount() - _keyOffset,
        _ => 0,
    };

    /// <summary>
    /// Get the maximum number of characters in the key, interpreting as UTF8.
    /// </summary>
    public int GetKeyMaxCharCount() => _kind switch
    {
        KeyNotificationKind.KeySpace or KeyNotificationKind.SubKeySpace => Encoding.UTF8.GetMaxCharCount(ChannelSuffix.Length - _keyOffset),
        KeyNotificationKind.KeyEvent or KeyNotificationKind.SubKeyEvent => _value.GetMaxCharCount() - _keyOffset,
        _ => 0,
    };

    /// <summary>
    /// Get the number of characters in the key, interpreting as UTF8.
    /// </summary>
    /// <remarks>If a scratch-buffer is required, it may be preferable to use <see cref="GetKeyMaxCharCount"/>, which is less expensive.</remarks>
    public int GetKeyCharCount()
    {
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpace:
                return Encoding.UTF8.GetCharCount(ChannelSuffix.Slice(_keyOffset));

            case KeyNotificationKind.KeyEvent:
            case KeyNotificationKind.SubKeyEvent:
                return _keyOffset == 0 ? _value.GetCharCount() : SlowMeasure(in this);

            default:
                return 0;
        }

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
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpace:
                // Key is in the channel suffix
                var suffix = ChannelSuffix.Slice(_keyOffset);
                bytesWritten = suffix.Length; // assume success
                if (bytesWritten <= destination.Length)
                {
                    suffix.CopyTo(destination);
                    return true;
                }
                return false;

            case KeyNotificationKind.KeyEvent:
                // Key is in the value/payload (plain key, not length-prefixed)
                if (_value.TryGetSpan(out var direct))
                {
                    bytesWritten = direct.Length - _keyOffset;
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

            case KeyNotificationKind.SubKeyEvent:
                // Key is length-prefixed in payload: <key_len>:<key>|<len>:<subkey>
                var keyValue = ExtractLengthPrefixedValue(_value, 0);
                if (keyValue.IsNull)
                {
                    bytesWritten = 0;
                    return false;
                }

                if (keyValue.TryGetSpan(out var keySpan))
                {
                    var slicedSpan = keySpan.Slice(_keyOffset);
                    bytesWritten = slicedSpan.Length;
                    if (bytesWritten <= destination.Length)
                    {
                        slicedSpan.CopyTo(destination);
                        return true;
                    }
                    return false;
                }

                byte[]? keyBytes = keyValue;
                if (_keyOffset != 0 && keyBytes is not null)
                {
                    bytesWritten = keyBytes.Length - _keyOffset;
                    if (bytesWritten <= destination.Length)
                    {
                        keyBytes.AsSpan(_keyOffset).CopyTo(destination);
                        return true;
                    }
                    return false;
                }

                bytesWritten = keyValue.GetByteCount();
                if (bytesWritten <= destination.Length)
                {
                    keyValue.CopyTo(destination);
                    return true;
                }
                bytesWritten = 0;
                return false;

            case KeyNotificationKind.SubKeySpaceItem:
                // Key is in channel: __subkeyspaceitem@<db>__:<key>\n<subkey>
                var suffixItem = ChannelSuffix;
                var newlineIndex = suffixItem.IndexOf((byte)'\n');
                if (newlineIndex > 0)
                {
                    var keySpanItem = suffixItem.Slice(_keyOffset, newlineIndex - _keyOffset);
                    bytesWritten = keySpanItem.Length;
                    if (bytesWritten <= destination.Length)
                    {
                        keySpanItem.CopyTo(destination);
                        return true;
                    }
                    return false;
                }
                bytesWritten = 0;
                return false;

            case KeyNotificationKind.SubKeySpaceEvent:
                // Key is in channel: __subkeyspaceevent@<db>__:<event>|<key>
                var suffixEvent = ChannelSuffix;
                var pipeIndex = suffixEvent.IndexOf((byte)'|');
                if (pipeIndex >= 0 && pipeIndex + 1 < suffixEvent.Length)
                {
                    var keySpanEvent = suffixEvent.Slice(pipeIndex + 1 + _keyOffset);
                    bytesWritten = keySpanEvent.Length;
                    if (bytesWritten <= destination.Length)
                    {
                        keySpanEvent.CopyTo(destination);
                        return true;
                    }
                    return false;
                }
                bytesWritten = 0;
                return false;

            default:
                bytesWritten = 0;
                return false;
        }

        static bool SlowCopy(in KeyNotification value, Span<byte> destination, out int bytesWritten)
        {
            var span = value.GetKeySpan(out var lease, stackalloc byte[128]);
            bool result = span.TryCopyTo(destination);
            bytesWritten = result ? span.Length : 0;
            Return(lease);
            return result;
        }
    }

    /// <summary>
    /// Attempt to copy the bytes from the key to a buffer, returning the number of bytes written.
    /// </summary>
    public bool TryCopyKey(Span<char> destination, out int charsWritten)
    {
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpace:
                // Key is in the channel suffix
                var suffix = ChannelSuffix.Slice(_keyOffset);
                if (Encoding.UTF8.GetMaxCharCount(suffix.Length) <= destination.Length ||
                    Encoding.UTF8.GetCharCount(suffix) <= destination.Length)
                {
                    charsWritten = Encoding.UTF8.GetChars(suffix, destination);
                    return true;
                }
                charsWritten = 0;
                return false;

            case KeyNotificationKind.KeyEvent:
                // Key is in the value/payload (plain key, not length-prefixed)
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

            case KeyNotificationKind.SubKeyEvent:
                // Key is length-prefixed in payload: <key_len>:<key>|<len>:<subkey>
                var keyValue = ExtractLengthPrefixedValue(_value, 0);
                if (keyValue.IsNull)
                {
                    charsWritten = 0;
                    return false;
                }

                if (_keyOffset == 0)
                {
                    if (keyValue.GetMaxCharCount() <= destination.Length || keyValue.GetCharCount() <= destination.Length)
                    {
                        charsWritten = keyValue.CopyTo(destination);
                        return true;
                    }
                }
                else
                {
                    // Need to slice the extracted key value
                    byte[]? keyBytes = keyValue;
                    if (keyBytes is not null)
                    {
                        var keySpan = keyBytes.AsSpan(_keyOffset);
                        if (Encoding.UTF8.GetMaxCharCount(keySpan.Length) <= destination.Length ||
                            Encoding.UTF8.GetCharCount(keySpan) <= destination.Length)
                        {
                            charsWritten = Encoding.UTF8.GetChars(keySpan, destination);
                            return true;
                        }
                    }
                }
                charsWritten = 0;
                return false;

            case KeyNotificationKind.SubKeySpaceItem:
                // Key is in channel: __subkeyspaceitem@<db>__:<key>\n<subkey>
                var suffixItem = ChannelSuffix;
                var newlineIndex = suffixItem.IndexOf((byte)'\n');
                if (newlineIndex > 0)
                {
                    var keyBytes = suffixItem.Slice(_keyOffset, newlineIndex - _keyOffset);
                    if (Encoding.UTF8.GetMaxCharCount(keyBytes.Length) <= destination.Length ||
                        Encoding.UTF8.GetCharCount(keyBytes) <= destination.Length)
                    {
                        charsWritten = Encoding.UTF8.GetChars(keyBytes, destination);
                        return true;
                    }
                }
                charsWritten = 0;
                return false;

            case KeyNotificationKind.SubKeySpaceEvent:
                // Key is in channel: __subkeyspaceevent@<db>__:<event>|<key>
                var suffixEvent = ChannelSuffix;
                var pipeIndex = suffixEvent.IndexOf((byte)'|');
                if (pipeIndex >= 0 && pipeIndex + 1 < suffixEvent.Length)
                {
                    var keyBytes = suffixEvent.Slice(pipeIndex + 1 + _keyOffset);
                    if (Encoding.UTF8.GetMaxCharCount(keyBytes.Length) <= destination.Length ||
                        Encoding.UTF8.GetCharCount(keyBytes) <= destination.Length)
                    {
                        charsWritten = Encoding.UTF8.GetChars(keyBytes, destination);
                        return true;
                    }
                }
                charsWritten = 0;
                return false;

            default:
                charsWritten = 0;
                return false;
        }
    }

    /// <summary>
    /// Get the portion of the channel after the "__{keyspace|keyevent}@{db}__:".
    /// </summary>
    internal ReadOnlySpan<byte> ChannelSuffix
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
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpaceItem:
                // Type is in the payload/value
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

            case KeyNotificationKind.KeyEvent:
            case KeyNotificationKind.SubKeyEvent:
                // Type is in the channel suffix
                return ChannelSuffix.SequenceEqual(type);

            case KeyNotificationKind.SubKeySpace:
                // Type is before the | in the payload
                if (_value.TryGetSpan(out var directSub))
                {
                    var pipeIndex = directSub.IndexOf((byte)'|');
                    if (pipeIndex > 0)
                    {
                        return directSub.Slice(0, pipeIndex).SequenceEqual(type);
                    }
                }

                byte[]? leaseSub = null;
                var maxCountSub = _value.GetMaxByteCount();
                Span<byte> localCopySub = maxCountSub <= MAX_STACK
                    ? stackalloc byte[MAX_STACK]
                    : (leaseSub = ArrayPool<byte>.Shared.Rent(maxCountSub));
                var countSub = _value.CopyTo(localCopySub);
                var pipeIndexSub = localCopySub.Slice(0, countSub).IndexOf((byte)'|');
                bool resultSub = pipeIndexSub > 0 && localCopySub.Slice(0, pipeIndexSub).SequenceEqual(type);
                if (leaseSub is not null) ArrayPool<byte>.Shared.Return(leaseSub);
                return resultSub;

            case KeyNotificationKind.SubKeySpaceEvent:
                // Type is in the channel suffix before the |
                var suffix = ChannelSuffix;
                var pipeIndexEvent = suffix.IndexOf((byte)'|');
                if (pipeIndexEvent > 0)
                {
                    return suffix.Slice(0, pipeIndexEvent).SequenceEqual(type);
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// The type of notification associated with this event, if it is well-known - otherwise <see cref="KeyNotificationType.Unknown"/>.
    /// </summary>
    /// <remarks>Unexpected values can be processed manually from the <see cref="GetChannel()"/> and <see cref="GetValue()"/>.</remarks>
    public KeyNotificationType Type
    {
        get
        {
            switch (_kind)
            {
                case KeyNotificationKind.KeySpace:
                case KeyNotificationKind.SubKeySpaceItem:
                    // Payload contains the event-type
                    if (_value.TryGetSpan(out var direct))
                    {
                        return KeyNotificationTypeMetadata.Parse(direct);
                    }

                    if (_value.GetByteCount() <= KeyNotificationTypeMetadata.BufferBytes)
                    {
                        Span<byte> localCopy = stackalloc byte[KeyNotificationTypeMetadata.BufferBytes];
                        var len = _value.CopyTo(localCopy);
                        return KeyNotificationTypeMetadata.Parse(localCopy.Slice(0, len));
                    }
                    return KeyNotificationType.Unknown;

                case KeyNotificationKind.KeyEvent:
                case KeyNotificationKind.SubKeyEvent:
                    // Channel contains the event-type
                    return KeyNotificationTypeMetadata.Parse(ChannelSuffix);

                case KeyNotificationKind.SubKeySpace:
                    // Payload contains <event>|<len>:<subkey>[|<len>:<subkey>...]
                    if (_value.TryGetSpan(out var directSub))
                    {
                        var pipeIndexSub = directSub.IndexOf((byte)'|');
                        if (pipeIndexSub > 0)
                        {
                            return KeyNotificationTypeMetadata.Parse(directSub.Slice(0, pipeIndexSub));
                        }
                    }

                    // Need to copy the value to find the pipe - the event type is before the first |
                    byte[]? leaseSub = null;
                    var byteCountSub = _value.GetByteCount();
                    Span<byte> localCopySub = byteCountSub <= KeyNotificationTypeMetadata.BufferBytes
                        ? stackalloc byte[KeyNotificationTypeMetadata.BufferBytes]
                        : (leaseSub = ArrayPool<byte>.Shared.Rent(byteCountSub));
                    var lenSub = _value.CopyTo(localCopySub);
                    var pipeIndexSub2 = localCopySub.Slice(0, lenSub).IndexOf((byte)'|');
                    KeyNotificationType resultSub = pipeIndexSub2 > 0
                        ? KeyNotificationTypeMetadata.Parse(localCopySub.Slice(0, pipeIndexSub2))
                        : KeyNotificationType.Unknown;
                    if (leaseSub is not null) ArrayPool<byte>.Shared.Return(leaseSub);
                    return resultSub;

                case KeyNotificationKind.SubKeySpaceEvent:
                    // Channel contains event|key - extract the event part before the pipe
                    var suffix = ChannelSuffix;
                    var pipeIndexEvent = suffix.IndexOf((byte)'|');
                    if (pipeIndexEvent > 0)
                    {
                        return KeyNotificationTypeMetadata.Parse(suffix.Slice(0, pipeIndexEvent));
                    }
                    return KeyNotificationType.Unknown;

                default:
                    return KeyNotificationType.Unknown;
            }
        }
    }

    /// <summary>
    /// Indicates whether this notification originated from a keyspace notification, for example <c>__keyspace@4__:mykey</c> with payload <c>set</c>.
    /// </summary>
    [Obsolete($"Prefer {nameof(KeyNotification)}.{nameof(Kind)}", error: false)]
    public bool IsKeySpace => _kind == KeyNotificationKind.KeySpace;

    /// <summary>
    /// Indicates whether this notification originated from a keyevent notification, for example <c>__keyevent@4__:set</c> with payload <c>mykey</c>.
    /// </summary>
    [Obsolete($"Prefer {nameof(KeyNotification)}.{nameof(Kind)}", error: false)]
    public bool IsKeyEvent => _kind == KeyNotificationKind.KeyEvent;

    /// <summary>
    /// Indicates whether the key associated with this notification starts with the specified prefix.
    /// </summary>
    /// <returns>This API is intended as a high-throughput filter API.</returns>
    public bool KeyStartsWith(ReadOnlySpan<byte> prefix) // intentionally leading people to the BLOB API
    {
        switch (_kind)
        {
            case KeyNotificationKind.KeySpace:
            case KeyNotificationKind.SubKeySpace:
                // Key is in the channel suffix
                return ChannelSuffix.Slice(_keyOffset).StartsWith(prefix);

            case KeyNotificationKind.KeyEvent:
                // Key is in the value/payload (plain key, not length-prefixed)
                if (_keyOffset == 0) return _value.StartsWith(prefix);

                var span = GetKeySpan(out var lease, stackalloc byte[128]);
                bool result = span.StartsWith(prefix);
                Return(lease);
                return result;

            case KeyNotificationKind.SubKeyEvent:
                // Key is length-prefixed in payload: <key_len>:<key>|<len>:<subkey>
                var keyValue = ExtractLengthPrefixedValue(_value, 0);
                if (keyValue.IsNull) return false;
                if (_keyOffset == 0) return keyValue.StartsWith(prefix);

                // Need to check the sliced portion
                byte[]? keyBytes = keyValue;
                if (keyBytes is not null && _keyOffset < keyBytes.Length)
                {
                    return keyBytes.AsSpan(_keyOffset).StartsWith(prefix);
                }
                return false;

            case KeyNotificationKind.SubKeySpaceItem:
                // Key is in channel: __subkeyspaceitem@<db>__:<key>\n<subkey>
                var suffixItem = ChannelSuffix;
                var newlineIndex = suffixItem.IndexOf((byte)'\n');
                if (newlineIndex > 0)
                {
                    var keySpan = suffixItem.Slice(_keyOffset, newlineIndex - _keyOffset);
                    return keySpan.StartsWith(prefix);
                }
                return false;

            case KeyNotificationKind.SubKeySpaceEvent:
                // Key is in channel: __subkeyspaceevent@<db>__:<event>|<key>
                var suffixEvent = ChannelSuffix;
                var pipeIndex = suffixEvent.IndexOf((byte)'|');
                if (pipeIndex >= 0 && pipeIndex + 1 < suffixEvent.Length)
                {
                    var keySpan = suffixEvent.Slice(pipeIndex + 1 + _keyOffset);
                    return keySpan.StartsWith(prefix);
                }
                return false;

            default:
                return false;
        }
    }
}

internal static partial class KeyNotificationChannels
{
    [AsciiHash("__keyspace@")]
    internal static partial class KeySpacePrefix
    {
    }

    [AsciiHash("__keyevent@")]
    internal static partial class KeyEventPrefix
    {
    }

    [AsciiHash("__subkeyspace@")]
    internal static partial class SubKeySpacePrefix
    {
    }

    [AsciiHash("__subkeyevent@")]
    internal static partial class SubKeyEventPrefix
    {
    }

    [AsciiHash("__subkeyspaceitem@")]
    internal static partial class SubKeySpaceItemPrefix
    {
    }

    [AsciiHash("__subkeyspaceevent@")]
    internal static partial class SubKeySpaceEventPrefix
    {
    }
}
