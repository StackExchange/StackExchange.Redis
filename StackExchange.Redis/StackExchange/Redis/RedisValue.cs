using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents values that can be stored in redis
    /// </summary>
    public struct RedisValue : IEquatable<RedisValue>, IComparable<RedisValue>, IComparable, IConvertible
    {
        internal static readonly RedisValue[] EmptyArray = Array.Empty<RedisValue>();

        private readonly object _objectOrSentinel;
        private readonly ReadOnlyMemory<byte> _memory;
        private readonly long _overlappedValue64;

        // internal bool IsNullOrDefaultValue {  get { return (valueBlob == null && valueInt64 == 0L) || ((object)valueBlob == (object)NullSentinel); } }
        private RedisValue(long overlappedValue64, ReadOnlyMemory<byte> memory, object objectOrSentinel)
        {
            _overlappedValue64 = overlappedValue64;
            _memory = memory;
            _objectOrSentinel = objectOrSentinel;
        }

        private readonly static object Sentinel_Integer = new object();
        private readonly static object Sentinel_Raw = new object();
        private readonly static object Sentinel_Double = new object();
        /// <summary>
        /// Represents the string <c>""</c>
        /// </summary>
        public static RedisValue EmptyString { get; } = new RedisValue(0, default, Sentinel_Raw);

        /// <summary>
        /// A null value
        /// </summary>
        public static RedisValue Null { get; } = new RedisValue(0, default, null);

        /// <summary>
        /// Indicates whether the value is a primitive integer
        /// </summary>
        public bool IsInteger => _objectOrSentinel == Sentinel_Integer;

        /// <summary>
        /// Indicates whether the value should be considered a null value
        /// </summary>
        public bool IsNull => _objectOrSentinel == null;

        /// <summary>
        /// Indicates whether the value is either null or a zero-length value
        /// </summary>
        public bool IsNullOrEmpty
        {
            get
            {
                if (IsNull) return true;
                if (_objectOrSentinel == Sentinel_Raw && _memory.Length == 0) return true;
                if (_objectOrSentinel is string s && s.Length == 0) return true;
                if (_objectOrSentinel is byte[] arr && arr.Length == 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Indicates whether the value is greater than zero-length or has an integer value
        /// </summary>
        public bool HasValue => !IsNullOrEmpty;

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        /// <param name="x">The first <see cref="RedisValue"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisValue"/> to compare.</param>
        public static bool operator !=(RedisValue x, RedisValue y) => !(x == y);

        private double OverlappedValueDouble => BitConverter.Int64BitsToDouble(_overlappedValue64);

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        /// <param name="x">The first <see cref="RedisValue"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisValue"/> to compare.</param>
        public static bool operator ==(RedisValue x, RedisValue y)
        {
            StorageType xType = x.Type, yType = y.Type;

            if (xType == StorageType.Null) return yType == StorageType.Null;
            if (xType == StorageType.Null) return false;

            if (xType == yType)
            {
                switch (xType)
                {
                    case StorageType.Double:
                        return x.OverlappedValueDouble == y.OverlappedValueDouble;
                    case StorageType.Int64:
                        return x._overlappedValue64 == y._overlappedValue64;
                    case StorageType.String:
                        return (string)x._objectOrSentinel == (string)y._objectOrSentinel;
                    case StorageType.Raw:
                        return x._memory.Span.SequenceEqual(y._memory.Span);
                }
            }

            // otherwise, compare as strings
            return (string)x == (string)y;
        }

        /// <summary>
        /// See Object.Equals()
        /// </summary>
        /// <param name="obj">The other <see cref="RedisValue"/> to compare.</param>
        public override bool Equals(object obj)
        {
            if (obj == null) return IsNull;
            if (obj is RedisValue typed) return Equals(typed);
            var other = TryParse(obj);
            if (other.IsNull) return false; // parse fail
            return this == other;
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        /// <param name="other">The <see cref="RedisValue"/> to compare to.</param>
        public bool Equals(RedisValue other) => this == other;

        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            switch (Type)
            {
                case StorageType.Null:
                    return -1;
                case StorageType.Double:
                    return OverlappedValueDouble.GetHashCode();
                case StorageType.Int64:
                    return _overlappedValue64.GetHashCode();
                case StorageType.Raw:
                    return GetHashCode(_memory);
                case StorageType.String:
                default:
                    return _objectOrSentinel.GetHashCode();
            }
        }

        /// <summary>
        /// Returns a string representation of the value
        /// </summary>
        public override string ToString() => (string)this;

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

        internal static unsafe int GetHashCode(ReadOnlyMemory<byte> memory)
        {
            unchecked
            {
                var span8 = memory.Span;
                int len = span8.Length;
                if (len == 0) return 0;

                int acc = 728271210;

                var span64 = MemoryMarshal.Cast<byte, long>(span8);
                for (int i = 0; i < span64.Length; i++)
                {
                    var val = span64[i];
                    int valHash = (((int)val) ^ ((int)(val >> 32)));
                    acc = (((acc << 5) + acc) ^ valHash);
                }
                int spare = len % 8, offset = len - spare;
                while (spare-- != 0)
                {
                    acc = (((acc << 5) + acc) ^ span8[offset++]);
                }
                return acc;
            }
        }
        internal static bool TryParseInt64(ReadOnlySpan<byte> value, out long result)
        {
            result = 0;
            if (value.IsEmpty) return false;
            checked
            {
                int max = value.Length;
                if (value[0] == '-')
                {
                    for (int i = 1; i < max; i++)
                    {
                        var b = value[i];
                        if (b < '0' || b > '9') return false;
                        result = (result * 10) - (b - '0');
                    }
                    return true;
                }
                else
                {
                    for (int i = 0; i < max; i++)
                    {
                        var b = value[i];
                        if (b < '0' || b > '9') return false;
                        result = (result * 10) + (b - '0');
                    }
                    return true;
                }
            }
        }
        internal static bool TryParseInt64(string value, out long result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;
            checked
            {
                int max = value.Length;
                if (value[0] == '-')
                {
                    for (int i = 1; i < max; i++)
                    {
                        var b = value[i];
                        if (b < '0' || b > '9') return false;
                        result = (result * 10) - (b - '0');
                    }
                    return true;
                }
                else
                {
                    for (int i = 0; i < max; i++)
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

        internal enum StorageType
        {
            Null, Int64, Double, Raw, String,
        }

        internal StorageType Type
        {
            get
            {
                var objectOrSentinel = _objectOrSentinel;
                if (objectOrSentinel == null) return StorageType.Null;
                if (objectOrSentinel == Sentinel_Integer) return StorageType.Int64;
                if (objectOrSentinel == Sentinel_Double) return StorageType.Double;
                if (objectOrSentinel == Sentinel_Raw) return StorageType.Raw;
                if (objectOrSentinel is string) return StorageType.String;
                if (objectOrSentinel is byte[]) return StorageType.Raw; // doubled-up, but retaining the array
                throw new InvalidOperationException("Unknown type");
            }
        }

        /// <summary>
        /// Compare against a RedisValue for relative order
        /// </summary>
        /// <param name="other">The other <see cref="RedisValue"/> to compare.</param>
        public int CompareTo(RedisValue other)
        {
            try
            {
                StorageType thisType = this.Type,
                            otherType = other.Type;

                if (thisType == StorageType.Null) return otherType == StorageType.Null ? 0 : -1;
                if (otherType == StorageType.Null) return 1;

                if (thisType == otherType)
                {
                    switch (thisType)
                    {
                        case StorageType.Double:
                            return this.OverlappedValueDouble.CompareTo(other.OverlappedValueDouble);
                        case StorageType.Int64:
                            return this._overlappedValue64.CompareTo(other._overlappedValue64);
                        case StorageType.String:
                            return string.CompareOrdinal((string)this._objectOrSentinel, (string)other._objectOrSentinel);
                        case StorageType.Raw:
                            return this._memory.Span.SequenceCompareTo(other._memory.Span);
                    }
                }

                // otherwise, compare as strings
                return string.CompareOrdinal((string)this, (string)other);
            }
            catch (Exception ex)
            {
                ConnectionMultiplexer.TraceWithoutContext(ex.Message);
            }
            // if all else fails, consider equivalent
            return 0;
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj == null) return CompareTo(Null);

            var val = TryParse(obj);
            if (val.IsNull) return -1; // parse fail

            return CompareTo(val);
        }

        internal static RedisValue TryParse(object obj)
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
            if (obj is ReadOnlyMemory<byte>) return (RedisValue)(ReadOnlyMemory<byte>)obj;
            if (obj is Memory<byte>) return (RedisValue)(Memory<byte>)obj;

            return Null;
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="int"/>.
        /// </summary>
        /// <param name="value">The <see cref="int"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int value) => new RedisValue(value, default, Sentinel_Integer);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{int}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="long"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long value) => new RedisValue(value, default, Sentinel_Integer);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{long}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="double"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(double value)
        {
            try
            {
                var i64 = (long)value;
                if (value == i64) return new RedisValue(i64, default, Sentinel_Integer);
            }
            catch { }
            return new RedisValue(BitConverter.DoubleToInt64Bits(value), default, Sentinel_Double);
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{double}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{double}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(double? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:ReadOnlyMemory{byte}"/>.
        /// </summary>
        public static implicit operator RedisValue(ReadOnlyMemory<byte> value)
        {
            if (value.Length == 0) return EmptyString;
            return new RedisValue(0, value, Sentinel_Raw);
        }
        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:Memory{byte}"/>.
        /// </summary>
        public static implicit operator RedisValue(Memory<byte> value) => (ReadOnlyMemory<byte>)value;

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="string"/>.
        /// </summary>
        /// <param name="value">The <see cref="string"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(string value)
        {
            if (value == null) return Null;
            if (value.Length == 0) return EmptyString;
            return new RedisValue(0, default, value);
        }



        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:byte[]"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(byte[] value)
        {
            if (value == null) return Null;
            if (value.Length == 0) return EmptyString;
            return new RedisValue(0, new Memory<byte>(value), value);
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The <see cref="bool"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(bool value) => new RedisValue(value ? 1 : 0, default, Sentinel_Integer);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{bool}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(bool? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator bool(RedisValue value)
        {
            switch ((long)value)
            {
                case 0: return false;
                case 1: return true;
                default: throw new InvalidCastException();
            }
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="int"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator int(RedisValue value)
        {
            checked
            {
                return (int)(long)value;
            }
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value._overlappedValue64;
                case StorageType.Double:
                    var f64 = value.OverlappedValueDouble;
                    var i64 = (long)f64;
                    if (f64 == i64) return i64;
                    break;
                case StorageType.String:
                    if (TryParseInt64((string)value._objectOrSentinel, out i64)) return i64;
                    break;
                case StorageType.Raw:
                    if (TryParseInt64(value._memory.Span, out i64)) return i64;
                    break;
            }
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator double(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value._overlappedValue64;
                case StorageType.Double:
                    return value.OverlappedValueDouble;
                case StorageType.String:
                    if (Format.TryParseDouble((string)value._objectOrSentinel, out var f64)) return f64;
                    break;
                case StorageType.Raw:
                    if (TryParseDouble(value._memory.Span, out f64)) return f64;
                    break;
            }
            throw new InvalidCastException();
        }

        private static bool TryParseDouble(ReadOnlySpan<byte> blob, out double value)
        {
            // simple integer?
            if (TryParseInt64(blob, out var i64))
            {
                value = i64;
                return true;
            }

            return Format.TryParseDouble(blob, out value);
        }

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{double}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator double? (RedisValue value)
        {
            if (value.IsNull) return null;
            return (double)value;
        }

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long? (RedisValue value)
        {
            if (value.IsNull) return null;
            return (long)value;
        }

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator int? (RedisValue value)
        {
            if (value.IsNull) return null;
            return (int)value;
        }

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator bool? (RedisValue value)
        {
            if (value.IsNull) return null;
            return (bool)value;
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator string(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null: return null;
                case StorageType.Double: return Format.ToString(value.OverlappedValueDouble.ToString());
                case StorageType.Int64: return Format.ToString(value._overlappedValue64);
                case StorageType.String: return (string)value._objectOrSentinel;
                case StorageType.Raw:
                    var span = value._memory.Span;
                    if (span.IsEmpty) return "";
                    if (span.Length == 2 && span[0] == (byte)'O' && span[1] == (byte)'K') return "OK"; // frequent special-case
                    try
                    {
                        return Format.DecodeUtf8(span);
                    }
                    catch
                    {
                        return ToHex(span);
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
        static string ToHex(ReadOnlySpan<byte> src)
        {
            const string HexValues = "0123456789ABCDEF";

            if (src.IsEmpty) return "";
            var s = new string((char)0, src.Length * 3 - 1);
            var dst = MemoryMarshal.AsMemory(s.AsMemory()).Span;

            int i = 0;
            int j = 0;

            byte b = src[i++];
            dst[j++] = HexValues[b >> 4];
            dst[j++] = HexValues[b & 0xF];

            while (i < src.Length)
            {
                b = src[i++];
                dst[j++] = '-';
                dst[j++] = HexValues[b >> 4];
                dst[j++] = HexValues[b & 0xF];
            }
            return s;
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator byte[] (RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null: return null;
                case StorageType.Raw:
                    if (value._objectOrSentinel is byte[] arr) return arr;

                    if (MemoryMarshal.TryGetArray(value._memory, out var segment)
                        && segment.Offset == 0
                        && segment.Count == (segment.Array?.Length ?? -1))
                        return segment.Array; // the memory is backed by an array, and we're reading all of it

                    return value._memory.ToArray();
                case StorageType.Int64:
                    Span<byte> span = stackalloc byte[PhysicalConnection.MaxInt64TextLen];
                    int len = PhysicalConnection.WriteRaw(span, value._overlappedValue64, false, 0);
                    arr = new byte[len - 2]; // don't need the CRLF
                    span.Slice(0, arr.Length).CopyTo(arr);
                    return arr;
            }
            // fallback: stringify and encode
            return Encoding.UTF8.GetBytes((string)value);
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a ReadOnlyMemory
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator ReadOnlyMemory<byte>(RedisValue value)
            => value.Type == StorageType.Raw ? value._memory : (byte[])value;

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
            if (conversionType == null) throw new ArgumentNullException(nameof(conversionType));
            if (conversionType == typeof(byte[])) return (byte[])this;
            if (conversionType == typeof(ReadOnlyMemory<byte>)) return (ReadOnlyMemory<byte>)this;
            if (conversionType == typeof(RedisValue)) return this;
            switch (System.Type.GetTypeCode(conversionType))
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
        /// <param name="val">The <see cref="long"/> value, if conversion was possible.</param>
        public bool TryParse(out long val)
        {
            switch (Type)
            {
                case StorageType.Null: val = 0; return true; // in redis-land 0 approx. equal null; so roll with it
                case StorageType.Int64: val = _overlappedValue64; return true;
                case StorageType.String: return TryParseInt64((string)_objectOrSentinel, out val);
                case StorageType.Raw: return TryParseInt64(_memory.Span, out val);
                case StorageType.Double:
                    var f64 = OverlappedValueDouble;
                    if (f64 >= long.MinValue && f64 <= long.MaxValue)
                    {
                        val = (long)f64;
                        return true;
                    }
                    break;
            }
            val = default;
            return false;
        }

        /// <summary>
        /// Convert to a int if possible, returning true.
        ///
        /// Returns false otherwise.
        /// </summary>
        /// <param name="val">The <see cref="int"/> value, if conversion was possible.</param>
        public bool TryParse(out int val)
        {
            if (TryParse(out long l) && l >= int.MinValue && l <= int.MaxValue)
            {
                val = (int)l;
                return true;
            }
            val = default;
            return false;

        }

        /// <summary>
        /// Convert to a double if possible, returning true.
        ///
        /// Returns false otherwise.
        /// </summary>
        /// <param name="val">The <see cref="double"/> value, if conversion was possible.</param>
        public bool TryParse(out double val)
        {
            switch (Type)
            {
                case StorageType.Null: val = 0; return true;
                case StorageType.Int64: val = _overlappedValue64; return true;
                case StorageType.Double: val = OverlappedValueDouble; return true;
                case StorageType.String: return Format.TryParseDouble((string)_objectOrSentinel, out val);
                case StorageType.Raw: return TryParseDouble(_memory.Span, out val);
            }
            val = default;
            return false;
        }
    }
}
