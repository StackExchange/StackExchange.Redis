using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using StackExchange.Redis.Resp;

namespace StackExchange.Redis
{
    internal readonly struct RawResult
    {
        [Obsolete("This API is inefficient and should be avoided.")]
        internal RespReader this[int index]
        {
            get
            {
                if (index < 0) throw new IndexOutOfRangeException(nameof(index));
                var items = GetItems();
                int i = 0;
                for (; i < index && items.MoveNext(); i++) { }
                if (i != index) throw new IndexOutOfRangeException(nameof(index));
                return items.Value;
            }
        }

        private RespReader InitReader()
        {
            RespReader reader = new(_rawFragment);
            reader.MoveNext();
            return reader;
        }
        internal int ItemsCount()
        {
            var reader = new RespReader(_rawFragment);
            return reader.TryMoveNext(checkError: false) && reader.IsAggregate
                ? reader.AggregateLength() : 0;
        }

        internal int ScalarLength()
        {
            var reader = new RespReader(_rawFragment);
            return reader.TryMoveNext(checkError: false) && reader.IsScalar
                ? reader.ScalarLength() : 0;
        }

        private readonly ReadOnlySequence<byte> _rawFragment;

        internal static readonly RawResult Nil = default;
        private readonly ResultType _resultType;
        private readonly ResultFlags _flags;

        [Flags]
        internal enum ResultFlags
        {
            None = 0,
            HasValue = 1 << 0, // simply indicates "not the default" (always set in .ctor)
            NonNull = 1 << 1, // defines explicit null; isn't "IsNull" because we want default to be null
            Resp3 = 1 << 2, // was the connection in RESP3 mode?
        }

        public RawResult(in ReadOnlySequence<byte> fragment, ResultFlags flags)
        {
            var reader = new RespReader(fragment);
            if (!reader.TryMoveNext(checkError: false))
            {
                ThrowEOF();
            }

            var resultType = reader.Prefix switch
            {
                RespPrefix.SimpleString => ResultType.SimpleString,
                RespPrefix.SimpleError => ResultType.Error,
                RespPrefix.Array => ResultType.Array,
                RespPrefix.Integer => ResultType.Integer,
                RespPrefix.BulkString => ResultType.BulkString,
                RespPrefix.BulkError => ResultType.BlobError,
                RespPrefix.VerbatimString => ResultType.VerbatimString,
                RespPrefix.Double => ResultType.Double,
                RespPrefix.Boolean => ResultType.Boolean,
                RespPrefix.BigInteger => ResultType.BigInteger,
                RespPrefix.Null => ResultType.Null,
                RespPrefix.Map => ResultType.Map,
                RespPrefix.Set => ResultType.Set,
                RespPrefix.Push => ResultType.Push,
                RespPrefix.Attribute => ResultType.Attribute,
                _ => throw new ArgumentOutOfRangeException(nameof(reader.Prefix), $"Unexpected prefix: {reader.Prefix}"),
            };

            _flags = (flags & ~ResultFlags.NonNull) | ResultFlags.HasValue;
            if (reader.IsNull)
            {
                _flags |= ResultFlags.NonNull;
            }
            _resultType = resultType;
            _rawFragment = fragment;

            static void ThrowEOF() => throw new EndOfStreamException("Unable to read RESP fragment");
        }

        private static void ThrowInvalidType(ResultType resultType)
             => throw new ArgumentOutOfRangeException(nameof(resultType), $"Invalid result-type: {resultType}");

        public bool IsError => _resultType.IsError();

        public ResultType Resp3Type => _resultType;

        // if null, assume string
        public ResultType Resp2TypeBulkString => _resultType == ResultType.Null ? ResultType.BulkString : _resultType.ToResp2();
        // if null, assume array
        public ResultType Resp2TypeArray => _resultType == ResultType.Null ? ResultType.Array : _resultType.ToResp2();

        internal bool IsNull => (_flags & ResultFlags.NonNull) == 0;

        public bool HasValue => (_flags & ResultFlags.HasValue) != 0;

        public bool IsResp3 => (_flags & ResultFlags.Resp3) != 0;

        public override string ToString()
        {
            if (IsNull) return "(null)";

            return _resultType.ToResp2() switch
            {
                ResultType.SimpleString or ResultType.Integer or ResultType.Error => $"{Resp3Type}: {GetString()}",
                ResultType.BulkString => $"{Resp3Type}: {ScalarLength()} bytes",
                ResultType.Array => $"{Resp3Type}: {ItemsCount()} items",
                _ => $"(unknown: {Resp3Type})",
            };
        }

        internal RedisChannel AsRedisChannel(byte[]? channelPrefix, RedisChannel.RedisChannelOptions options)
        {
            switch (Resp2TypeBulkString)
            {
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    if (channelPrefix == null)
                    {
                        return new RedisChannel(GetBlob(), options);
                    }
                    if (StartsWith(channelPrefix))
                    {
                        byte[] copy = Payload.Slice(channelPrefix.Length).ToArray();

                        return new RedisChannel(copy, options);
                    }
                    return default;
                default:
                    throw new InvalidCastException("Cannot convert to RedisChannel: " + Resp3Type);
            }
        }

        internal RedisKey AsRedisKey() => InitReader().ReadRedisKey();

        internal RedisValue AsRedisValue() => InitReader().ReadRedisValue();

        internal Lease<byte>? AsLease()
        {
            if (IsNull) return null;
            var reader = InitReader();
            reader.DemandScalar();
            var len = reader.ScalarLength();
            if (len == 0) return Lease<byte>.Empty;

            var lease = Lease<byte>.Create(len);
            var actual = reader.CopyTo(lease.Span);
            Debug.Assert(actual == len);
            return lease;
        }

        internal bool IsEqual(in CommandBytes expected)
        {
            if (expected.Length != Payload.Length) return false;
            return new CommandBytes(Payload).Equals(expected);
        }

        internal unsafe bool IsEqual(byte[]? expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));

            var rangeToCheck = Payload;

            if (expected.Length != rangeToCheck.Length) return false;
            if (rangeToCheck.IsSingleSegment) return rangeToCheck.First.Span.SequenceEqual(expected);

            int offset = 0;
            foreach (var segment in rangeToCheck)
            {
                var from = segment.Span;
                var to = new Span<byte>(expected, offset, from.Length);
                if (!from.SequenceEqual(to)) return false;

                offset += from.Length;
            }
            return true;
        }

        internal bool StartsWith(in CommandBytes expected)
        {
            var len = expected.Length;
            if (len > Payload.Length) return false;

            var rangeToCheck = Payload.Slice(0, len);
            return new CommandBytes(rangeToCheck).Equals(expected);
        }

        internal bool StartsWith(byte[] expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (expected.Length > Payload.Length) return false;

            var rangeToCheck = Payload.Slice(0, expected.Length);
            if (rangeToCheck.IsSingleSegment) return rangeToCheck.First.Span.SequenceEqual(expected);

            int offset = 0;
            foreach (var segment in rangeToCheck)
            {
                var from = segment.Span;
                var to = new Span<byte>(expected, offset, from.Length);
                if (!from.SequenceEqual(to)) return false;

                offset += from.Length;
            }
            return true;
        }

        internal byte[]? GetBlob()
        {
            if (IsNull) return null;

            if (Payload.IsEmpty) return [];

            return Payload.ToArray();
        }

        internal bool GetBoolean()
        {
            if (Payload.Length != 1) throw new InvalidCastException();
            if (Resp3Type == ResultType.Boolean)
            {
                return Payload.First.Span[0] switch
                {
                    (byte)'t' => true,
                    (byte)'f' => false,
                    _ => throw new InvalidCastException(),
                };
            }
            return Payload.First.Span[0] switch
            {
                (byte)'1' => true,
                (byte)'0' => false,
                _ => throw new InvalidCastException(),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RespReader.AggregateEnumerator GetItems()
            => InitReader().AggregateChildren();

        private T[]? ToArray<T>(RespReader.Projection<T> projection)
        {
            var reader = InitReader();
            if (reader.IsNull) return null;
            reader.DemandAggregate();
            var expected = reader.AggregateLength();
            if (expected == 0) return [];

            var arr = new T[expected];
            int actual = reader.Fill(arr, projection);
            if (actual != expected) ThrowNoFill(expected, actual);
            return arr;
        }

        private Lease<T>? ToLease<T>(RespReader.Projection<T> projection)
        {
            var reader = InitReader();
            if (reader.IsNull) return null;
            reader.DemandAggregate();
            var expected = reader.AggregateLength();
            if (expected == 0) return Lease<T>.Empty;

            var lease = Lease<T>.Create(expected, clear: false);
            int actual = reader.Fill(lease.Span, projection);
            if (actual != expected) ThrowNoFill(expected, actual);
            return lease;
        }

        private static void ThrowNoFill(int expected, int actual) =>
            throw new InvalidOperationException($"A call to {nameof(RespReader.Fill)} read {actual} of {expected} elements.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double?[]? GetItemsAsDoubles() => ToArray<double?>((ref RespReader value) => value.IsNull ? null : value.ReadDouble());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisKey[]? GetItemsAsKeys() => this.ToArray<RedisKey>((ref RespReader x) => x.ReadRedisKey());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisValue[]? GetItemsAsValues() => this.ToArray<RedisValue>((ref RespReader x) => x.ReadRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string?[]? GetItemsAsStrings() => ToArray<string?>((ref RespReader value) => value.ReadString());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string[]? GetItemsAsStringsNotNullable() => ToArray<string>((ref RespReader value) => value.ReadString()!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool[]? GetItemsAsBooleans() => ToArray<bool>((ref RespReader value) => value.ReadBoolean());

        internal GeoPosition? GetItemsAsGeoPosition()
        {
            if (IsNull) return null;
            var items = GetItems();
            if (!items.MoveNext())
            {
                return null;
            }

            var root = items.Value;
            if (root.IsNull) return null;
            return AsGeoPosition(root.AggregateChildren());
        }

        internal SortedSetEntry[]? GetItemsAsSortedSetEntryArray() => this.ToArray(static (ref RespReader value) =>
        {
            var iter = value.AggregateChildren();
            if (!iter.MoveNext()) ThrowEOF();
            var element = iter.Value.ReadRedisValue();
            if (!iter.MoveNext()) ThrowEOF();
            var score = iter.Value.IsNull ? double.NaN : iter.Value.ReadDouble();
            return new SortedSetEntry(element, score);

            static void ThrowEOF() =>
                throw new EndOfStreamException("Insufficient chile elements for " + nameof(SortedSetEntry));
        });


        private static GeoPosition AsGeoPosition(RespReader.AggregateEnumerator coords)
        {
            double longitude, latitude;
            if (coords.IsSingleSegment)
            {
                var span = coords.FirstSpan;
                longitude = (double)span[0].AsRedisValue();
                latitude = (double)span[1].AsRedisValue();
            }
            else
            {
                longitude = (double)coords[0].AsRedisValue();
                latitude = (double)coords[1].AsRedisValue();
            }

            return new GeoPosition(longitude, latitude);
        }

        internal GeoPosition?[]? GetItemsAsGeoPositionArray()
            => this.ToArray<GeoPosition?>((in RawResult item) => item.IsNull ? default : AsGeoPosition(item.GetItems()));

        internal unsafe string? GetString() => GetString(out _);
        internal unsafe string? GetString(out ReadOnlySpan<char> verbatimPrefix)
        {
            verbatimPrefix = default;
            if (IsNull) return null;
            if (Payload.IsEmpty) return "";

            string s;
            if (Payload.IsSingleSegment)
            {
                s = Format.GetString(Payload.First.Span);
                return Resp3Type == ResultType.VerbatimString ? GetVerbatimString(s, out verbatimPrefix) : s;
            }
#if NET6_0_OR_GREATER
            // use system-provided sequence decoder
            return Encoding.UTF8.GetString(in _payload);
#else
            var decoder = Encoding.UTF8.GetDecoder();
            int charCount = 0;
            foreach (var segment in Payload)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;

                fixed (byte* bPtr = span)
                {
                    charCount += decoder.GetCharCount(bPtr, span.Length, false);
                }
            }

            decoder.Reset();

            s = new string((char)0, charCount);
            fixed (char* sPtr = s)
            {
                char* cPtr = sPtr;
                foreach (var segment in Payload)
                {
                    var span = segment.Span;
                    if (span.IsEmpty) continue;

                    fixed (byte* bPtr = span)
                    {
                        var written = decoder.GetChars(bPtr, span.Length, cPtr, charCount, false);
                        if (written < 0 || written > charCount) Throw(); // protect against hypothetical cPtr weirdness
                        cPtr += written;
                        charCount -= written;
                    }
                }
            }

            return Resp3Type == ResultType.VerbatimString ? GetVerbatimString(s, out verbatimPrefix) : s;

            static void Throw() => throw new InvalidOperationException("Invalid result from GetChars");
#endif
            static string? GetVerbatimString(string? value, out ReadOnlySpan<char> type)
            {
                // The first three bytes provide information about the format of the following string, which
                // can be txt for plain text, or mkd for markdown. The fourth byte is always `:`.
                // Then the real string follows.
                if (value is not null
                    && value.Length >= 4 && value[3] == ':')
                {
                    type = value.AsSpan().Slice(0, 3);
                    value = value.Substring(4);
                }
                else
                {
                    type = default;
                }
                return value;
            }
        }

        internal bool TryGetDouble(out double value)
        {
            var reader = InitReader();
            if (reader.ScalarIsEmpty())
            {
                value = 0;
                return false;
            }
            return reader.TryReadDouble(out value);
        }

        internal bool TryGetInt64(out long value)
        {
            var reader = InitReader();
            if (reader.ScalarIsEmpty())
            {
                value = 0;
                return false;
            }
            return reader.TryReadInt64(out value);
        }
    }
}
