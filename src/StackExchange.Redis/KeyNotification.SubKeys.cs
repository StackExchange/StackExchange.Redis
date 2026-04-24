using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using RESPite;

namespace StackExchange.Redis;

public readonly ref partial struct KeyNotification
{
    /// <summary>
    /// Gets all sub-keys in this notification. For notifications without sub-keys, returns an empty enumerable.
    /// </summary>
    /// <remarks>This method is available for SubKeySpace, SubKeyEvent, SubKeySpaceItem, and SubKeySpaceEvent notification types.</remarks>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    public SubKeyEnumerable GetSubKeys()
    {
        return new SubKeyEnumerable(this);
    }

    /// <summary>
    /// Provides enumeration over sub-keys in a keyspace notification.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    public readonly ref struct SubKeyEnumerable
    {
        private static readonly RedisValue[] Empty = [];
        private static readonly RedisValue[] Multiple = [RedisValue.Null, RedisValue.Null];
        private readonly KeyNotification _notification;

        internal SubKeyEnumerable(KeyNotification notification)
        {
            _notification = notification;
        }

        /// <summary>
        /// Gets an enumerator for the sub-keys.
        /// </summary>
        public SubKeyEnumerator GetEnumerator() => new SubKeyEnumerator(_notification);

        /// <summary>
        /// Gets the number of sub-keys in this notification.
        /// </summary>
        public int Count()
        {
            var count = 0;
            using var enumerator = GetEnumerator();
            while (enumerator.TryMoveNext(setCurrent: false))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Gets the first sub-key in this notification.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence contains no elements.</exception>
        public RedisValue First()
        {
            foreach (var subKey in this)
            {
                return subKey;
            }
            return Empty.First(); // for error consistency
        }

        /// <summary>
        /// Gets the first sub-key in this notification, or a null value if the sequence is empty.
        /// </summary>
        public RedisValue FirstOrDefault()
        {
            foreach (var subKey in this)
            {
                return subKey;
            }
            return RedisValue.Null;
        }

        /// <summary>
        /// Gets the only sub-key in this notification.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence contains no elements, or more than one element.</exception>
        public RedisValue Single()
        {
            using var enumerator = GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return Empty.Single(); // for error consistency
            }
            var result = enumerator.Current;
            if (enumerator.MoveNext())
            {
                return Multiple.Single(); // for error consistency
            }
            return result;
        }

        /// <summary>
        /// Gets the only sub-key in this notification, or a null value if the sequence is empty.
        /// </summary>
        /// <exception cref="InvalidOperationException">The sequence contains more than one element.</exception>
        public RedisValue SingleOrDefault()
        {
            using var enumerator = GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return RedisValue.Null;
            }
            var result = enumerator.Current;
            if (enumerator.MoveNext())
            {
                return Multiple.Single(); // for error consistency
            }
            return result;
        }

        /// <summary>
        /// Tries to copy all sub-keys to the specified span.
        /// </summary>
        /// <param name="destination">The span to copy sub-keys into.</param>
        /// <param name="count">The number of sub-keys copied.</param>
        /// <returns><c>true</c> if all sub-keys were copied; <c>false</c> if the destination was too small (partial copy).</returns>
        public bool TryCopyTo(Span<RedisValue> destination, out int count)
        {
            count = 0;
            foreach (var subKey in this)
            {
                if (count >= destination.Length)
                {
                    return false; // Destination too small, partial copy
                }
                destination[count++] = subKey;
            }
            return true; // All sub-keys copied
        }

        /// <summary>
        /// Copies all sub-keys to the specified span.
        /// </summary>
        /// <param name="destination">The span to copy sub-keys into.</param>
        /// <returns>The number of sub-keys copied. If the destination is too small, only as many sub-keys as will fit are copied.</returns>
        public int CopyTo(Span<RedisValue> destination)
        {
            _ = TryCopyTo(destination, out var count);
            return count;
        }

        /// <summary>
        /// Converts the sub-keys to an array.
        /// </summary>
        public RedisValue[] ToArray()
        {
            var count = Count();
            if (count == 0) return Empty;

            var array = new RedisValue[count];
            var index = 0;
            foreach (var subKey in this)
            {
                array[index++] = subKey;
            }
            return array;
        }

        /// <summary>
        /// Converts the sub-keys to a list.
        /// </summary>
        public List<RedisValue> ToList()
        {
            var count = Count();
            var list = new List<RedisValue>(count);
            foreach (var subKey in this)
            {
                list.Add(subKey);
            }
            return list;
        }
    }

    /// <summary>
    /// Enumerator for sub-keys in a keyspace notification.
    /// </summary>
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    public ref struct SubKeyEnumerator
    {
        private readonly KeyNotificationKind _kind;
        private ReadOnlySpan<byte> _data;
        private byte[]? _lease;
        private int _position;
        private RedisValue _current;

        internal SubKeyEnumerator(scoped KeyNotification notification)
        {
            _kind = notification._kind;
            _lease = null;
            _position = 0;
            _current = default;

            // Always copy the relevant data to a leased buffer to avoid lifetime issues
            switch (_kind)
            {
                case KeyNotificationKind.SubKeySpace:
                case KeyNotificationKind.SubKeyEvent:
                    // Payload: <event>|<len>:<subkey>[|<len>:<subkey>...] or <key_len>:<key>|<len>:<subkey>[|<len>:<subkey>...]
                    // We need to skip to the first | and then iterate through length-prefixed subkeys
                    _data = CopyAndLeaseValue(notification._value, out _lease);

                    // Find the first pipe to skip the event/key part
                    var firstPipe = _data.IndexOf((byte)'|');
                    if (firstPipe >= 0 && firstPipe + 1 < _data.Length)
                    {
                        _position = firstPipe + 1; // Start after the first |
                    }
                    else
                    {
                        _position = _data.Length; // No subkeys
                    }
                    break;

                case KeyNotificationKind.SubKeySpaceItem:
                    // Channel: __subkeyspaceitem@<db>__:<key>\n<subkey>
                    // Single subkey only - extract from channel suffix after \n
                    var suffix = notification.ChannelSuffix;
                    var newlineIndex = suffix.IndexOf((byte)'\n');
                    if (newlineIndex >= 0 && newlineIndex + 1 < suffix.Length)
                    {
                        // Copy the subkey part to a leased buffer
                        var subkeySpan = suffix.Slice(newlineIndex + 1);
                        var buffer = _lease = ArrayPool<byte>.Shared.Rent(subkeySpan.Length);
                        subkeySpan.CopyTo(buffer);
                        _data = buffer.AsSpan(0, subkeySpan.Length);
                        _position = 0; // Will return this single value
                    }
                    else
                    {
                        _data = default;
                        _position = 0;
                    }
                    break;

                case KeyNotificationKind.SubKeySpaceEvent:
                    // Payload: <len>:<subkey>[|<len>:<subkey>...]
                    _data = CopyAndLeaseValue(notification._value, out _lease);
                    _position = 0;
                    break;

                default:
                    // No subkeys for other notification types
                    _data = default;
                    _position = 0;
                    break;
            }
        }

        private static ReadOnlySpan<byte> CopyAndLeaseValue(RedisValue value, out byte[] lease)
        {
            // Always lease a buffer and copy - we can't return a span directly from RedisValue
            // because it may reference data in the notification parameter which has limited lifetime
            var byteCount = value.GetByteCount();
            var buffer = lease = ArrayPool<byte>.Shared.Rent(byteCount);
            var written = value.CopyTo(buffer);
            return buffer.AsSpan(0, written);
        }

        /// <summary>
        /// Gets the current sub-key.
        /// </summary>
        public RedisValue Current => _current;

        /// <summary>
        /// Advances to the next sub-key.
        /// </summary>
        public bool MoveNext() => TryMoveNext(setCurrent: true);

        internal bool TryMoveNext(bool setCurrent)
        {
            if (_position >= _data.Length)
            {
                return false;
            }

            switch (_kind)
            {
                case KeyNotificationKind.SubKeySpaceItem:
                    // Single subkey - return it once
                    if (_position == 0)
                    {
                        if (setCurrent)
                        {
                            _current = _data.ToArray();
                        }
                        _position = _data.Length; // Mark as consumed
                        return true;
                    }
                    return false;

                case KeyNotificationKind.SubKeySpace:
                case KeyNotificationKind.SubKeyEvent:
                case KeyNotificationKind.SubKeySpaceEvent:
                    // Length-prefixed format: <len>:<subkey>
                    var value = KeyNotification.ExtractLengthPrefixedValue(_data.Slice(_position));
                    if (value.IsNull)
                    {
                        return false;
                    }

                    if (setCurrent)
                    {
                        _current = value;
                    }

                    // Move position forward: skip the length prefix + colon + value + pipe (if present)
                    var remaining = _data.Slice(_position);
                    var colonIndex = remaining.IndexOf((byte)':');
                    if (colonIndex < 0) return false;

                    var valueLength = (int)value.Length();
                    _position += colonIndex + 1 + valueLength;

                    // Skip the separator if present (| or ,)
                    if (_position < _data.Length && (_data[_position] == (byte)'|' || _data[_position] == (byte)','))
                    {
                        _position++;
                    }

                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Releases any leased buffers.
        /// </summary>
        public void Dispose()
        {
            if (_lease is not null)
            {
                ArrayPool<byte>.Shared.Return(_lease);
                _lease = null;
            }
        }
    }
}
