using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using StackExchange.Redis.Transports;

namespace StackExchange.Redis
{
    // incorporates a single Result (deframed payload) and the corresponding underlying Buffer (underlying memory) associated with a single response
    internal readonly struct RawResultBuffer
    {
        public readonly ITransportState Transport;
        public readonly ReadOnlySequence<byte> Buffer;
        public readonly RawResult Result;
        public RawResultBuffer(ITransportState transport, in ReadOnlySequence<byte> buffer, in RawResult result)
        {
            Transport = transport;
            Buffer = buffer;
            Result = result;
        }
    }
    internal readonly struct RawResult
    {
        internal ref readonly RawResult this[int index] => ref GetRef<RawResult>(index);

        internal int ItemsCount => _type == (ResultType.MultiBulk | NonNullFlag) ? GetLength<RawResult>() : 0;
        internal ReadOnlySequence<byte> Payload => Type == ResultType.MultiBulk ? default : GetSequence<byte>();

        private ReadOnlySequence<T> GetSequence<T>()
        {
            if (_startObject is ReadOnlySequenceSegment<T> startSegment)
                return new ReadOnlySequence<T>(startSegment, _startIndex, (ReadOnlySequenceSegment<T>)_endObject, _endIndex);

            if (_startObject is null)
                return default;

            var length = _endIndex - _startIndex;
            if (_startObject is T[] arr)
                return new ReadOnlySequence<T>(arr, _startIndex, length);

            ReadOnlyMemory<T> mem;
            if (_startObject is MemoryManager<T> manager)
            {
                mem = manager.Memory;
            }
            else if (typeof(T) == typeof(char) && _startObject is string s)
            {
                mem = FromString<T>(s);
            }
            else
            {
                // _startObject could be null for a default sequence
                if (_startObject is not null) ThrowUnexpected(_startObject);
                mem = default;
            }
            if (_startIndex != 0 || length != mem.Length) mem = mem.Slice(_startIndex, length);
            return new ReadOnlySequence<T>(mem);
        }

        private ref readonly T GetRef<T>(int index)
        {
            ReadOnlySpan<T> span;
            int length;
            if (_startObject is ReadOnlySequenceSegment<T> segment)
            {
                var endSegment = (ReadOnlySequenceSegment<T>)_endObject;
                length = checked((int)((endSegment.RunningIndex + _endIndex) - (segment.RunningIndex + _startIndex)));
                if (index < 0 || index >= length) Throw();
                span = segment.Memory.Span.Slice(_startIndex);
                while (true)
                {
                    if (index < span.Length) return ref span[index];
                    index -= span.Length;
                    segment = segment.Next;
                    span = segment.Memory.Span;
                }
            }

            length = _endIndex - _startIndex;
            if (index < 0 || index >= length) Throw();
            if (_startObject is T[] arr)
                return ref arr[_startIndex + index];

            if (_startObject is MemoryManager<T> manager)
            {
                span = manager.Memory.Span;
            }
            else if (typeof(T) == typeof(char) && _startObject is string s)
            {
                span = FromString<T>(s).Span;
            }
            else
            {
                // _startObject could be null for a default sequence
                if (_startObject is not null) ThrowUnexpected(_startObject);
                span = default;
            }
            return ref span[_startIndex + index];

            static void Throw() => throw new IndexOutOfRangeException(nameof(index));
        }

        private int GetLength<T>()
        {
            if (_startObject == _endObject) return _endIndex - _startIndex;

            // multi-segment length calculation
            var startSegment = (ReadOnlySequenceSegment<T>)_startObject;
            var endSegment = (ReadOnlySequenceSegment<T>)_endObject;
            return checked((int)((endSegment.RunningIndex + _endIndex) - (startSegment.RunningIndex + _startIndex)));
        }

        static void ThrowUnexpected(object obj)
            => throw new InvalidOperationException($"Unexpected sequence start object: {obj.GetType().FullName}");
        static ReadOnlyMemory<T> FromString<T>(string s)
        {
            var sMem = s.AsMemory();
            return Unsafe.As<ReadOnlyMemory<char>, ReadOnlyMemory<T>>(ref sMem);
        }

        internal static readonly RawResult NullMultiBulk = new RawResult(ResultType.MultiBulk);
        internal static readonly RawResult EmptyMultiBulk = new RawResult(ResultType.MultiBulk | NonNullFlag);
        internal static readonly RawResult Nil = default;

        internal void ReleaseItemsRecursive()
        {
            if (_type == (ResultType.MultiBulk | NonNullFlag))
            {
                var seq = GetSequence<RawResult>();
                foreach (var segment in seq)
                {
                    var span = segment.Span;
                    foreach (var item in span)
                        item.ReleaseItemsRecursive();
                    segment.Release();
                }
            }
        }

        internal void PreserveItemsRecursive()
        {
            if (_type == (ResultType.MultiBulk | NonNullFlag))
            {
                var seq = GetSequence<RawResult>();
                foreach (var segment in seq)
                {
                    segment.Preserve();
                    var span = segment.Span;
                    foreach (var item in span)
                        item.PreserveItemsRecursive();
                }
            }
        }

        private readonly object _startObject, _endObject;
        private readonly int _startIndex, _endIndex;
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
            var position = payload.Start;
            _startObject = position.GetObject();
            _startIndex = position.GetInteger();
            position = payload.End;
            _endObject = position.GetObject();
            _endIndex = position.GetInteger();
        }

        private RawResult(ResultType type)
        {
            _type = type;
            _startObject = _endObject = null;
            _startIndex = _endIndex = 0;
        }

        public RawResult(ReadOnlySequence<RawResult> items)
        {
            _type = ResultType.MultiBulk | NonNullFlag;
            var position = items.Start;
            _startObject = position.GetObject();
            _startIndex = position.GetInteger();
            position = items.End;
            _endObject = position.GetObject();
            _endIndex = position.GetInteger();
        }

        public bool IsError => Type == ResultType.Error;

        public ResultType Type => _type & ~NonNullFlag;

        internal bool IsNull => (_type & NonNullFlag) == 0;
        public bool HasValue => Type != ResultType.None;

        public override string ToString()
        {
            if (IsNull) return "(null)";

            return Type switch
            {
                ResultType.SimpleString or ResultType.Integer or ResultType.Error => $"{Type}: {GetString()}",
                ResultType.BulkString => $"{Type}: {Payload.Length} bytes",
                ResultType.MultiBulk => $"{Type}: {ItemsCount} items",
                _ => $"(unknown: {Type})",
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
        internal RedisChannel AsRedisChannel(byte[]? channelPrefix, RedisChannel.PatternMode mode)
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
                    return default;
                default:
                    throw new InvalidCastException("Cannot convert to RedisChannel: " + Type);
            }
        }

        internal RedisKey AsRedisKey() => Type switch
        {
            ResultType.SimpleString or ResultType.BulkString => (RedisKey)GetBlob(),
            _ => throw new InvalidCastException("Cannot convert to RedisKey: " + Type),
        };

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

        internal Lease<byte>? AsLease()
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

            if (Payload.IsEmpty) return Array.Empty<byte>();

            return Payload.ToArray();
        }

        internal bool GetBoolean()
        {
            if (Payload.Length != 1) throw new InvalidCastException();
            return Payload.First.Span[0] switch
            {
                (byte)'1' => true,
                (byte)'0' => false,
                _ => throw new InvalidCastException(),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySequence<RawResult> GetItems() => _type == (ResultType.MultiBulk | NonNullFlag) ? GetSequence<RawResult>() : default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double?[]? GetItemsAsDoubles() => this.ToArray<double?>((in RawResult x) => x.TryGetDouble(out double val) ? val : null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisKey[]? GetItemsAsKeys() => this.ToArray<RedisKey>((in RawResult x) => x.AsRedisKey());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RedisValue[]? GetItemsAsValues() => this.ToArray<RedisValue>((in RawResult x) => x.AsRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal string?[]? GetItemsAsStrings() => this.ToArray<string?>((in RawResult x) => (string?)x.AsRedisValue());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool[]? GetItemsAsBooleans() => this.ToArray<bool>((in RawResult x) => (bool)x.AsRedisValue());

        internal GeoPosition? GetItemsAsGeoPosition()
        {
            var items = GetItems();
            if (IsNull || items.Length == 0)
            {
                return null;
            }

            ref readonly RawResult root = ref items.First.Span[0];
            if (root.IsNull)
            {
                return null;
            }
            return AsGeoPosition(root.GetItems());
        }

        private static GeoPosition AsGeoPosition(in ReadOnlySequence<RawResult> coords)
        {
            double longitude, latitude;
            if (coords.IsSingleSegment)
            {
                var span = coords.First.Span;
                longitude = (double)span[0].AsRedisValue();
                latitude = (double)span[1].AsRedisValue();
            }
            else
            {
                longitude = (double)coords.GetRef(0).AsRedisValue();
                latitude = (double)coords.GetRef(1).AsRedisValue();
            }

            return new GeoPosition(longitude, latitude);
        }

        internal GeoPosition?[]? GetItemsAsGeoPositionArray()
            => this.ToArray<GeoPosition?>((in RawResult item) => item.IsNull ? default : AsGeoPosition(item.GetItems()));

        internal unsafe string? GetString()
        {
            if (IsNull) return null;
            if (Payload.IsEmpty) return "";

            if (Payload.IsSingleSegment)
            {
                return Format.GetString(Payload.First.Span);
            }
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
            if (IsNull || Payload.IsEmpty || Payload.Length > MessageFormatter.MaxInt64TextLen)
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
