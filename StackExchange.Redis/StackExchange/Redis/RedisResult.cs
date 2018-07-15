﻿using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a general-purpose result from redis, that may be cast into various anticipated types
    /// </summary>
    public abstract class RedisResult
    {
        /// <summary>
        /// Create a new RedisResult representing a single value.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to create a result from.</param>
        /// <returns> new <see cref="RedisResult"/>.</returns>
        public static RedisResult Create(RedisValue value) => new SingleRedisResult(value, null);

        /// <summary>
        /// Create a new RedisResult representing an array of values.
        /// </summary>
        /// <param name="values">The <see cref="RedisValue"/>s to create a result from.</param>
        /// <returns> new <see cref="RedisResult"/>.</returns>
        public static RedisResult Create(RedisValue[] values) => new ArrayRedisResult(
            values == null ? null : Array.ConvertAll(values, value => new SingleRedisResult(value, null)));

        // internally, this is very similar to RawResult, except it is designed to be usable
        // outside of the IO-processing pipeline: the buffers are standalone, etc

        internal static RedisResult TryCreate(PhysicalConnection connection, RawResult result)
        {
            try
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        return new SingleRedisResult(result.AsRedisValue(), result.Type);
                    case ResultType.MultiBulk:
                        var items = result.GetItems();
                        var arr = result.IsNull ? null : new RedisResult[items.Length];
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var next = TryCreate(connection, items[i]);
                            if (next == null) return null; // means we didn't understand
                            arr[i] = next;
                        }
                        return new ArrayRedisResult(arr);
                    case ResultType.Error:
                        return new ErrorRedisResult(result.GetString());
                    default:
                        return null;
                }
            } catch (Exception ex)
            {
                connection?.OnInternalError(ex);
                return null; // will be logged as a protocol fail by the processor
            }
        }

        /// <summary>
        /// Indicate the type of result that was received from redis
        /// </summary>
        public abstract ResultType Type { get; }

        /// <summary>
        /// Indicates whether this result was a null result
        /// </summary>
        public abstract bool IsNull { get; }

        /// <summary>
        /// Interprets the result as a <see cref="string"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="string"/>.</param>
        public static explicit operator string(RedisResult result) => result.AsString();
        /// <summary>
        /// Interprets the result as a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:byte[]"/>.</param>
        public static explicit operator byte[] (RedisResult result) => result.AsByteArray();
        /// <summary>
        /// Interprets the result as a <see cref="double"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="double"/>.</param>
        public static explicit operator double(RedisResult result) => result.AsDouble();
        /// <summary>
        /// Interprets the result as an <see cref="long"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="long"/>.</param>
        public static explicit operator long(RedisResult result) => result.AsInt64();
        /// <summary>
        /// Interprets the result as an <see cref="int"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="int"/>.</param>
        public static explicit operator int(RedisResult result) => result.AsInt32();
        /// <summary>
        /// Interprets the result as a <see cref="bool"/>
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="bool"/>.</param>
        public static explicit operator bool(RedisResult result) => result.AsBoolean();
        /// <summary>
        /// Interprets the result as a <see cref="RedisValue"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="RedisValue"/>.</param>
        public static explicit operator RedisValue(RedisResult result) => result.AsRedisValue();
        /// <summary>
        /// Interprets the result as a <see cref="RedisKey"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="RedisKey"/>.</param>
        public static explicit operator RedisKey(RedisResult result) => result.AsRedisKey();
        /// <summary>
        /// Interprets the result as a <see cref="T:Nullable{double}"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:Nullable{double}"/>.</param>
        public static explicit operator double? (RedisResult result) => result.AsNullableDouble();
        /// <summary>
        /// Interprets the result as a <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:Nullable{long}"/>.</param>
        public static explicit operator long? (RedisResult result) => result.AsNullableInt64();
        /// <summary>
        /// Interprets the result as a <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:Nullable{int}"/>.</param>
        public static explicit operator int? (RedisResult result) => result.AsNullableInt32();
        /// <summary>
        /// Interprets the result as a <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:Nullable{bool}"/>.</param>
        public static explicit operator bool? (RedisResult result) => result.AsNullableBoolean();
        /// <summary>
        /// Interprets the result as a <see cref="T:string[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:string[]"/>.</param>
        public static explicit operator string[] (RedisResult result) => result.AsStringArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:byte[][]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:byte[][]"/>.</param>
        public static explicit operator byte[][] (RedisResult result) => result.AsByteArrayArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:double[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:double[]"/>.</param>
        public static explicit operator double[] (RedisResult result) => result.AsDoubleArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:long[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:long[]"/>.</param>
        public static explicit operator long[] (RedisResult result) => result.AsInt64Array();
        /// <summary>
        /// Interprets the result as a <see cref="T:int[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:int[]"/>.</param>
        public static explicit operator int[] (RedisResult result) => result.AsInt32Array();
        /// <summary>
        /// Interprets the result as a <see cref="T:bool[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:bool[]"/>.</param>
        public static explicit operator bool[] (RedisResult result) => result.AsBooleanArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:RedisValue[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:RedisValue[]"/>.</param>
        public static explicit operator RedisValue[] (RedisResult result) => result.AsRedisValueArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:RedisKey[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:RedisKey[]"/>.</param>
        public static explicit operator RedisKey[] (RedisResult result) => result.AsRedisKeyArray();
        /// <summary>
        /// Interprets the result as a <see cref="T:RedisResult[]"/>.
        /// </summary>
        /// <param name="result">The result to convert to a <see cref="T:RedisResult[]"/>.</param>
        public static explicit operator RedisResult[] (RedisResult result) => result.AsRedisResultArray();

        internal abstract bool AsBoolean();
        internal abstract bool[] AsBooleanArray();
        internal abstract byte[] AsByteArray();
        internal abstract byte[][] AsByteArrayArray();
        internal abstract double AsDouble();
        internal abstract double[] AsDoubleArray();
        internal abstract int AsInt32();
        internal abstract int[] AsInt32Array();
        internal abstract long AsInt64();
        internal abstract long[] AsInt64Array();
        internal abstract bool? AsNullableBoolean();
        internal abstract double? AsNullableDouble();
        internal abstract int? AsNullableInt32();
        internal abstract long? AsNullableInt64();
        internal abstract RedisKey AsRedisKey();
        internal abstract RedisKey[] AsRedisKeyArray();
        internal abstract RedisResult[] AsRedisResultArray();
        internal abstract RedisValue AsRedisValue();
        internal abstract RedisValue[] AsRedisValueArray();
        internal abstract string AsString();
        internal abstract string[] AsStringArray();
        private sealed class ArrayRedisResult : RedisResult
        {
            public override bool IsNull => value == null;
            private readonly RedisResult[] value;

            public override ResultType Type => ResultType.MultiBulk;
            public ArrayRedisResult(RedisResult[] value)
            {
                this.value = value ?? throw new ArgumentNullException(nameof(value));
            }

            public override string ToString() => value.Length + " element(s)";

            internal override bool AsBoolean()
            {
                if (value.Length == 1) return value[0].AsBoolean();
                throw new InvalidCastException();
            }

            internal override bool[] AsBooleanArray() => Array.ConvertAll(value, x => x.AsBoolean());

            internal override byte[] AsByteArray()
            {
                if (value.Length == 1) return value[0].AsByteArray();
                throw new InvalidCastException();
            }

            internal override byte[][] AsByteArrayArray() => Array.ConvertAll(value, x => x.AsByteArray());

            internal override double AsDouble()
            {
                if (value.Length == 1) return value[0].AsDouble();
                throw new InvalidCastException();
            }

            internal override double[] AsDoubleArray() => Array.ConvertAll(value, x => x.AsDouble());

            internal override int AsInt32()
            {
                if (value.Length == 1) return value[0].AsInt32();
                throw new InvalidCastException();
            }

            internal override int[] AsInt32Array() => Array.ConvertAll(value, x => x.AsInt32());

            internal override long AsInt64()
            {
                if (value.Length == 1) return value[0].AsInt64();
                throw new InvalidCastException();
            }

            internal override long[] AsInt64Array() => Array.ConvertAll(value, x => x.AsInt64());

            internal override bool? AsNullableBoolean()
            {
                if (value.Length == 1) return value[0].AsNullableBoolean();
                throw new InvalidCastException();
            }

            internal override double? AsNullableDouble()
            {
                if (value.Length == 1) return value[0].AsNullableDouble();
                throw new InvalidCastException();
            }

            internal override int? AsNullableInt32()
            {
                if (value.Length == 1) return value[0].AsNullableInt32();
                throw new InvalidCastException();
            }

            internal override long? AsNullableInt64()
            {
                if (value.Length == 1) return value[0].AsNullableInt64();
                throw new InvalidCastException();
            }

            internal override RedisKey AsRedisKey()
            {
                if (value.Length == 1) return value[0].AsRedisKey();
                throw new InvalidCastException();
            }

            internal override RedisKey[] AsRedisKeyArray() => Array.ConvertAll(value, x => x.AsRedisKey());

            internal override RedisResult[] AsRedisResultArray() => value;

            internal override RedisValue AsRedisValue()
            {
                if (value.Length == 1) return value[0].AsRedisValue();
                throw new InvalidCastException();
            }

            internal override RedisValue[] AsRedisValueArray() => Array.ConvertAll(value, x => x.AsRedisValue());

            internal override string AsString()
            {
                if (value.Length == 1) return value[0].AsString();
                throw new InvalidCastException();
            }

            internal override string[] AsStringArray() => Array.ConvertAll(value, x => x.AsString());
        }

        private sealed class ErrorRedisResult : RedisResult
        {
            private readonly string value;

            public override ResultType Type => ResultType.Error;
            public ErrorRedisResult(string value)
            {
                this.value = value ?? throw new ArgumentNullException(nameof(value));
            }

            public override bool IsNull => value == null;
            public override string ToString() => value;
            internal override bool AsBoolean() => throw new RedisServerException(value);
            internal override bool[] AsBooleanArray() => throw new RedisServerException(value);
            internal override byte[] AsByteArray() => throw new RedisServerException(value);
            internal override byte[][] AsByteArrayArray() => throw new RedisServerException(value);
            internal override double AsDouble() => throw new RedisServerException(value);
            internal override double[] AsDoubleArray() => throw new RedisServerException(value);
            internal override int AsInt32() => throw new RedisServerException(value);
            internal override int[] AsInt32Array() => throw new RedisServerException(value);
            internal override long AsInt64() => throw new RedisServerException(value);
            internal override long[] AsInt64Array() => throw new RedisServerException(value);
            internal override bool? AsNullableBoolean() => throw new RedisServerException(value);
            internal override double? AsNullableDouble() => throw new RedisServerException(value);
            internal override int? AsNullableInt32() => throw new RedisServerException(value);
            internal override long? AsNullableInt64() => throw new RedisServerException(value);
            internal override RedisKey AsRedisKey() => throw new RedisServerException(value);
            internal override RedisKey[] AsRedisKeyArray() => throw new RedisServerException(value);
            internal override RedisResult[] AsRedisResultArray() => throw new RedisServerException(value);
            internal override RedisValue AsRedisValue() => throw new RedisServerException(value);
            internal override RedisValue[] AsRedisValueArray() => throw new RedisServerException(value);
            internal override string AsString() => throw new RedisServerException(value);
            internal override string[] AsStringArray() => throw new RedisServerException(value);
        }

        private sealed class SingleRedisResult : RedisResult
        {
            private readonly RedisValue _value;
            private readonly ResultType _resultType;
            public override ResultType Type => _resultType;
            public SingleRedisResult(RedisValue value, ResultType? resultType)
            {
                _value = value;
                _resultType = resultType ?? (value.IsInteger ? ResultType.Integer : ResultType.BulkString);
            }

            public override bool IsNull => _value.IsNull;

            public override string ToString() => _value.ToString();
            internal override bool AsBoolean() => (bool)_value;
            internal override bool[] AsBooleanArray() => new[] { AsBoolean() };
            internal override byte[] AsByteArray() => (byte[])_value;
            internal override byte[][] AsByteArrayArray() => new[] { AsByteArray() };
            internal override double AsDouble() => (double)_value;
            internal override double[] AsDoubleArray() => new[] { AsDouble() };
            internal override int AsInt32() => (int)_value;
            internal override int[] AsInt32Array() => new[] { AsInt32() };
            internal override long AsInt64() => (long)_value;
            internal override long[] AsInt64Array() => new[] { AsInt64() };
            internal override bool? AsNullableBoolean() => (bool?)_value;
            internal override double? AsNullableDouble() => (double?)_value;
            internal override int? AsNullableInt32() => (int?)_value;
            internal override long? AsNullableInt64() => (long?)_value;
            internal override RedisKey AsRedisKey() => (byte[])_value;
            internal override RedisKey[] AsRedisKeyArray() => new[] { AsRedisKey() };
            internal override RedisResult[] AsRedisResultArray() => throw new InvalidCastException();
            internal override RedisValue AsRedisValue() => _value;
            internal override RedisValue[] AsRedisValueArray() => new[] { AsRedisValue() };
            internal override string AsString() => (string)_value;
            internal override string[] AsStringArray() => new[] { AsString() };
        }
    }
}
