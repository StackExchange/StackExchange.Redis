using System;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a key that can be stored in redis
    /// </summary>
    public struct RedisKey : IEquatable<RedisKey>
    {
        internal static readonly RedisKey[] EmptyArray = new RedisKey[0];
        private readonly byte[] keyPrefix;
        private readonly object keyValue; // always either a string or a byte[]
        internal RedisKey(byte[] keyPrefix, object keyValue)
        {
            this.keyPrefix = keyPrefix?.Length == 0 ? null : keyPrefix;
            this.keyValue = keyValue;
        }

        internal RedisKey AsPrefix() => new RedisKey((byte[])this, null);

        internal bool IsNull => keyPrefix == null && keyValue == null;

        internal bool IsEmpty
        {
            get
            {
                if (keyPrefix != null) return false;
                if (keyValue == null) return true;
                if (keyValue is string) return ((string)keyValue).Length == 0;
                return ((byte[])keyValue).Length == 0;
            }
        }

        internal byte[] KeyPrefix => keyPrefix;
        internal object KeyValue => keyValue;

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisKey x, RedisKey y) => !(x == y);

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(string x, RedisKey y) => !(x == y);

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(byte[] x, RedisKey y) => !(x == y);

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisKey x, string y) => !(x == y);

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisKey x, byte[] y) => !(x == y);

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisKey x, RedisKey y) => CompositeEquals(x.keyPrefix, x.keyValue, y.keyPrefix, y.keyValue);

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(string x, RedisKey y) => CompositeEquals(null, x, y.keyPrefix, y.keyValue);

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(byte[] x, RedisKey y) => CompositeEquals(null, x, y.keyPrefix, y.keyValue);

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisKey x, string y) => CompositeEquals(x.keyPrefix, x.keyValue, null, y);

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisKey x, byte[] y) => CompositeEquals(x.keyPrefix, x.keyValue, null, y);

        /// <summary>
        /// See Object.Equals
        /// </summary>
        /// <param name="obj">The <see cref="RedisKey"/> to compare to.</param>
        public override bool Equals(object obj)
        {
            if (obj is RedisKey other)
            {
                return CompositeEquals(keyPrefix, keyValue, other.keyPrefix, other.keyValue);
            }
            if (obj is string || obj is byte[])
            {
                return CompositeEquals(keyPrefix, keyValue, null, obj);
            }
            return false;
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        /// <param name="other">The <see cref="RedisKey"/> to compare to.</param>
        public bool Equals(RedisKey other) => CompositeEquals(keyPrefix, keyValue, other.keyPrefix, other.keyValue);

        private static bool CompositeEquals(byte[] keyPrefix0, object keyValue0, byte[] keyPrefix1, object keyValue1)
        {
            if (RedisValue.Equals(keyPrefix0, keyPrefix1))
            {
                if (keyValue0 == keyValue1) return true; // ref equal
                if (keyValue0 == null || keyValue1 == null) return false; // null vs non-null

                if (keyValue0 is string && keyValue1 is string) return ((string)keyValue0) == ((string)keyValue1);
                if (keyValue0 is byte[] && keyValue1 is byte[]) return RedisValue.Equals((byte[])keyValue0, (byte[])keyValue1);
            }

            return RedisValue.Equals(ConcatenateBytes(keyPrefix0, keyValue0, null), ConcatenateBytes(keyPrefix1, keyValue1, null));
        }

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode()
        {
            int chk0 = keyPrefix == null ? 0 : RedisValue.GetHashCode(keyPrefix),
                chk1 = keyValue is string ? keyValue.GetHashCode() : RedisValue.GetHashCode((byte[])keyValue);

            return unchecked((17 * chk0) + chk1);
        }

        /// <summary>
        /// Obtains a string representation of the key
        /// </summary>
        public override string ToString() => ((string)this) ?? "(null)";

        internal RedisValue AsRedisValue() => (byte[])this;

        internal void AssertNotNull()
        {
            if (IsNull) throw new ArgumentException("A null key is not valid in this context");
        }

        /// <summary>
        /// Create a <see cref="RedisKey"/> from a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The string to get a key from.</param>
        public static implicit operator RedisKey(string key)
        {
            if (key == null) return default(RedisKey);
            return new RedisKey(null, key);
        }
        /// <summary>
        /// Create a <see cref="RedisKey"/> from a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The byte array to get a key from.</param>
        public static implicit operator RedisKey(byte[] key)
        {
            if (key == null) return default(RedisKey);
            return new RedisKey(null, key);
        }

        /// <summary>
        /// Obtain the <see cref="RedisKey"/> as a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The key to get a byte array for.</param>
        public static implicit operator byte[] (RedisKey key) => ConcatenateBytes(key.keyPrefix, key.keyValue, null);

        /// <summary>
        /// Obtain the key as a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The key to get a string for.</param>
        public static implicit operator string(RedisKey key)
        {
            byte[] arr;
            if (key.keyPrefix == null)
            {
                if (key.keyValue == null) return null;

                if (key.keyValue is string) return (string)key.keyValue;

                arr = (byte[])key.keyValue;
            }
            else
            {
                arr = (byte[])key;
            }
            if (arr == null) return null;
            try
            {
                return Encoding.UTF8.GetString(arr);
            }
            catch
            {
                return BitConverter.ToString(arr);
            }
        }

        /// <summary>
        /// Concatenate two keys
        /// </summary>
        /// <param name="x">The first <see cref="RedisKey"/> to add.</param>
        /// <param name="y">The second <see cref="RedisKey"/> to add.</param>
        [Obsolete]
        public static RedisKey operator +(RedisKey x, RedisKey y)
        {
            return new RedisKey(ConcatenateBytes(x.keyPrefix, x.keyValue, y.keyPrefix), y.keyValue);
        }

        internal static RedisKey WithPrefix(byte[] prefix, RedisKey value)
        {
            if (prefix == null || prefix.Length == 0) return value;
            if (value.keyPrefix == null) return new RedisKey(prefix, value.keyValue);
            if (value.keyValue == null) return new RedisKey(prefix, value.keyPrefix);

            // two prefixes; darn
            byte[] copy = new byte[prefix.Length + value.keyPrefix.Length];
            Buffer.BlockCopy(prefix, 0, copy, 0, prefix.Length);
            Buffer.BlockCopy(value.keyPrefix, 0, copy, prefix.Length, value.keyPrefix.Length);
            return new RedisKey(copy, value.keyValue);
        }

        internal static byte[] ConcatenateBytes(byte[] a, object b, byte[] c)
        {
            if ((a == null || a.Length == 0) && (c == null || c.Length == 0))
            {
                if (b == null) return null;
                if (b is string) return Encoding.UTF8.GetBytes((string)b);
                return (byte[])b;
            }

            int aLen = a?.Length ?? 0,
                bLen = b == null ? 0 : (b is string
                ? Encoding.UTF8.GetByteCount((string)b)
                : ((byte[])b).Length),
                cLen = c?.Length ?? 0;

            var result = new byte[aLen + bLen + cLen];
            if (aLen != 0) Buffer.BlockCopy(a, 0, result, 0, aLen);
            if (bLen != 0)
            {
                if (b is string s)
                {
                    Encoding.UTF8.GetBytes(s, 0, s.Length, result, aLen);
                }
                else
                {
                    Buffer.BlockCopy((byte[])b, 0, result, aLen, bLen);
                }
            }
            if (cLen != 0) Buffer.BlockCopy(c, 0, result, aLen + bLen, cLen);
            return result;
        }

        /// <summary>
        /// Prepends p to this RedisKey, returning a new RedisKey.
        /// 
        /// Avoids some allocations if possible, repeated Prepend/Appends make
        /// it less possible.
        /// </summary>
        /// <param name="prefix">The prefix to prepend.</param>
        public RedisKey Prepend(RedisKey prefix) => WithPrefix(prefix, this);

        /// <summary>
        /// Appends p to this RedisKey, returning a new RedisKey.
        /// 
        /// Avoids some allocations if possible, repeated Prepend/Appends make
        /// it less possible.
        /// </summary>
        /// <param name="suffix">The suffix to append.</param>
        public RedisKey Append(RedisKey suffix) => WithPrefix(this, suffix);
    }
}