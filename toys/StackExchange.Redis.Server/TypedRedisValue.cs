using System;
using System.Buffers;
using System.Collections.Generic;
using RESPite.Messages;

namespace StackExchange.Redis
{
    /// <summary>
    /// A <see cref="RedisValue"/> with an eplicit encoding type, which could represent an array of items.
    /// </summary>
    public readonly struct TypedRedisValue
    {
        /// <summary>
        /// Rents an array from the pool and returns a <see cref="TypedRedisValue"/> that wraps it.
        /// The returned span is cleared to ensure safe usage.
        /// </summary>
        /// <param name="count">The number of elements to rent.</param>
        /// <param name="span">The span that can be used to populate the array.</param>
        /// <param name="type">The RESP type of the array.</param>
        /// <returns>A <see cref="TypedRedisValue"/> that wraps the rented array.</returns>
        public static TypedRedisValue Rent(int count, out Span<TypedRedisValue> span, RespPrefix type)
        {
            if (count == 0)
            {
                span = default;
                return EmptyArray(type);
            }

            var arr = ArrayPool<TypedRedisValue>.Shared.Rent(count);
            span = new Span<TypedRedisValue>(arr, 0, count);
            span.Clear(); // Clear the span to ensure safe usage by external callers
            return new TypedRedisValue(arr, count, type);
        }

        /// <summary>
        /// An invalid empty value that has no type.
        /// </summary>
        public static TypedRedisValue Nil => default;

        /// <summary>
        /// Returns whether this value is an invalid empty value.
        /// </summary>
        public bool IsNil => Type == RespPrefix.None;

        /// <summary>
        /// Returns whether this value represents a null array.
        /// </summary>
        public bool IsNullArray => IsAggregate && _value.IsNull;

        private readonly RedisValue _value;

        /// <summary>
        /// The type of value being represented.
        /// </summary>
        public RespPrefix Type { get; }

        /// <summary>
        /// Initialize a TypedRedisValue from a value and optionally a type.
        /// </summary>
        /// <param name="value">The value to initialize.</param>
        /// <param name="type">The type of <paramref name="value"/>.</param>
        private TypedRedisValue(RedisValue value, RespPrefix? type = null)
        {
            Type = type ?? (value.IsInteger ? RespPrefix.Integer : RespPrefix.BulkString);
            _value = value;
        }

        /// <summary>
        /// Initialize a TypedRedisValue that represents an error.
        /// </summary>
        /// <param name="value">The error message.</param>
        public static TypedRedisValue Error(string value)
            => new TypedRedisValue(value, RespPrefix.SimpleError);

        /// <summary>
        /// Initialize a TypedRedisValue that represents a simple string.
        /// </summary>
        /// <param name="value">The string value.</param>
        public static TypedRedisValue SimpleString(string value)
            => new TypedRedisValue(value, RespPrefix.SimpleString);

        /// <summary>
        /// The simple string OK.
        /// </summary>
        public static TypedRedisValue OK { get; } = SimpleString("OK");

        internal static TypedRedisValue Zero { get; } = Integer(0);
        internal static TypedRedisValue One { get; } = Integer(1);
        internal static TypedRedisValue NullArray(RespPrefix type) => new TypedRedisValue((TypedRedisValue[])null, 0, type);
        internal static TypedRedisValue EmptyArray(RespPrefix type) => new TypedRedisValue([], 0, type);

        /// <summary>
        /// Gets the array elements as a span.
        /// </summary>
        public ReadOnlySpan<TypedRedisValue> Span
        {
            get
            {
                if (_value.TryGetForeign<TypedRedisValue[]>(out var arr, out int index, out var length))
                {
                    return arr.AsSpan(index, length);
                }

                return default;
            }
        }

        public bool IsAggregate => Type is RespPrefix.Array or RespPrefix.Set or RespPrefix.Map or RespPrefix.Push or RespPrefix.Attribute;

        public bool IsNullValueOrArray => IsAggregate ? IsNullArray : _value.IsNull;
        public bool IsError => Type is RespPrefix.SimpleError or RespPrefix.BulkError;

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> that represents an integer.
        /// </summary>
        /// <param name="value">The value to initialize from.</param>
        public static TypedRedisValue Integer(long value)
            => new TypedRedisValue(value, RespPrefix.Integer);

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> from a <see cref="ReadOnlySpan{TypedRedisValue}"/>.
        /// </summary>
        /// <param name="items">The items to intialize a value from.</param>
        public static TypedRedisValue MultiBulk(ReadOnlySpan<TypedRedisValue> items, RespPrefix type)
        {
            if (items.IsEmpty) return EmptyArray(type);
            var result = Rent(items.Length, out var span, type);
            items.CopyTo(span);
            return result;
        }

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> from a collection.
        /// </summary>
        /// <param name="items">The items to intialize a value from.</param>
        public static TypedRedisValue MultiBulk(ICollection<TypedRedisValue> items, RespPrefix type)
        {
            if (items == null) return NullArray(type);
            int count = items.Count;
            if (count == 0) return EmptyArray(type);
            var result = Rent(count, out var span, type);
            int i = 0;
            foreach (var item in items)
            {
                span[i++] = item;
            }

            return result;
        }

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> that represents a bulk string.
        /// </summary>
        /// <param name="value">The value to initialize from.</param>
        public static TypedRedisValue BulkString(RedisValue value)
            => new TypedRedisValue(value, RespPrefix.BulkString);

        /// <summary>
        /// Initialize a <see cref="TypedRedisValue"/> that represents a bulk string.
        /// </summary>
        /// <param name="value">The value to initialize from.</param>
        public static TypedRedisValue BulkString(in RedisChannel value)
            => new TypedRedisValue((byte[])value, RespPrefix.BulkString);

        private TypedRedisValue(TypedRedisValue[] oversizedItems, int count, RespPrefix type)
        {
            if (oversizedItems == null)
            {
                if (count != 0) throw new ArgumentOutOfRangeException(nameof(count));
                oversizedItems = [];
            }
            else
            {
                if (count < 0 || count > oversizedItems.Length) throw new ArgumentOutOfRangeException(nameof(count));
                if (count == 0) oversizedItems = [];
            }

            _value = RedisValue.CreateForeign(oversizedItems, 0, count);
            Type = type;
        }

        internal void Recycle(int limit = -1)
        {
            if (_value.TryGetForeign<TypedRedisValue[]>(out var arr, out var index, out var length))
            {
                if (limit < 0) limit = length;
                var span = arr.AsSpan(index, limit);
                foreach (ref readonly TypedRedisValue el in span)
                {
                    el.Recycle();
                }
                span.Clear();
                ArrayPool<TypedRedisValue>.Shared.Return(arr, clearArray: false); // we did it ourselves
            }
        }

        /// <summary>
        /// Get the underlying <see cref="RedisValue"/> assuming that it is a valid type with a meaningful value.
        /// </summary>
        public RedisValue AsRedisValue() => IsAggregate ? default : _value;

        /// <summary>
        /// Obtain the value as a string.
        /// </summary>
        public override string ToString()
        {
            if (IsAggregate) return $"{Type}:[{Span.Length}]";

            switch (Type)
            {
                case RespPrefix.BulkString:
                case RespPrefix.SimpleString:
                case RespPrefix.Integer:
                case RespPrefix.SimpleError:
                    return $"{Type}:{_value}";
                default:
                    return IsAggregate ? $"{Type}:[{Span.Length}]" : Type.ToString();
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
