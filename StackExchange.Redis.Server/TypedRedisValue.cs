using System;
using System.Buffers;

namespace StackExchange.Redis
{
    /// <summary>
    /// A RedisValue with an eplicit encoding type, which could represent an array of items
    /// </summary>
    public readonly struct TypedRedisValue
    {
        internal static TypedRedisValue Rent(int count, out Span<TypedRedisValue> span)
        {
            var arr = ArrayPool<TypedRedisValue>.Shared.Rent(count);
            span = new Span<TypedRedisValue>(arr, 0, count);
            return new TypedRedisValue(arr, count);
        }

        /// <summary>
        /// An invalid empty value that has no type
        /// </summary>
        public static TypedRedisValue Nil => default;
        /// <summary>
        /// Returns whether this value is an invalid empty value
        /// </summary>
        public bool IsNil => Type == ResultType.None;

        /// <summary>
        /// Returns whether this value represents a null array
        /// </summary>
        public bool IsNullArray => Type == ResultType.MultiBulk && _value.DirectObject == null;

        private readonly RedisValue _value;

        /// <summary>
        /// The type of value being represented
        /// </summary>
        public ResultType Type { get; }
        
        /// <summary>
        /// Initialize a TypedRedisValue from a value and optionally a type
        /// </summary>
        private TypedRedisValue(RedisValue value, ResultType? type = null)
        {
            Type = type ?? (value.IsInteger ? ResultType.Integer : ResultType.BulkString);
            _value = value;
        }

        /// <summary>
        /// Initialize a TypedRedisValue that represents an error
        /// </summary>
        public static TypedRedisValue Error(string value)
            => new TypedRedisValue(value, ResultType.Error);

        /// <summary>
        /// Initialize a TypedRedisValue that represents a simple string
        /// </summary>
        public static TypedRedisValue SimpleString(string value)
            => new TypedRedisValue(value, ResultType.SimpleString);

        /// <summary>
        /// The simple string OK
        /// </summary>
        public static TypedRedisValue OK { get; } = SimpleString("OK");
        internal static TypedRedisValue Zero { get; } = Integer(0);
        internal static TypedRedisValue One { get; } = Integer(1);
        internal static TypedRedisValue NullArray { get; } = MultiBulk(null);
        internal static TypedRedisValue EmptyArray { get; } = MultiBulk(Array.Empty<TypedRedisValue>());

        /// <summary>
        /// Gets the array elements as a span
        /// </summary>
        public ReadOnlySpan<TypedRedisValue> Span
        {
            get
            {
                if (Type != ResultType.MultiBulk) return default;
                var arr = (TypedRedisValue[])_value.DirectObject;
                if (arr == null) return default;
                var length = (int)_value.DirectInt64;
                return new ReadOnlySpan<TypedRedisValue>(arr, 0, length);
            }
        }
        public ArraySegment<TypedRedisValue> Segment
        {
            get
            {
                if (Type != ResultType.MultiBulk) return default;
                var arr = (TypedRedisValue[])_value.DirectObject;
                if (arr == null) return default;
                var length = (int)_value.DirectInt64;
                return new ArraySegment<TypedRedisValue>(arr, 0, length);
            }
        }

        /// <summary>
        /// Initialize a TypedRedisValue that represents an integer
        /// </summary>
        public static TypedRedisValue Integer(long value)
            => new TypedRedisValue(value, ResultType.Integer);

        /// <summary>
        /// Initialize a TypedRedisValue from an array
        /// </summary>
        public static TypedRedisValue MultiBulk(TypedRedisValue[] items)
            => new TypedRedisValue(items, items == null ? 0 : items.Length);

        /// <summary>
        /// Initialize a TypedRedisValue from an oversized array
        /// </summary>
        public static TypedRedisValue MultiBulk(TypedRedisValue[] oversizedItems, int count)
            => new TypedRedisValue(oversizedItems, count);

        /// <summary>
        /// Initialize a TypedRedisValue that represents a bulk string
        /// </summary>
        public static TypedRedisValue BulkString(RedisValue value)
            => new TypedRedisValue(value, ResultType.BulkString);

        private TypedRedisValue(TypedRedisValue[] oversizedItems, int count)
        {
            if (oversizedItems == null)
            {
                if (count != 0) throw new ArgumentOutOfRangeException(nameof(count));
            }
            else
            {
                if (count < 0 || count > oversizedItems.Length) throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0) oversizedItems = Array.Empty<TypedRedisValue>();
            }
            _value = new RedisValue(oversizedItems, count);
            Type = ResultType.MultiBulk;
        }
        internal void Recycle(int limit = -1)
        {
            var arr = _value.DirectObject as TypedRedisValue[];
            if (arr != null)
            {
                if (limit < 0) limit = (int)_value.DirectInt64;
                for (int i = 0; i < limit; i++)
                {
                    arr[i].Recycle();
                }
                ArrayPool<TypedRedisValue>.Shared.Return(arr, clearArray: false);
            }
        }

        /// <summary>
        /// Get the underlying value assuming that it is a valid type with a meaningful value
        /// </summary>
        internal RedisValue AsRedisValue() => Type == ResultType.MultiBulk ? default :_value;

        /// <summary>
        /// Obtain the value as a string
        /// </summary>
        public override string ToString()
        {
            switch (Type)
            {
                case ResultType.BulkString:
                case ResultType.SimpleString:
                case ResultType.Integer:
                case ResultType.Error:
                    return $"{Type}:{_value}";
                case ResultType.MultiBulk:
                    return $"{Type}:[{Span.Length}]";
                default:
                    return Type.ToString();
            }
        }

        /// <summary>
        /// Not supported
        /// </summary>
        public override int GetHashCode() => throw new NotSupportedException();
        /// <summary>
        /// Not supported
        /// </summary>
        public override bool Equals(object obj) => throw new NotSupportedException();

    }
}
