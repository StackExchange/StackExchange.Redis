using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace StackExchange.Redis
{
    internal readonly struct RawResult
    {
        internal ref RawResult this[int index] => ref GetItems()[index];

        internal int ItemsCount => (int)_items.Length;

        private readonly ReadOnlySequence<byte> _payload;
        internal ReadOnlySequence<byte> Payload => _payload;

        internal static readonly RawResult Nil = default;
        // Note: can't use Memory<RawResult> here - struct recursion breaks runtime
        private readonly Sequence _items;
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

        public RawResult(ResultType resultType, in ReadOnlySequence<byte> payload, ResultFlags flags)
        {
            switch (resultType)
            {
                case ResultType.SimpleString:
                case ResultType.Error:
                case ResultType.Integer:
                case ResultType.BulkString:
                case ResultType.Double:
                case ResultType.Boolean:
                case ResultType.BlobError:
                case ResultType.VerbatimString:
                case ResultType.BigInteger:
                    break;
                case ResultType.Null:
                    flags &= ~ResultFlags.NonNull;
                    break;
                default:
                    ThrowInvalidType(resultType);
                    break;
            }
            _resultType = resultType;
            _flags = flags | ResultFlags.HasValue;
            _payload = payload;
            _items = default;
        }

        public RawResult(ResultType resultType, Sequence<RawResult> items, ResultFlags flags)
        {
            switch (resultType)
            {
                case ResultType.Array:
                case ResultType.Map:
                case ResultType.Set:
                case ResultType.Attribute:
                case ResultType.Push:
                    break;
                case ResultType.Null:
                    flags &= ~ResultFlags.NonNull;
                    break;
                default:
                    ThrowInvalidType(resultType);
                    break;
            }
            _resultType = resultType;
            _flags = flags | ResultFlags.HasValue;
            _payload = default;
            _items = items.Untyped();
        }

        private static void ThrowInvalidType(ResultType resultType)
             => throw new ArgumentOutOfRangeException(nameof(resultType), $"Invalid result-type: {resultType}");

        public bool IsError => _resultType.IsError();

        public ResultType Resp3Type => _resultType;

        // if null, assume string
        public ResultType Resp2TypeBulkString => _resultType == ResultType.Null ? ResultType.BulkString : _resultType.ToResp2();
        // if null, assume array
        public ResultType Resp2TypeArray => _resultType == ResultType.Null ? ResultType.Array : _resultType.ToResp2();

        internal bool IsNull => (_flags &  ResultFlags.NonNull) == 0;

        public bool HasValue => (_flags & ResultFlags.HasValue) != 0;

        public bool IsResp3 => (_flags & ResultFlags.Resp3) != 0;

        public override string ToString()
        {
            if (IsNull) return "(null)";

            return _resultType.ToResp2() switch
            {
                ResultType.SimpleString or ResultType.Integer or ResultType.Error => $"{Resp3Type}: {GetString()}",
                ResultType.BulkString => $"{Resp3Type}: {Payload.Length} bytes",
                ResultType.Array => $"{Resp3Type}: {ItemsCount} items",
                _ => $"(unknown: {Resp3Type})",
            };
        }

        public Tokenizer GetInlineTokenizer() => new Tokenizer(Payload);

        internal ref struct Tokenizer
        {
            // tokenizes things according to the inline protocol
            // specifically; the line: abc    "def ghi" jkl
            // is 3 tokens: "abc", "def ghi" and "jkl"
            public Tokenizer GetEnumerator() => this;
            private BufferReader _value;

            public Tokenizer(scoped in ReadOnlySequence<byte> value)
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
        internal RedisChannel AsRedisChannel(byte[]? channelPrefix, RedisChannel.PatternMode mode)
        {
            switch (Resp2TypeBulkString)
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
                    return default;
                default:
                    throw new InvalidCastException("Cannot convert to RedisChannel: " + Resp3Type);
            }
        }

        internal RedisKey AsRedisKey()
        {
            return Resp2TypeBulkString switch
            {
                ResultType.SimpleString or ResultType.BulkString => (RedisKey)GetBlob(),
                _ => throw new InvalidCastException("Cannot convert to RedisKey: " + Resp3Type),
            };
        }

        internal RedisValue AsRedisValue()
        {
            if (IsNull) return RedisValue.Null;
            if (Resp3Type == ResultType.Boolean && Payload.Length == 1)
            {
                switch (Payload.First.Span[0])
                {
                    case (byte)'t': return (RedisValue)true;
                    case (byte)'f': return (RedisValue)false;
                }
            }
            switch (Resp2TypeBulkString)
            {
                case ResultType.Integer:
                    long i64;
                    if (TryGetInt64(out i64)) return (RedisValue)i64;
                    break;
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    return (RedisValue)GetBlob();
            }
            throw new InvalidCastException("Cannot convert to RedisValue: " + Resp3Type);
        }

        internal Lease<byte>? AsLease()
        {
            if (IsNull) return null;
            switch (Resp2TypeBulkString)
            {
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    var payload = Payload;
                    var lease = Lease<byte>.Create(checked((int)payload.Length), false);
                    payload.CopyTo(lease.Span);
                    return lease;
            }
            throw new InvalidCastException("Cannot convert to Lease: " + Resp3Type);
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
            foreach(var segment in rangeToCheck)
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

            if (Payload.IsEmpty) return Array.Empty<byte>();

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
        internal Sequence<RawResult> GetItems() => _items.Cast<RawResult>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double?[]? GetItemsAsDoubles() => this.ToArray<double?>((in RawResult x) => x.TryGetDouble(out double val) ? val : null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisKey[]? GetItemsAsKeys() => this.ToArray<RedisKey>((in RawResult x) => x.AsRedisKey());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisValue[]? GetItemsAsValues() => this.ToArray<RedisValue>((in RawResult x) => x.AsRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string?[]? GetItemsAsStrings() => this.ToArray<string?>((in RawResult x) => (string?)x.AsRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string[]? GetItemsAsStringsNotNullable() => this.ToArray<string>((in RawResult x) => (string)x.AsRedisValue()!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool[]? GetItemsAsBooleans() => this.ToArray<bool>((in RawResult x) => (bool)x.AsRedisValue());

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

        internal SortedSetEntry[]? GetItemsAsSortedSetEntryArray() => this.ToArray((in RawResult item) => AsSortedSetEntry(item.GetItems()));

        private static SortedSetEntry AsSortedSetEntry(in Sequence<RawResult> elements)
        {
            if (elements.IsSingleSegment)
            {
                var span = elements.FirstSpan;
                return new SortedSetEntry(span[0].AsRedisValue(), span[1].TryGetDouble(out double val) ? val : double.NaN);
            }
            else
            {
                return new SortedSetEntry(elements[0].AsRedisValue(), elements[1].TryGetDouble(out double val) ? val : double.NaN);
            }
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
                //  the first three bytes provide information about the format of the following string, which
                //  can be txt for plain text, or mkd for markdown. The fourth byte is always `:`
                //  Then the real string follows.
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

        internal bool TryGetDouble(out double val)
        {
            if (IsNull || Payload.IsEmpty)
            {
                val = 0;
                return false;
            }
            if (TryGetInt64(out long i64))
            {
                val = i64;
                return true;
            }

            if (Payload.IsSingleSegment) return Format.TryParseDouble(Payload.First.Span, out val);
            if (Payload.Length < 64)
            {
                Span<byte> span = stackalloc byte[(int)Payload.Length];
                Payload.CopyTo(span);
                return Format.TryParseDouble(span, out val);
            }
            return Format.TryParseDouble(GetString(), out val);
        }

        internal bool TryGetInt64(out long value)
        {
            if (IsNull || Payload.IsEmpty || Payload.Length > Format.MaxInt64TextLen)
            {
                value = 0;
                return false;
            }

            if (Payload.IsSingleSegment) return Format.TryParseInt64(Payload.First.Span, out value);

            Span<byte> span = stackalloc byte[(int)Payload.Length]; // we already checked the length was <= MaxInt64TextLen
            Payload.CopyTo(span);
            return Format.TryParseInt64(span, out value);
        }

        internal bool Is(char value)
        {
            var span = Payload.First.Span;
            return span.Length == 1 && (char)span[0] == value && Payload.IsSingleSegment;
        }
    }
}
