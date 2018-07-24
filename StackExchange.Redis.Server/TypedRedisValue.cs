using System;
using System.Buffers;

namespace StackExchange.Redis
{
    /// <summary>
    /// A RedisValue with an eplicit encoding type, which could represent an array of items
    /// </summary>
    public readonly struct TypedRedisValue
    {
        internal static TypedRedisValue Rent(int count)
            => new TypedRedisValue(ArrayPool<TypedRedisValue>.Shared.Rent(count), count);

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
        public bool IsNullArray => Type == ResultType.MultiBulk && _oversizedItems == null;

        /// <summary>
        /// The type of value being represented
        /// </summary>
        public ResultType Type { get; }

        private readonly RedisValue _value;
        private readonly TypedRedisValue[] _oversizedItems;

        /// <summary>
        /// Gets items from an array by index
        /// </summary>
        public TypedRedisValue this[int index]
        {
            get => _oversizedItems[index];
            internal set => _oversizedItems[index] = value;
        }
        /// <summary>
        /// Gets the length of the value as an array
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Initialize a TypedRedisValue from a value and optionally a type
        /// </summary>
        private TypedRedisValue(RedisValue value, ResultType? type = null)
        {
            Type = type ?? (value.IsInteger ? ResultType.Integer : ResultType.BulkString);
            _value = value;
            Length = default;
            _oversizedItems = default;
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
        public ReadOnlySpan<TypedRedisValue> Span => new ReadOnlySpan<TypedRedisValue>(_oversizedItems, 0, Length);
        internal Span<TypedRedisValue> MutableSpan => new Span<TypedRedisValue>(_oversizedItems, 0, Length);

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
            _value = default;
            Type = ResultType.MultiBulk;
            Length = count;
            _oversizedItems = oversizedItems;
        }
        internal void Recycle(int limit = -1)
        {
            var arr = _oversizedItems;
            if (arr != null)
            {
                if (limit < 0) limit = Length;
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
        internal RedisValue AsRedisValue() => _value;

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
                    return $"{Type}:[{Length}]";
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
