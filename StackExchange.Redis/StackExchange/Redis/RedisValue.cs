using System;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents values that can be stored in redis
    /// </summary>
    public struct RedisValue : IEquatable<RedisValue>
    {
        internal static readonly RedisValue[] EmptyArray = new RedisValue[0];

        private static readonly RedisValue
            @null = new RedisValue(0, null),
            emptyString = new RedisValue(0, EmptyByteArr);

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
        public static RedisValue EmptyString { get { return emptyString; } }

        /// <summary>
        /// A null value
        /// </summary>
        public static RedisValue Null { get { return @null; } }

        /// <summary>
        /// Indicates whether the value is a primitive integer
        /// </summary>
        public bool IsInteger { get { return valueBlob == IntegerSentinel; } }

        /// <summary>
        /// Indicates whether the value should be considered a null value
        /// </summary>
        public bool IsNull { get { return valueBlob == null; } }

        /// <summary>
        /// Indicates whether the value is either null or a zero-length value
        /// </summary>
        public bool IsNullOrEmpty
        {
            get
            {
                return valueBlob == null || (valueBlob.Length == 0 && !(valueBlob == IntegerSentinel));
            }
        }

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
                    Equals((byte[])x, (byte[])y);
                }
            }
            else if (y.valueBlob == IntegerSentinel)
            {
                Equals((byte[])x, (byte[])y);
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
                bool neg = value[offset] == '-';
                int max = offset + count;
                for (int i = neg ? (offset + 1) : offset; i < max; i++)
                {
                    var b = value[i];
                    if (b < '0' || b > '9') return false;
                    result = (result * 10) + (b - '0');
                }
                if (neg) result = -result;
                return true;
            }
        }

        internal RedisValue Assert()
        {
            if (IsNull) throw new ArgumentException("A null value is not valid in this context");
            return this;
        }
        /// <summary>
        /// Creates a new RedisValue from an Int32
        /// </summary>
        public static implicit operator RedisValue(int value)
        {
            return new RedisValue(value, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from an Int64
        /// </summary>
        public static implicit operator RedisValue(long value)
        {
            return new RedisValue(value, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from a Double
        /// </summary>
        public static implicit operator RedisValue(double value)
        {
            return Format.ToString(value);
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
        /// <summary>
        /// Creates a new RedisValue from a Boolean
        /// </summary>
        public static implicit operator RedisValue(bool value)
        {
            return new RedisValue(value ? 1 : 0, IntegerSentinel);
        }
        /// <summary>
        /// Creates a new RedisValue from a Boolean
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

            // simple integer?
            if (blob.Length == 1 && blob[0] >= '0' && blob[0] <= '9') return (blob[0] - '0');

            double r8;
            if (Format.TryParseDouble(Encoding.UTF8.GetString(blob), out r8)) return r8;
            throw new InvalidCastException();
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
    }
}
