using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis
{
    internal readonly struct RawResult
    {
        internal static readonly RawResult NullMultiBulk = new RawResult((RawResult[])null);
        internal static readonly RawResult EmptyMultiBulk = new RawResult(new RawResult[0]);
        internal static readonly RawResult Nil = new RawResult();
        

        private readonly ReadOnlySequence<byte> _payload;
        private readonly RawResult[] _subArray;
        private readonly ResultType _type;

        const ResultType NullResultTypeBit = unchecked((ResultType)(1 << 31));
        public RawResult(ResultType resultType, ReadOnlySequence<byte> payload, bool isNull)
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
            if (isNull) resultType |= NullResultTypeBit;
            _type = resultType;
            _payload = payload;
            _subArray = default;
        }

        public RawResult(RawResult[] arr)
        {
            _type = ResultType.MultiBulk;
            if (arr == null) _type |= NullResultTypeBit;
            _payload = default;
            _subArray = arr;
        }

        public bool HasValue => Type != ResultType.None;

        public bool IsError => Type == ResultType.Error;

        public ResultType Type => _type & ~NullResultTypeBit;

        internal bool IsNull => (_type & NullResultTypeBit) != 0;

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
                    return $"{Type}: {_payload.Length} bytes";
                case ResultType.MultiBulk:
                    return $"{Type}: {_subArray.Length} items";
                default:
                    return $"(unknown: {Type})";
            }
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
                    if (AssertStarts(channelPrefix))
                    {

                        byte[] copy = _payload.Slice(channelPrefix.Length).ToArray();
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

        internal unsafe bool IsEqual(byte[] expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));

            var rangeToCheck = _payload;

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

        internal bool AssertStarts(byte[] expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (expected.Length > _payload.Length) return false;

            var rangeToCheck = _payload.Slice(0, expected.Length);
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

            if (_payload.IsEmpty) return Array.Empty<byte>();

            return _payload.ToArray();
        }

        internal bool GetBoolean()
        {
            if (_payload.Length != 1) throw new InvalidCastException();
            switch (_payload.First.Span[0])
            {
                case (byte)'1': return true;
                case (byte)'0': return false;
                default: throw new InvalidCastException();
            }
        }

        internal RawResult[] GetItems()
        {
            if (Type == ResultType.MultiBulk) return _subArray;
            throw new InvalidOperationException();
        }

        internal RedisKey[] GetItemsAsKeys()
        {
            RawResult[] items = GetItems();
            if (items == null)
            {
                return null;
            }
            else if (items.Length == 0)
            {
                return RedisKey.EmptyArray;
            }
            else
            {
                var arr = new RedisKey[items.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = items[i].AsRedisKey();
                }
                return arr;
            }
        }

        internal RedisValue[] GetItemsAsValues()
        {
            RawResult[] items = GetItems();
            if (items == null)
            {
                return null;
            }
            else if (items.Length == 0)
            {
                return RedisValue.EmptyArray;
            }
            else
            {
                var arr = new RedisValue[items.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = items[i].AsRedisValue();
                }
                return arr;
            }
        }

        private static readonly string[] NilStrings = new string[0];
        internal string[] GetItemsAsStrings()
        {
            RawResult[] items = GetItems();
            if (items == null)
            {
                return null;
            }
            else if (items.Length == 0)
            {
                return NilStrings;
            }
            else
            {
                var arr = new string[items.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = (string)(items[i].AsRedisValue());
                }
                return arr;
            }
        }

        internal GeoPosition? GetItemsAsGeoPosition()
        {
            RawResult[] items = GetItems();
            if (items == null || items.Length == 0)
            {
                return null;
            }

            var coords = items[0].GetItems();
            if (coords == null)
            {
                return null;
            }
            return new GeoPosition((double)coords[1].AsRedisValue(), (double)coords[0].AsRedisValue());
        }

        internal GeoPosition?[] GetItemsAsGeoPositionArray()
        {
            RawResult[] items = GetItems();
            if (items == null)
            {
                return null;
            }
            else if (items.Length == 0)
            {
                return new GeoPosition?[0];
            }
            else
            {
                var arr = new GeoPosition?[items.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    RawResult[] item = items[i].GetItems();
                    if (item == null)
                    {
                        arr[i] = null;
                    }
                    else
                    {
                        arr[i] = new GeoPosition((double)item[1].AsRedisValue(), (double)item[0].AsRedisValue());
                    }
                }
                return arr;
            }
        }

        internal RawResult[] GetItemsAsRawResults() => GetItems();

        internal unsafe string GetString()
        {
            if (IsNull) return null;
            if (_payload.IsEmpty) return "";

            if (_payload.IsSingleSegment)
            {
                var span = _payload.First.Span;
                fixed (byte* ptr = &span[0])
                {
                    return Encoding.UTF8.GetString(ptr, span.Length);
                }
            }
            var decoder = Encoding.UTF8.GetDecoder();
            int charCount = 0;
            foreach(var segment in _payload)
            {
                var span = segment.Span;
                if (span.IsEmpty) continue;

                fixed(byte* bPtr = &span[0])
                {
                    charCount += decoder.GetCharCount(bPtr, span.Length, false);
                }
            }
            
            decoder.Reset();

            string s = new string((char)0, charCount);
            fixed (char* sPtr = s)
            {
                char* cPtr = sPtr;
                foreach (var segment in _payload)
                {
                    var span = segment.Span;
                    if (span.IsEmpty) continue;

                    fixed (byte* bPtr = &span[0])
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
            if(IsNull || _payload.IsEmpty || _payload.Length > PhysicalConnection.MaxInt64TextLen)
            {
                value = 0;
                return false;
            }

            if (_payload.IsSingleSegment) return RedisValue.TryParseInt64(_payload.First.Span, out value);

            Span<byte> span = stackalloc byte[(int)_payload.Length]; // we already checked the length was <= MaxInt64TextLen
            _payload.CopyTo(span);
            return RedisValue.TryParseInt64(span, out value);
        }
    }
}

