using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents values that can be stored in redis
    /// </summary>
    public readonly struct RedisValue : IEquatable<RedisValue>, IComparable<RedisValue>, IComparable, IConvertible
    {
        internal static readonly RedisValue[] EmptyArray = Array.Empty<RedisValue>();

        private readonly object _objectOrSentinel;
        private readonly ReadOnlyMemory<byte> _memory;
        private readonly long _overlappedBits64;

        // internal bool IsNullOrDefaultValue {  get { return (valueBlob == null && valueInt64 == 0L) || ((object)valueBlob == (object)NullSentinel); } }
        private RedisValue(long overlappedValue64, ReadOnlyMemory<byte> memory, object objectOrSentinel)
        {
            _overlappedBits64 = overlappedValue64;
            _memory = memory;
            _objectOrSentinel = objectOrSentinel;
        }

        internal RedisValue(object obj, long overlappedBits)
        {   // this creates a bodged RedisValue which should **never**
            // be seen directly; the contents are ... unexpected
            _overlappedBits64 = overlappedBits;
            _objectOrSentinel = obj;
            _memory = default;
        }

        /// <summary>
        /// Creates a <see cref="RedisValue"/> from a string.
        /// </summary>
        public RedisValue(string value) : this(0, default, value) { }

#pragma warning disable RCS1085 // Use auto-implemented property.
        internal object DirectObject => _objectOrSentinel;
        internal long DirectOverlappedBits64 => _overlappedBits64;
#pragma warning restore RCS1085 // Use auto-implemented property.

        private readonly static object Sentinel_SignedInteger = new object();
        private readonly static object Sentinel_UnsignedInteger = new object();
        private readonly static object Sentinel_Raw = new object();
        private readonly static object Sentinel_Double = new object();

        /// <summary>
        /// Obtain this value as an object - to be used alongside Unbox
        /// </summary>
        public object Box()
        {
            var obj = _objectOrSentinel;
            if (obj is null || obj is string || obj is byte[]) return obj;
            if (obj == Sentinel_SignedInteger)
            {
                var l = OverlappedValueInt64;
                if (l >= -1 && l <= 20) return s_CommonInt32[((int)l) + 1];
                return l;
            }
            if (obj == Sentinel_UnsignedInteger)
            {
                return OverlappedValueUInt64;
            }
            if (obj == Sentinel_Double)
            {
                var d = OverlappedValueDouble;
                if (double.IsPositiveInfinity(d)) return s_DoublePosInf;
                if (double.IsNegativeInfinity(d)) return s_DoubleNegInf;
                if (double.IsNaN(d)) return s_DoubleNAN;
                return d;
            }
            if (obj == Sentinel_Raw && _memory.IsEmpty) return s_EmptyString;
            return this;
        }

        /// <summary>
        /// Parse this object as a value - to be used alongside Box.
        /// </summary>
        /// <param name="value">The value to unbox.</param>
        public static RedisValue Unbox(object value)
        {
            var val = TryParse(value, out var valid);
            if (!valid) throw new ArgumentException(nameof(value));
            return val;
        }

        /// <summary>
        /// Represents the string <c>""</c>
        /// </summary>
        public static RedisValue EmptyString { get; } = new RedisValue(0, default, Sentinel_Raw);

        // note: it is *really important* that this s_EmptyString assignment happens *after* the EmptyString initializer above!
        static readonly object s_DoubleNAN = double.NaN, s_DoublePosInf = double.PositiveInfinity, s_DoubleNegInf = double.NegativeInfinity,
            s_EmptyString = RedisValue.EmptyString;
        static readonly object[] s_CommonInt32 = Enumerable.Range(-1, 22).Select(i => (object)i).ToArray(); // [-1,20] = 22 values

        /// <summary>
        /// A null value
        /// </summary>
        public static RedisValue Null { get; } = new RedisValue(0, default, null);

        /// <summary>
        /// Indicates whether the value is a primitive integer (signed or unsigned)
        /// </summary>
        public bool IsInteger => _objectOrSentinel == Sentinel_SignedInteger || _objectOrSentinel == Sentinel_UnsignedInteger;

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
                if (_objectOrSentinel == Sentinel_Raw && _memory.IsEmpty) return true;
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

        private double OverlappedValueDouble
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitConverter.Int64BitsToDouble(_overlappedBits64);
        }

        internal long OverlappedValueInt64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _overlappedBits64;
        }

        internal ulong OverlappedValueUInt64
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unchecked((ulong)_overlappedBits64);
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        /// <param name="x">The first <see cref="RedisValue"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisValue"/> to compare.</param>
        public static bool operator ==(RedisValue x, RedisValue y)
        {
            x = x.Simplify();
            y = y.Simplify();
            StorageType xType = x.Type, yType = y.Type;

            if (xType == StorageType.Null) return yType == StorageType.Null;
            if (xType == StorageType.Null) return false;

            if (xType == yType)
            {
                switch (xType)
                {
                    case StorageType.Double: // make sure we use double equality rules
                        return x.OverlappedValueDouble == y.OverlappedValueDouble;
                    case StorageType.Int64:
                    case StorageType.UInt64: // as long as xType == yType, only need to check the bits
                        return x._overlappedBits64 == y._overlappedBits64;
                    case StorageType.String:
                        return (string)x._objectOrSentinel == (string)y._objectOrSentinel;
                    case StorageType.Raw:
                        return x._memory.Span.SequenceEqual(y._memory.Span);
                }
            }

            // if either is a numeric type, and the other isn't the *same* type (above), then:
            // it can't be equal
            switch (xType)
            {
                case StorageType.UInt64:
                case StorageType.Int64:
                case StorageType.Double:
                    return false;
            }
            switch (yType)
            {
                case StorageType.UInt64:
                case StorageType.Int64:
                case StorageType.Double:
                    return false;
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
            var other = TryParse(obj, out var valid);
            return valid && this == other; // can't be equal if parse fail
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent
        /// </summary>
        /// <param name="other">The <see cref="RedisValue"/> to compare to.</param>
        public bool Equals(RedisValue other) => this == other;

        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode() => GetHashCode(this);
        private static int GetHashCode(RedisValue x)
        {
            x = x.Simplify();
            switch (x.Type)
            {
                case StorageType.Null:
                    return -1;
                case StorageType.Double:
                    return x.OverlappedValueDouble.GetHashCode();
                case StorageType.Int64:
                case StorageType.UInt64:
                    return x._overlappedBits64.GetHashCode();
                case StorageType.Raw:
                    return ((string)x).GetHashCode(); // to match equality
                case StorageType.String:
                default:
                    return x._objectOrSentinel.GetHashCode();
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
        

        internal void AssertNotNull()
        {
            if (IsNull) throw new ArgumentException("A null value is not valid in this context");
        }

        internal enum StorageType
        {
            Null, Int64, UInt64, Double, Raw, String,
        }

        internal StorageType Type
        {
            get
            {
                var objectOrSentinel = _objectOrSentinel;
                if (objectOrSentinel == null) return StorageType.Null;
                if (objectOrSentinel == Sentinel_SignedInteger) return StorageType.Int64;
                if (objectOrSentinel == Sentinel_Double) return StorageType.Double;
                if (objectOrSentinel == Sentinel_Raw) return StorageType.Raw;
                if (objectOrSentinel is string) return StorageType.String;
                if (objectOrSentinel is byte[]) return StorageType.Raw; // doubled-up, but retaining the array
                if (objectOrSentinel == Sentinel_UnsignedInteger) return StorageType.UInt64;
                throw new InvalidOperationException("Unknown type");
            }
        }

        /// <summary>
        /// Get the size of this value in bytes
        /// </summary>
        public long Length()
        {
            switch (Type)
            {
                case StorageType.Null: return 0;
                case StorageType.Raw: return _memory.Length;
                case StorageType.String: return Encoding.UTF8.GetByteCount((string)_objectOrSentinel);
                default: throw new InvalidOperationException("Unable to compute length of type: " + Type);
            }
        }

        /// <summary>
        /// Compare against a RedisValue for relative order
        /// </summary>
        /// <param name="other">The other <see cref="RedisValue"/> to compare.</param>
        public int CompareTo(RedisValue other) => CompareTo(this, other);

        private static int CompareTo(RedisValue x, RedisValue y)
        {
            try
            {
                x = x.Simplify();
                y = y.Simplify();
                StorageType xType = x.Type, yType = y.Type;

                if (xType == StorageType.Null) return yType == StorageType.Null ? 0 : -1;
                if (yType == StorageType.Null) return 1;

                if (xType == yType)
                {
                    switch (xType)
                    {
                        case StorageType.Double:
                            return x.OverlappedValueDouble.CompareTo(y.OverlappedValueDouble);
                        case StorageType.Int64:
                            return x.OverlappedValueInt64.CompareTo(y.OverlappedValueInt64);
                        case StorageType.UInt64:
                            return x.OverlappedValueUInt64.CompareTo(y.OverlappedValueUInt64);
                        case StorageType.String:
                            return string.CompareOrdinal((string)x._objectOrSentinel, (string)y._objectOrSentinel);
                        case StorageType.Raw:
                            return x._memory.Span.SequenceCompareTo(y._memory.Span);
                    }
                }

                switch (xType)
                { // numbers can be still be compared between types
                    case StorageType.Double:
                        if (yType == StorageType.Int64) return x.OverlappedValueDouble.CompareTo((double)y.OverlappedValueInt64);
                        if (yType == StorageType.UInt64) return x.OverlappedValueDouble.CompareTo((double)y.OverlappedValueUInt64);
                        break;
                    case StorageType.Int64:
                        if (yType == StorageType.Double) return ((double)x.OverlappedValueInt64).CompareTo(y.OverlappedValueDouble);
                        if (yType == StorageType.UInt64) return 1; // we only use unsigned if > int64, so: y is bigger
                        break;
                    case StorageType.UInt64:
                        if (yType == StorageType.Double) return ((double)x.OverlappedValueUInt64).CompareTo(y.OverlappedValueDouble);
                        if (yType == StorageType.Int64) return -1; // we only use unsigned if > int64, so: x is bigger
                        break;
                }

                // otherwise, compare as strings
                return string.CompareOrdinal((string)x, (string)y);
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

            var val = TryParse(obj, out var valid);
            if (!valid) return -1; // parse fail

            return CompareTo(val);
        }

        internal static RedisValue TryParse(object obj, out bool valid)
        {
            valid = true;
            switch (obj)
            {
                case null: return Null;
                case string v: return v;
                case int v: return v;
                case uint v: return v;
                case double v: return v;
                case byte[] v: return v;
                case bool v: return v;
                case long v: return v;
                case ulong v: return v;
                case float v: return v;
                case ReadOnlyMemory<byte> v: return v;
                case Memory<byte> v: return v;
                case RedisValue v: return v;
                default:
                    valid = false;
                    return Null;
            }
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="int"/>.
        /// </summary>
        /// <param name="value">The <see cref="int"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int value) => new RedisValue(value, default, Sentinel_SignedInteger);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{int}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="long"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long value) => new RedisValue(value, default, Sentinel_SignedInteger);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{long}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(ulong value)
        {
            const ulong MSB = (1UL) << 63;
            return (value & MSB) == 0
                ? new RedisValue((long)value, default, Sentinel_SignedInteger) // prefer signed whenever we can
                : new RedisValue(unchecked((long)value), default, Sentinel_UnsignedInteger); // with unsigned as the fallback
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{ulong}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{ulong}"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(ulong? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The <see cref="uint"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(uint value) => new RedisValue(value, default, Sentinel_SignedInteger); // 32-bits always fits as signed

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{uint}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{uint}"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(uint? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="double"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(double value)
        {
            try
            {
                var i64 = (long)value;
                // note: double doesn't offer integer accuracy at 64 bits, so we know it can't be unsigned (only use that for 64-bit)
                if (value == i64) return new RedisValue(i64, default, Sentinel_SignedInteger);
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
        /// <param name="value">The <see cref="T:ReadOnlyMemory{byte}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(ReadOnlyMemory<byte> value)
        {
            if (value.Length == 0) return EmptyString;
            return new RedisValue(0, value, Sentinel_Raw);
        }
        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:Memory{byte}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Memory{byte}"/> to convert to a <see cref="RedisValue"/>.</param>
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
        public static implicit operator RedisValue(bool value) => new RedisValue(value ? 1 : 0, default, Sentinel_SignedInteger);

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
            => checked((int)(long)value);

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value.OverlappedValueInt64;
                case StorageType.UInt64:
                    return checked((long)value.OverlappedValueUInt64); // this will throw since unsigned is always 64-bit
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to long: '{value}'");
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator uint(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return checked((uint)value.OverlappedValueInt64);
                case StorageType.UInt64:
                    return checked((uint)value.OverlappedValueUInt64);
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to uint: '{value}'");
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator ulong(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return checked((ulong)value.OverlappedValueInt64); // throw if negative
                case StorageType.UInt64:
                    return value.OverlappedValueUInt64;
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to ulong: '{value}'");
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator double(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value.OverlappedValueInt64;
                case StorageType.UInt64:
                    return value.OverlappedValueUInt64;
                case StorageType.Double:
                    return value.OverlappedValueDouble;
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to double: '{value}'");
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="decimal"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator decimal(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value.OverlappedValueInt64;
                case StorageType.UInt64:
                    return value.OverlappedValueUInt64;
                case StorageType.Double:
                    return (decimal)value.OverlappedValueDouble;
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to decimal: '{value}'");
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="float"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator float(RedisValue value)
        {
            value = value.Simplify();
            switch (value.Type)
            {
                case StorageType.Null:
                    return 0; // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                case StorageType.Int64:
                    return value.OverlappedValueInt64;
                case StorageType.UInt64:
                    return value.OverlappedValueUInt64;
                case StorageType.Double:
                    return (float)value.OverlappedValueDouble;
            }
            throw new InvalidCastException($"Unable to cast from {value.Type} to double: '{value}'");
        }

        private static bool TryParseDouble(ReadOnlySpan<byte> blob, out double value)
        {
            // simple integer?
            if (Format.CouldBeInteger(blob) && Format.TryParseInt64(blob, out var i64))
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
            => value.IsNull ? (double?)null : (double)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{float}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator float? (RedisValue value)
            => value.IsNull ? (float?)null : (float)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{decimal}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator decimal? (RedisValue value)
            => value.IsNull ? (decimal?)null : (decimal)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long? (RedisValue value)
            => value.IsNull ? (long?)null : (long)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{ulong}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator ulong? (RedisValue value)
            => value.IsNull ? (ulong?)null : (ulong)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator int? (RedisValue value)
            => value.IsNull ? (int?)null : (int)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{uint}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator uint? (RedisValue value)
            => value.IsNull ? (uint?)null : (uint)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator bool? (RedisValue value)
            => value.IsNull ? (bool?)null : (bool)value;

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator string(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null: return null;
                case StorageType.Double: return Format.ToString(value.OverlappedValueDouble);
                case StorageType.Int64: return Format.ToString(value.OverlappedValueInt64);
                case StorageType.UInt64: return Format.ToString(value.OverlappedValueUInt64);
                case StorageType.String: return (string)value._objectOrSentinel;
                case StorageType.Raw:
                    var span = value._memory.Span;
                    if (span.IsEmpty) return "";
                    if (span.Length == 2 && span[0] == (byte)'O' && span[1] == (byte)'K') return "OK"; // frequent special-case
                    try
                    {
                        return Format.GetString(span);
                    }
                    catch
                    {
                        return ToHex(span);
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
        private static string ToHex(ReadOnlySpan<byte> src)
        {
            const string HexValues = "0123456789ABCDEF";

            if (src.IsEmpty) return "";
            var s = new string((char)0, (src.Length * 3) - 1);
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
                    {
                        return segment.Array; // the memory is backed by an array, and we're reading all of it
                    }

                    return value._memory.ToArray();
                case StorageType.Int64:
                    Span<byte> span = stackalloc byte[PhysicalConnection.MaxInt64TextLen + 2];
                    int len = PhysicalConnection.WriteRaw(span, value.OverlappedValueInt64, false, 0);
                    arr = new byte[len - 2]; // don't need the CRLF
                    span.Slice(0, arr.Length).CopyTo(arr);
                    return arr;
                case StorageType.UInt64:
                    // we know it is a huge value - just jump straight to Utf8Formatter
                    span = stackalloc byte[PhysicalConnection.MaxInt64TextLen];
                    if (!Utf8Formatter.TryFormat(value.OverlappedValueUInt64, span, out len))
                        throw new InvalidOperationException("TryFormat failed");
                    arr = new byte[len];
                    span.Slice(0, len).CopyTo(arr);
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
        byte IConvertible.ToByte(IFormatProvider provider) => (byte)(uint)this;
        char IConvertible.ToChar(IFormatProvider provider) => (char)(uint)this;
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
                case TypeCode.Byte: return checked((byte)(uint)this);
                case TypeCode.Char: return checked((char)(uint)this);
                case TypeCode.DateTime: return DateTime.Parse((string)this, provider);
                case TypeCode.Decimal: return (decimal)this;
                case TypeCode.Double: return (double)this;
                case TypeCode.Int16: return (short)this;
                case TypeCode.Int32: return (int)this;
                case TypeCode.Int64: return (long)this;
                case TypeCode.SByte: return (sbyte)this;
                case TypeCode.Single: return (float)this;
                case TypeCode.String: return (string)this;
                case TypeCode.UInt16: return checked((ushort)(uint)this);
                case TypeCode.UInt32: return (uint)this;
                case TypeCode.UInt64: return (ulong)this;
                case TypeCode.Object: return this;
                default:
                    throw new NotSupportedException();
            }
        }

        ushort IConvertible.ToUInt16(IFormatProvider provider) => checked((ushort)(uint)this);
        uint IConvertible.ToUInt32(IFormatProvider provider) => (uint)this;
        ulong IConvertible.ToUInt64(IFormatProvider provider) => (ulong)this;

        /// <summary>
        /// Attempt to reduce to canonical terms ahead of time; parses integers, floats, etc
        /// Note: we don't use this aggressively ahead of time, a: because of extra CPU,
        /// but more importantly b: because it can change values - for example, if they start
        /// with "123.000", it should **stay** as "123.000", not become 123L; this could be
        /// a hash key or similar - we don't want to break it; RedisConnection uses
        /// the storage type, not the "does it look like a long?" - for this reason
        /// </summary>
        internal RedisValue Simplify()
        {
            long i64;
            ulong u64;
            switch (Type)
            {
                case StorageType.String:
                    string s = (string)_objectOrSentinel;
                    if (Format.CouldBeInteger(s))
                    {
                        if (Format.TryParseInt64(s, out i64)) return i64;
                        if (Format.TryParseUInt64(s, out u64)) return u64;
                    }
                    if (Format.TryParseDouble(s, out var f64)) return f64;
                    break;
                case StorageType.Raw:
                    var b = _memory.Span;
                    if (Format.CouldBeInteger(b))
                    {
                        if (Format.TryParseInt64(b, out i64)) return i64;
                        if (Format.TryParseUInt64(b, out u64)) return u64;
                    }
                    if (TryParseDouble(b, out f64)) return f64;
                    break;
                case StorageType.Double:
                    // is the double actually an integer?
                    f64 = OverlappedValueDouble;
                    if (f64 >= long.MinValue && f64 <= long.MaxValue && (i64 = (long)f64) == f64) return i64;
                    break;
            }
            return this;
        }

        /// <summary>
        /// <para>Convert to a signed long if possible, returning true.</para>
        /// <para>Returns false otherwise.</para>
        /// </summary>
        /// <param name="val">The <see cref="long"/> value, if conversion was possible.</param>
        public bool TryParse(out long val)
        {
            switch (Type)
            {
                case StorageType.Int64:
                    val = OverlappedValueInt64;
                    return true;
                case StorageType.UInt64:
                    // we only use unsigned for oversize, so no: it doesn't fit
                    val = default;
                    return false;
                case StorageType.String:
                    return Format.TryParseInt64((string)_objectOrSentinel, out val);
                case StorageType.Raw:
                    return Format.TryParseInt64(_memory.Span, out val);
                case StorageType.Double:
                    var d = OverlappedValueDouble;
                    try { val = (long)d; }
                    catch { val = default; return false; }
                    return val == d;
                case StorageType.Null:
                    // in redis-land 0 approx. equal null; so roll with it
                    val = 0;
                    return true;
            }
            val = default;
            return false;
        }

        /// <summary>
        /// <para>Convert to a int if possible, returning true.</para>
        /// <para>Returns false otherwise.</para>
        /// </summary>
        /// <param name="val">The <see cref="int"/> value, if conversion was possible.</param>
        public bool TryParse(out int val)
        {
            if (!TryParse(out long l) || l > int.MaxValue || l < int.MinValue)
            {
                val = 0;
                return false;
            }

            val = (int)l;
            return true;
        }

        /// <summary>
        /// <para>Convert to a double if possible, returning true.</para>
        /// <para>Returns false otherwise.</para>
        /// </summary>
        /// <param name="val">The <see cref="double"/> value, if conversion was possible.</param>
        public bool TryParse(out double val)
        {
            switch (Type)
            {
                case StorageType.Int64:
                    val = OverlappedValueInt64;
                    return true;
                case StorageType.UInt64:
                    val = OverlappedValueUInt64;
                    return true;
                case StorageType.Double:
                    val = OverlappedValueDouble;
                    return true;
                case StorageType.String:
                    return Format.TryParseDouble((string)_objectOrSentinel, out val);
                case StorageType.Raw:
                    return TryParseDouble(_memory.Span, out val);
                case StorageType.Null:
                    // in redis-land 0 approx. equal null; so roll with it
                    val = 0;
                    return true;
            }
            val = default;
            return false;
        }

        /// <summary>
        /// Create a RedisValue from a MemoryStream; it will *attempt* to use the internal buffer
        /// directly, but if this isn't possibly it will fallback to ToArray
        /// </summary>
        /// <param name="stream">The <see cref="MemoryStream"/> to create a value from.</param>
        public static RedisValue CreateFrom(MemoryStream stream)
        {
            if (stream == null) return Null;
            if (stream.Length == 0) return Array.Empty<byte>();
            if (stream.TryGetBuffer(out var segment) || ReflectionTryGetBuffer(stream, out segment))
            {
                return new Memory<byte>(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                // nowhere near as efficient, but...
                return stream.ToArray();
            }
        }

        private static readonly FieldInfo
            s_origin = typeof(MemoryStream).GetField("_origin", BindingFlags.NonPublic | BindingFlags.Instance),
            s_buffer = typeof(MemoryStream).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool ReflectionTryGetBuffer(MemoryStream ms, out ArraySegment<byte> buffer)
        {
            if (s_origin != null && s_buffer != null)
            {
                try
                {
                    int offset = (int)s_origin.GetValue(ms);
                    byte[] arr = (byte[])s_buffer.GetValue(ms);
                    buffer = new ArraySegment<byte>(arr, offset, checked((int)ms.Length));
                    return true;
                }
                catch { }
            }
            buffer = default;
            return false;
        }

        /// <summary>
        /// Indicates whether the current value has the supplied value as a prefix.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to check.</param>
        public bool StartsWith(RedisValue value)
        {
            if (IsNull || value.IsNull) return false;
            if (value.IsNullOrEmpty) return true;
            if (IsNullOrEmpty) return false;

            ReadOnlyMemory<byte> rawThis, rawOther;
            var thisType = Type;
            if (thisType == value.Type) // same? can often optimize
            {
                switch (thisType)
                {
                    case StorageType.String:
                        var sThis = ((string)_objectOrSentinel);
                        var sOther = ((string)value._objectOrSentinel);
                        return sThis.StartsWith(sOther, StringComparison.Ordinal);
                    case StorageType.Raw:
                        rawThis = _memory;
                        rawOther = value._memory;
                        return rawThis.Span.StartsWith(rawOther.Span);
                }
            }
            byte[] arr0 = null, arr1 = null;
            try
            {
                rawThis = AsMemory(out arr0);
                rawOther = value.AsMemory(out arr1);

                return rawThis.Span.StartsWith(rawOther.Span);
            }
            finally
            {
                if (arr0 != null) ArrayPool<byte>.Shared.Return(arr0);
                if (arr1 != null) ArrayPool<byte>.Shared.Return(arr1);
            }
        }

        private ReadOnlyMemory<byte> AsMemory(out byte[] leased)
        {
            switch (Type)
            {
                case StorageType.Raw:
                    leased = null;
                    return _memory;
                case StorageType.String:
                    string s = (string)_objectOrSentinel;
                    HaveString:
                    if (s.Length == 0)
                    {
                        leased = null;
                        return default;
                    }
                    leased = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(s));
                    var len = Encoding.UTF8.GetBytes(s, 0, s.Length, leased, 0);
                    return new ReadOnlyMemory<byte>(leased, 0, len);
                case StorageType.Double:
                    s = Format.ToString(OverlappedValueDouble);
                    goto HaveString;
                case StorageType.Int64:
                    leased = ArrayPool<byte>.Shared.Rent(PhysicalConnection.MaxInt64TextLen + 2); // reused code has CRLF terminator
                    len = PhysicalConnection.WriteRaw(leased, OverlappedValueInt64) - 2; // drop the CRLF
                    return new ReadOnlyMemory<byte>(leased, 0, len);
                case StorageType.UInt64:
                    leased = ArrayPool<byte>.Shared.Rent(PhysicalConnection.MaxInt64TextLen); // reused code has CRLF terminator
                    // value is huge, jump direct to Utf8Formatter
                    if (!Utf8Formatter.TryFormat(OverlappedValueUInt64, leased, out len))
                        throw new InvalidOperationException("TryFormat failed");
                    return new ReadOnlyMemory<byte>(leased, 0, len);
            }
            leased = null;
            return default;
        }
    }
}
