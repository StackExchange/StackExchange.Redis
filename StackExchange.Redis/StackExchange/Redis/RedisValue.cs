using System;
#if CORE_CLR
using System.Collections.Generic;
using System.Reflection;
#endif
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents values that can be stored in redis
    /// </summary>
    public struct RedisValue : IEquatable<RedisValue>, IComparable<RedisValue>, IComparable, IConvertible
    {
        internal static readonly RedisValue[] EmptyArray = new RedisValue[0];

        static readonly byte[] EmptyByteArr = new byte[0];

        private static readonly byte[] IntegerSentinel = new byte[0];

        private readonly byte[] valueBlob;

        private readonly long valueInt64;

        // internal bool IsNullOrDefaultValue {  get { return (valueBlob == null && valueInt64 == 0L) || ((object)valueBlob == (object)NullSentinel); } }
        private RedisValue(long valueInt64, byte[] valueBlob)
        {
            this.valueInt64 = valueInt64;
            this.valueBlob = valueBlob;
        }

        /// <summary>
        /// Represents the string <c>""</c>
        /// </summary>
        public static RedisValue EmptyString { get; } = new RedisValue(0, EmptyByteArr);

        /// <summary>
        /// A null value
        /// </summary>
        public static RedisValue Null { get; } = new RedisValue(0, null);

        /// <summary>
        /// Indicates whether the value is a primitive integer
        /// </summary>
        public bool IsInteger => valueBlob == IntegerSentinel;

        /// <summary>
        /// Indicates whether the value should be considered a null value
        /// </summary>
        public bool IsNull => valueBlob == null;

        /// <summary>
        /// Indicates whether the value is either null or a zero-length value
        /// </summary>
        public bool IsNullOrEmpty => valueBlob == null || (valueBlob.Length == 0 && !(valueBlob == IntegerSentinel));

        /// <summary>
        /// Indicates whether the value is greater than zero-length
        /// </summary>
        public bool HasValue => valueBlob != null && valueBlob.Length > 0;

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        public static bool operator !=(RedisValue x, RedisValue y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        public static bool operator ==(RedisValue x, RedisValue y)
        {
            if (x.valueBlob == null) return y.valueBlob == null;

            if (x.valueBlob == IntegerSentinel)
            {
                if (y.valueBlob == IntegerSentinel)
                {
                    return x.valueInt64 == y.valueInt64;
                }
                else
                {
                    return Equals((byte[])x, (byte[])y);
                }
            }
            else if (y.valueBlob == IntegerSentinel)
            {
                return Equals((byte[])x, (byte[])y);
            }

            return Equals(x.valueBlob, y.valueBlob);
        }

        /// <summary>
        /// See Object.Equals()
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj == null) return valueBlob == null;

            if (obj is RedisValue)
            {
                return Equals((RedisValue)obj);
            }
            if (obj is string)
            {
                return (string)obj == (string)this;
            }
            if (obj is byte[])
            {
                return Equals((byte[])obj, (byte[])this);
            }
            if (obj is long)
            {
                return (long)obj == (long)this;
            }
            if (obj is int)
            {
                return (int)obj == (int)this;
            }
            return false;
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        public bool Equals(RedisValue other)
        {
            return this == other;
        }

        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            if (valueBlob == IntegerSentinel) return valueInt64.GetHashCode();
            if (valueBlob == null) return -1;
            return GetHashCode(valueBlob);
        }

        /// <summary>
        /// Returns a string representation of the value
        /// </summary>
        public override string ToString()
        {
            return (string)this;
        }

        internal static unsafe bool Equals(byte[] x, byte[] y)
        {
            if ((object)x == (object)y) return true; // ref equals
            if (x == null || y == null) return false;
            int len = x.Length;
            if (len != y.Length) return false;

            int octets = len / 8, spare = len % 8;
            fixed (byte* x8 = x, y8 = y)
            {
                long* x64 = (long*)x8, y64 = (long*)y8;
                for (int i = 0; i < octets; i++)
                {
                    if (x64[i] != y64[i]) return false;
                }
                int offset = len - spare;
                while (spare-- != 0)
                {
                    if (x8[offset] != y8[offset++]) return false;
                }
            }
            return true;
        }

        internal static unsafe int GetHashCode(byte[] value)
        {
            unchecked
            {
                if (value == null) return -1;
                int len = value.Length;
                if (len == 0) return 0;
                int octets = len / 8, spare = len % 8;
                int acc = 728271210;
                fixed (byte* ptr8 = value)
                {
                    long* ptr64 = (long*)ptr8;
                    for (int i = 0; i < octets; i++)
                    {
                        long val = ptr64[i];
                        int valHash = (((int)val) ^ ((int)(val >> 32)));
                        acc = (((acc << 5) + acc) ^ valHash);
                    }
                    int offset = len - spare;
                    while (spare-- != 0)
                    {
                        acc = (((acc << 5) + acc) ^ ptr8[offset++]);
                    }
                }
                return acc;
            }
        }

        internal static bool TryParseInt64(byte[] value, int offset, int count, out long result)
        {
            result = 0;
            if (value == null || count <= 0) return false;
            checked
            {
                int max = offset + count;
                if (value[offset] == '-')
                {
                    for (int i = offset + 1; i < max; i++)
                    {
                        var b = value[i];
                        if (b < '0' || b > '9') return false;
                        result = (result * 10) - (b - '0');
                    }
                    return true;
                }
                else
                {
                    for (int i = offset; i < max; i++)
                    {
                        var b = value[i];
                        if (b < '0' || b > '9') return false;
                        result = (result * 10) + (b - '0');
                    }
                    return true;
                }
            }
        }

        internal void AssertNotNull()
        {
            if (IsNull) throw new ArgumentException("A null value is not valid in this context");
        }

        enum CompareType {
            Null, Int64, Double, Raw
        }
        CompareType ResolveType(out long i64, out double r8)
        {
            byte[] blob = valueBlob;
            if (blob == IntegerSentinel)
            {
                i64 = valueInt64;
                r8 = default(double);
                return CompareType.Int64;
            }
            if(blob == null)
            {
                i64 = default(long);
                r8 = default(double);
                return CompareType.Null;
            }
            if(TryParseInt64(blob, 0, blob.Length, out i64))
            {
                r8 = default(double);
                return CompareType.Int64;
            }
            if(TryParseDouble(blob, out r8))
            {
                i64 = default(long);
                return CompareType.Double;
            }
            i64 = default(long);
            r8 = default(double);
            return CompareType.Raw;
        }

        /// <summary>
        /// Compare against a RedisValue for relative order
        /// </summary>
        public int CompareTo(RedisValue other)
        {
            try
            {
                long thisInt64, otherInt64;
                double thisDouble, otherDouble;
                CompareType thisType = this.ResolveType(out thisInt64, out thisDouble),
                    otherType = other.ResolveType(out otherInt64, out otherDouble);

                if(thisType == CompareType.Null)
                {
                    return otherType == CompareType.Null ? 0 : -1;
                }
                if(otherType == CompareType.Null)
                {
                    return 1;
                }

                if(thisType == CompareType.Int64)
                {
                    if (otherType == CompareType.Int64) return thisInt64.CompareTo(otherInt64);
                    if (otherType == CompareType.Double) return ((double)thisInt64).CompareTo(otherDouble);
                }
                else if(thisType == CompareType.Double)
                {
                    if (otherType == CompareType.Int64) return thisDouble.CompareTo((double)otherInt64);
                    if (otherType == CompareType.Double) return thisDouble.CompareTo(otherDouble);
                }
                // otherwise, compare as strings
#if !CORE_CLR
                return StringComparer.InvariantCulture.Compare((string)this, (string)other);
#else
                var compareInfo = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
                return compareInfo.Compare((string)this, (string)other, System.Globalization.CompareOptions.Ordinal);
#endif
            }
            catch(Exception ex)
            {
                ConnectionMultiplexer.TraceWithoutContext(ex.Message);
            }
            // if all else fails, consider equivalent
            return 0;
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is RedisValue) return CompareTo((RedisValue)obj);
            if (obj is long) return CompareTo((RedisValue)(long)obj);
            if (obj is double) return CompareTo((RedisValue)(double)obj);
            if (obj is string) return CompareTo((RedisValue)(string)obj);
            if (obj is byte[]) return CompareTo((RedisValue)(byte[])obj);
            if (obj is bool) return CompareTo((RedisValue)(bool)obj);
            return -1;
        }


        /// <summary>
        /// Creates a new RedisValue from an Int32
        /// </summary>
        public static implicit operator RedisValue(int value)
        {
            return new RedisValue(value, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from a nullable Int32
        /// </summary>
        public static implicit operator RedisValue(int? value)
        {
            return value == null ? Null : (RedisValue)value.GetValueOrDefault();
        }
        /// <summary>
        /// Creates a new RedisValue from an Int64
        /// </summary>
        public static implicit operator RedisValue(long value)
        {
            return new RedisValue(value, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from a nullable Int64
        /// </summary>
        public static implicit operator RedisValue(long? value)
        {
            return value == null ? Null : (RedisValue)value.GetValueOrDefault();
        }
        /// <summary>
        /// Creates a new RedisValue from a Double
        /// </summary>
        public static implicit operator RedisValue(double value)
        {
            return Format.ToString(value);
        }

        /// <summary>
        /// Creates a new RedisValue from a nullable Double
        /// </summary>
        public static implicit operator RedisValue(double? value)
        {
            return value == null ? Null : (RedisValue)value.GetValueOrDefault();
        }
        /// <summary>
        /// Creates a new RedisValue from a String
        /// </summary>
        public static implicit operator RedisValue(string value)
        {
            byte[] blob;
            if (value == null) blob = null;
            else if (value.Length == 0) blob = EmptyByteArr;
            else blob = Encoding.UTF8.GetBytes(value);
            return new RedisValue(0, blob);
        }
        /// <summary>
        /// Creates a new RedisValue from a Byte[]
        /// </summary>
        public static implicit operator RedisValue(byte[] value)
        {
            byte[] blob;
            if (value == null) blob = null;
            else if (value.Length == 0) blob = EmptyByteArr;
            else blob = value;
            return new RedisValue(0, blob);
        }

        internal static RedisValue Parse(object obj)
        {
            if (obj == null) return RedisValue.Null;
            if (obj is RedisValue) return (RedisValue)obj;
            if (obj is string) return (RedisValue)(string)obj;
            if (obj is int) return (RedisValue)(int)obj;
            if (obj is double) return (RedisValue)(double)obj;
            if (obj is byte[]) return (RedisValue)(byte[])obj;
            if (obj is bool) return (RedisValue)(bool)obj;
            if (obj is long) return (RedisValue)(long)obj;
            if (obj is float) return (RedisValue)(float)obj;

            throw new InvalidOperationException("Unable to format type for redis: " + obj.GetType().FullName);
        }
        /// <summary>
        /// Creates a new RedisValue from a Boolean
        /// </summary>
        public static implicit operator RedisValue(bool value)
        {
            return new RedisValue(value ? 1 : 0, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from a nullable Boolean
        /// </summary>
        public static implicit operator RedisValue(bool? value)
        {
            return value == null ? Null : (RedisValue)value.GetValueOrDefault();
        }
        /// <summary>
        /// Converts the value to a Boolean
        /// </summary>
        public static explicit operator bool (RedisValue value)
        {
            switch((long)value)
            {
                case 0: return false;
                case 1: return true;
                default: throw new InvalidCastException();
            }
        }

        /// <summary>
        /// Converts the value to an Int32
        /// </summary>
        public static explicit operator int(RedisValue value)
        {
            checked
            {
                return (int)(long)value;
            }
        }
        /// <summary>
        /// Converts the value to an Int64
        /// </summary>
        public static explicit operator long(RedisValue value)
        {
            var blob = value.valueBlob;
            if (blob == IntegerSentinel) return value.valueInt64;
            if (blob == null) return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
            long i64;
            if (TryParseInt64(blob, 0, blob.Length, out i64)) return i64;
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts the value to a Double
        /// </summary>
        public static explicit operator double (RedisValue value)
        {
            var blob = value.valueBlob;
            if (blob == IntegerSentinel) return value.valueInt64;
            if (blob == null) return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")

            double r8;
            if (TryParseDouble(blob, out r8)) return r8;
            throw new InvalidCastException();
        }

        static bool TryParseDouble(byte[] blob, out double value)
        {
            // simple integer?
            if (blob.Length == 1 && blob[0] >= '0' && blob[0] <= '9')
            {
                value = blob[0] - '0';
                return true;
            }

            return Format.TryParseDouble(Encoding.UTF8.GetString(blob), out value);
        }

        /// <summary>
        /// Converts the value to a nullable Double
        /// </summary>
        public static explicit operator double? (RedisValue value)
        {
            if (value.valueBlob == null) return null;
            return (double)value;
        }
        /// <summary>
        /// Converts the value to a nullable Int64
        /// </summary>
        public static explicit operator long? (RedisValue value)
        {
            if (value.valueBlob == null) return null;
            return (long)value;
        }
        /// <summary>
        /// Converts the value to a nullable Int32
        /// </summary>
        public static explicit operator int? (RedisValue value)
        {
            if (value.valueBlob == null) return null;
            return (int)value;
        }
        /// <summary>
        /// Converts the value to a nullable Boolean
        /// </summary>
        public static explicit operator bool? (RedisValue value)
        {
            if (value.valueBlob == null) return null;
            return (bool)value;
        }

        /// <summary>
        /// Converts the value to a String
        /// </summary>
        public static implicit operator string(RedisValue value)
        {
            var valueBlob = value.valueBlob;
            if (valueBlob == IntegerSentinel)
                return Format.ToString(value.valueInt64);
            if (valueBlob == null) return null;

            if (valueBlob.Length == 0) return "";
            if (valueBlob.Length == 2 && valueBlob[0] == (byte)'O' && valueBlob[1] == (byte)'K')
            {
                return "OK"; // special case for +OK status results from modules
            }
            try
            {
                return Encoding.UTF8.GetString(valueBlob);
            }
            catch
            {
                return BitConverter.ToString(valueBlob);
            }
        }
        /// <summary>
        /// Converts the value to a byte[]
        /// </summary>
        public static implicit operator byte[](RedisValue value)
        {
            var valueBlob = value.valueBlob;
            if (valueBlob == IntegerSentinel)
            {
                return Encoding.UTF8.GetBytes(Format.ToString(value.valueInt64));
            }
            return valueBlob;
        }

        TypeCode IConvertible.GetTypeCode() => TypeCode.Object;

        bool IConvertible.ToBoolean(IFormatProvider provider) => (bool)this;

        byte IConvertible.ToByte(IFormatProvider provider) => (byte)this;

        char IConvertible.ToChar(IFormatProvider provider) => (char)this;

        DateTime IConvertible.ToDateTime(IFormatProvider provider) => DateTime.Parse((string)this, provider);

        decimal IConvertible.ToDecimal(IFormatProvider provider) => (decimal)this;

        double IConvertible.ToDouble(IFormatProvider provider) => (double)this;

        short IConvertible.ToInt16(IFormatProvider provider) => (short)this;

        int IConvertible.ToInt32(IFormatProvider provider) => (int)this;

        long IConvertible.ToInt64(IFormatProvider provider) => (long)this;

        sbyte IConvertible.ToSByte(IFormatProvider provider) => (sbyte)this;

        float IConvertible.ToSingle(IFormatProvider provider) => (float)this;

        string IConvertible.ToString(IFormatProvider provider) => (string)this;

        object IConvertible.ToType(Type conversionType, IFormatProvider provider)
        {
            if (conversionType== null) throw new ArgumentNullException(nameof(conversionType));
            if (conversionType== typeof(byte[])) return (byte[])this;
            if (conversionType == typeof(RedisValue)) return this;
            switch(conversionType.GetTypeCode())
            {
                case TypeCode.Boolean: return (bool)this;
                case TypeCode.Byte: return (byte)this;
                case TypeCode.Char: return (char)this;
                case TypeCode.DateTime: return DateTime.Parse((string)this, provider);
                case TypeCode.Decimal: return (decimal)this;
                case TypeCode.Double: return (double)this;
                case TypeCode.Int16: return (short)this;
                case TypeCode.Int32: return (int)this;
                case TypeCode.Int64: return (long)this;
                case TypeCode.SByte: return (sbyte)this;
                case TypeCode.Single: return (float)this;
                case TypeCode.String: return (string)this;
                case TypeCode.UInt16: return (ushort)this;
                case TypeCode.UInt32: return (uint)this;
                case TypeCode.UInt64: return (long)this;
                case TypeCode.Object: return this;
                default:
                    throw new NotSupportedException();
            }
        }

        ushort IConvertible.ToUInt16(IFormatProvider provider) => (ushort)this;

        uint IConvertible.ToUInt32(IFormatProvider provider) => (uint)this;

        ulong IConvertible.ToUInt64(IFormatProvider provider) => (ulong)this;

        /// <summary>
        /// Convert to a long if possible, returning true.
        ///
        /// Returns false otherwise.
        /// </summary>
        public bool TryParse(out long val)
        {
            var blob = valueBlob;
            if (blob == IntegerSentinel)
            {
                val = valueInt64;
                return true;
            }

            if (blob == null)
            {
                // in redis-land 0 approx. equal null; so roll with it
                val = 0;
                return true;
            }

            return TryParseInt64(blob, 0, blob.Length, out val);
        }

        /// <summary>
        /// Convert to a int if possible, returning true.
        ///
        /// Returns false otherwise.
        /// </summary>
        public bool TryParse(out int val)
        {
            long l;
            if (!TryParse(out l) || l > int.MaxValue || l < int.MinValue)
            {
                val = 0;
                return false;
            }

            val = (int)l;
            return true;
        }

        /// <summary>
        /// Convert to a double if possible, returning true.
        ///
        /// Returns false otherwise.
        /// </summary>
        public bool TryParse(out double val)
        {
            var blob = valueBlob;
            if (blob == IntegerSentinel)
            {
                val = valueInt64;
                return true;
            }
            if (blob == null)
            {
                // in redis-land 0 approx. equal null; so roll with it
                val = 0;
                return true;
            }

            return TryParseDouble(blob, out val);
        }
    }

    internal static class ReflectionExtensions
    {
#if CORE_CLR
        internal static TypeCode GetTypeCode(this Type type)
        {
            if (type == null) return TypeCode.Empty;
            TypeCode result;
            if (typeCodeLookup.TryGetValue(type, out result)) return result;

            if (type.GetTypeInfo().IsEnum)
            {
                type = Enum.GetUnderlyingType(type);
                if (typeCodeLookup.TryGetValue(type, out result)) return result;
            }
            return TypeCode.Object;
        }

        static readonly Dictionary<Type, TypeCode> typeCodeLookup = new Dictionary<Type, TypeCode>
        {
            {typeof(bool), TypeCode.Boolean },
            {typeof(byte), TypeCode.Byte },
            {typeof(char), TypeCode.Char},
            {typeof(DateTime), TypeCode.DateTime},
            {typeof(decimal), TypeCode.Decimal},
            {typeof(double), TypeCode.Double },
            {typeof(short), TypeCode.Int16 },
            {typeof(int), TypeCode.Int32 },
            {typeof(long), TypeCode.Int64 },
            {typeof(object), TypeCode.Object},
            {typeof(sbyte), TypeCode.SByte },
            {typeof(float), TypeCode.Single },
            {typeof(string), TypeCode.String },
            {typeof(ushort), TypeCode.UInt16 },
            {typeof(uint), TypeCode.UInt32 },
            {typeof(ulong), TypeCode.UInt64 },
        };
#else
        internal static TypeCode GetTypeCode(this Type type)
        {
            return Type.GetTypeCode(type);
        }
#endif
    }
}
