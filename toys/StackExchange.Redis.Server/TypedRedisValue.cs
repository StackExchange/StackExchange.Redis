using System;
using System.Buffers;
using System.Collections.Generic;

namespace StackExchange.Redis
{
    /// <summary>
    /// A <see cref="RedisValue"/> with an eplicit encoding type, which could represent an array of items.
    /// </summary>
    public readonly struct TypedRedisValue
    {
        // note: if this ever becomes exposed on the public API, it should be made so that it clears;
        // can't trust external callers to clear the space, and using recycle without that is dangerous
        internal static TypedRedisValue Rent(int count, out Span<TypedRedisValue> span)
        {
            if (count == 0)
            {
                span = default;
                return EmptyArray;
            }
            var arr = ArrayPool<TypedRedisValue>.Shared.Rent(count);
            span = new Span<TypedRedisValue>(arr, 0, count);
            return new TypedRedisValue(arr, count);
        }

        /// <summary>
        /// An invalid empty value that has no type.
        /// </summary>
        public static TypedRedisValue Nil => default;
        /// <summary>
        /// Returns whether this value is an invalid empty value.
        /// </summary>
        public bool IsNil => Type == ResultType.None;

        /// <summary>
        /// Returns whether this value represents a null array.
        /// </summary>
        public bool IsNullArray => Type == ResultType.Array && _value.DirectObject == null;

        private readonly RedisValue _value;

        /// <summary>
        /// The type of value being represented.
        /// </summary>
        public ResultType Type { get; }

        /// <summary>
        /// Initialize a TypedRedisValue from a value and optionally a type.
        /// </summary>
        /// <param name="value">The value to initialize.</param>
        /// <param name="type">The type of <paramref name="value"/>.</param>
        private TypedRedisValue(RedisValue value, ResultType? type = null)
        {
            Type = type ?? (value.IsInteger ? ResultType.Integer : ResultType.BulkString);
            _value = value;
        }

        /// <summary>
        /// Initialize a TypedRedisValue that represents an error.
        /// </summary>
        /// <param name="value">The error message.</param>
        public static TypedRedisValue Error(string value)
            => new TypedRedisValue(value, ResultType.Error);

        /// <summary>
        /// Initialize a TypedRedisValue that represents a simple string.
        /// </summary>
        /// <param name="value">The string value.</param>
        public static TypedRedisValue SimpleString(string value)
            => new TypedRedisValue(value, ResultType.SimpleString);

        /// <summary>
        /// The simple string OK
        /// </summary>
        public static TypedRedisValue OK { get; } = SimpleString("OK");
        internal static TypedRedisValue Zero { get; } = Integer(0);
        internal static TypedRedisValue One { get; } = Integer(1);
        internal static TypedRedisValue NullArray { get; } = new TypedRedisValue((TypedRedisValue[])null, 0);
        internal static TypedRedisValue EmptyArray { get; } = new TypedRedisValue(Array.Empty<TypedRedisValue>(), 0);

        /// <summary>
        /// Gets the array elements as a span
        /// </summary>
        public ReadOnlySpan<TypedRedisValue> Span
        {
            get
            {
                if (Type != ResultType.Array) return default;
                var arr = (TypedRedisValue[])_value.DirectObject;
                if (arr == null) return default;
                var length = (int)_value.DirectOverlappedBits64;
                return new ReadOnlySpan<TypedRedisValue>(arr, 0, length);
            }
        }
        public ArraySegment<TypedRedisValue> Segment
        {
            get
            {
                if (Type != ResultType.Array) return default;
                var arr = (TypedRedisValue[])_value.DirectObject;
                if (arr == null) return default;
                var length = (int)_value.DirectOverlappedBits64;
                return new ArraySegment<TypedRedisValue>(arr, 0, length);
            }
        }

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> that represents an integer.
        /// </summary>
        /// <param name="value">The value to initialize from.</param>
        public static TypedRedisValue Integer(long value)
            => new TypedRedisValue(value, ResultType.Integer);

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> from a <see cref="ReadOnlySpan{TypedRedisValue}"/>.
        /// </summary>
        /// <param name="items">The items to intialize a value from.</param>
        public static TypedRedisValue MultiBulk(ReadOnlySpan<TypedRedisValue> items)
        {
            if (items.IsEmpty) return EmptyArray;
            var result = Rent(items.Length, out var span);
            items.CopyTo(span);
            return result;
        }

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> from a collection.
        /// </summary>
        /// <param name="items">The items to intialize a value from.</param>
        public static TypedRedisValue MultiBulk(ICollection<TypedRedisValue> items)
        {
            if (items == null) return NullArray;
            int count = items.Count;
            if (count == 0) return EmptyArray;
            var arr = ArrayPool<TypedRedisValue>.Shared.Rent(count);
            items.CopyTo(arr, 0);
            return new TypedRedisValue(arr, count);
        }

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> that represents a bulk string.
        /// </summary>
        /// <param name="value">The value to initialize from.</param>
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
            Type = ResultType.Array;
        }

        internal void Recycle(int limit = -1)
        {
            if (_value.DirectObject is TypedRedisValue[] arr)
            {
                if (limit < 0) limit = (int)_value.DirectOverlappedBits64;
                for (int i = 0; i < limit; i++)
                {
                    arr[i].Recycle();
                }
                ArrayPool<TypedRedisValue>.Shared.Return(arr, clearArray: false);
            }
        }

        /// <summary>
        /// Get the underlying <see cref="RedisValue"/> assuming that it is a valid type with a meaningful value.
        /// </summary>
        internal RedisValue AsRedisValue() => Type == ResultType.Array ? default :_value;

        /// <summary>
        /// Obtain the value as a string.
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
                case ResultType.Array:
                    return $"{Type}:[{Span.Length}]";
                default:
                    return Type.ToString();
            }
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        public override int GetHashCode() => throw new NotSupportedException();

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="obj">The object to compare to.</param>
        public override bool Equals(object obj) => throw new NotSupportedException();
    }
}
