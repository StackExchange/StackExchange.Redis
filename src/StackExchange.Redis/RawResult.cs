using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis
{
    internal readonly struct RawResult
    {
        internal ref RawResult this[int index] => ref GetItems()[index];

        internal int ItemsCount => (int)_items.Length;
        internal ReadOnlySequence<byte> Payload { get; }

        internal static readonly RawResult NullMultiBulk = new RawResult(default(Sequence<RawResult>), isNull: true);
        internal static readonly RawResult EmptyMultiBulk = new RawResult(default(Sequence<RawResult>), isNull: false);
        internal static readonly RawResult Nil = default;
        // note: can't use Memory<RawResult> here - struct recursion breaks runtimr
        private readonly Sequence _items;
        private readonly ResultType _type;

        private const ResultType NonNullFlag = (ResultType)128;

        public RawResult(ResultType resultType, in ReadOnlySequence<byte> payload, bool isNull)
        {
            switch (resultType)
            {
                case ResultType.SimpleString:
                case ResultType.Error:
                case ResultType.Integer:
                case ResultType.BulkString:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resultType));
            }
            if (!isNull) resultType |= NonNullFlag;
            _type = resultType;
            Payload = payload;
            _items = default;
        }

        public RawResult(Sequence<RawResult> items, bool isNull)
        {
            _type = isNull ? ResultType.MultiBulk : (ResultType.MultiBulk | NonNullFlag);
            Payload = default;
            _items = items.Untyped();
        }

        public bool IsError => Type == ResultType.Error;

        public ResultType Type => _type & ~NonNullFlag;

        internal bool IsNull => (_type & NonNullFlag) == 0;
        public bool HasValue => Type != ResultType.None;

        public override string ToString()
        {
            if (IsNull) return "(null)";

            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.Integer:
                case ResultType.Error:
                    return $"{Type}: {GetString()}";
                case ResultType.BulkString:
                    return $"{Type}: {Payload.Length} bytes";
                case ResultType.MultiBulk:
                    return $"{Type}: {ItemsCount} items";
                default:
                    return $"(unknown: {Type})";
            }
        }

        public Tokenizer GetInlineTokenizer() => new Tokenizer(Payload);

        internal ref struct Tokenizer
        {
            // tokenizes things according to the inline protocol
            // specifically; the line: abc    "def ghi" jkl
            // is 3 tokens: "abc", "def ghi" and "jkl"
            public Tokenizer GetEnumerator() => this;
            private BufferReader _value;

            public Tokenizer(in ReadOnlySequence<byte> value)
            {
                _value = new BufferReader(value);
                Current = default;
            }

            public bool MoveNext()
            {
                Current = default;
                // take any white-space
                while (_value.PeekByte() == (byte)' ') { _value.Consume(1); }

                byte terminator = (byte)' ';
                var first = _value.PeekByte();
                if (first < 0) return false; // EOF

                switch (first)
                {
                    case (byte)'"':
                    case (byte)'\'':
                        // start of string
                        terminator = (byte)first;
                        _value.Consume(1);
                        break;
                }

                int end = BufferReader.FindNext(_value, terminator);
                if (end < 0)
                {
                    Current = _value.ConsumeToEnd();
                }
                else
                {
                    Current = _value.ConsumeAsBuffer(end);
                    _value.Consume(1); // drop the terminator itself;
                }
                return true;
            }
            public ReadOnlySequence<byte> Current { get; private set; }
        }
        internal RedisChannel AsRedisChannel(byte[] channelPrefix, RedisChannel.PatternMode mode)
        {
            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    if (channelPrefix == null)
                    {
                        return new RedisChannel(GetBlob(), mode);
                    }
                    if (StartsWith(channelPrefix))
                    {
                        byte[] copy = Payload.Slice(channelPrefix.Length).ToArray();
                        return new RedisChannel(copy, mode);
                    }
                    return default(RedisChannel);
                default:
                    throw new InvalidCastException("Cannot convert to RedisChannel: " + Type);
            }
        }

        internal RedisKey AsRedisKey()
        {
            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    return (RedisKey)GetBlob();
                default:
                    throw new InvalidCastException("Cannot convert to RedisKey: " + Type);
            }
        }

        internal RedisValue AsRedisValue()
        {
            if (IsNull) return RedisValue.Null;
            switch (Type)
            {
                case ResultType.Integer:
                    long i64;
                    if (TryGetInt64(out i64)) return (RedisValue)i64;
                    break;
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    return (RedisValue)GetBlob();
            }
            throw new InvalidCastException("Cannot convert to RedisValue: " + Type);
        }

        internal Lease<byte> AsLease()
        {
            if (IsNull) return null;
            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    var payload = Payload;
                    var lease = Lease<byte>.Create(checked((int)payload.Length), false);
                    payload.CopyTo(lease.Span);
                    return lease;
            }
            throw new InvalidCastException("Cannot convert to Lease: " + Type);
        }

        internal bool IsEqual(in CommandBytes expected)
        {
            if (expected.Length != Payload.Length) return false;
            return new CommandBytes(Payload).Equals(expected);
        }

        internal unsafe bool IsEqual(byte[] expected)
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
            foreach(var segment in rangeToCheck)
            {
                var from = segment.Span;
                var to = new Span<byte>(expected, offset, from.Length);
                if (!from.SequenceEqual(to)) return false;

                offset += from.Length;
            }
            return true;
        }

        internal byte[] GetBlob()
        {
            if (IsNull) return null;

            if (Payload.IsEmpty) return Array.Empty<byte>();

            return Payload.ToArray();
        }

        internal bool GetBoolean()
        {
            if (Payload.Length != 1) throw new InvalidCastException();
            switch (Payload.First.Span[0])
            {
                case (byte)'1': return true;
                case (byte)'0': return false;
                default: throw new InvalidCastException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Sequence<RawResult> GetItems() => _items.Cast<RawResult>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisKey[] GetItemsAsKeys() => this.ToArray<RedisKey>((in RawResult x) => x.AsRedisKey());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisValue[] GetItemsAsValues() => this.ToArray<RedisValue>((in RawResult x) => x.AsRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string[] GetItemsAsStrings() => this.ToArray<string>((in RawResult x) => (string)x.AsRedisValue());

        internal GeoPosition? GetItemsAsGeoPosition()
        {
            var items = GetItems();
            if (IsNull || items.Length == 0)
            {
                return null;
            }

            ref RawResult root = ref items[0];
            if (root.IsNull)
            {
                return null;
            }
            return AsGeoPosition(root.GetItems());
        }

        private static GeoPosition AsGeoPosition(in Sequence<RawResult> coords)
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

        internal GeoPosition?[] GetItemsAsGeoPositionArray()
            => this.ToArray<GeoPosition?>((in RawResult item) => item.IsNull ? (GeoPosition?)null : AsGeoPosition(item.GetItems()));

        internal unsafe string GetString()
        {
            if (IsNull) return null;
            if (Payload.IsEmpty) return "";

            if (Payload.IsSingleSegment)
            {
                return Format.GetString(Payload.First.Span);
            }
            var decoder = Encoding.UTF8.GetDecoder();
            int charCount = 0;
            foreach(var segment in Payload)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;

                fixed(byte* bPtr = span)
                {
                    charCount += decoder.GetCharCount(bPtr, span.Length, false);
                }
            }

            decoder.Reset();

            string s = new string((char)0, charCount);
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
                        cPtr += written;
                        charCount -= written;
                    }
                }
            }
            return s;
        }

        internal bool TryGetDouble(out double val)
        {
            if (IsNull)
            {
                val = 0;
                return false;
            }
            if (TryGetInt64(out long i64))
            {
                val = i64;
                return true;
            }
            return Format.TryParseDouble(GetString(), out val);
        }

        internal bool TryGetInt64(out long value)
        {
            if (IsNull || Payload.IsEmpty || Payload.Length > PhysicalConnection.MaxInt64TextLen)
            {
                value = 0;
                return false;
            }

            if (Payload.IsSingleSegment) return Format.TryParseInt64(Payload.First.Span, out value);

            Span<byte> span = stackalloc byte[(int)Payload.Length]; // we already checked the length was <= MaxInt64TextLen
            Payload.CopyTo(span);
            return Format.TryParseInt64(span, out value);
        }
    }
}

