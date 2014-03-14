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
        private readonly byte[] value;
        private RedisKey(byte[] value)
        {
            this.value = value;
        }

        internal bool IsNull
        {
            get { return value == null; }
        }

        internal byte[] Value { get { return value; } }

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        public static bool operator !=(RedisKey x, RedisKey y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        public static bool operator !=(string x, RedisKey y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        public static bool operator !=(byte[] x, RedisKey y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        public static bool operator !=(RedisKey x, string y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two keys are not equal
        /// </summary>
        public static bool operator !=(RedisKey x, byte[] y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public static bool operator ==(RedisKey x, RedisKey y)
        {
            return RedisValue.Equals(x.value, y.value);
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public static bool operator ==(string x, RedisKey y)
        {
            return RedisValue.Equals(x == null ? null : Encoding.UTF8.GetBytes(x), y.value);
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public static bool operator ==(byte[] x, RedisKey y)
        {
            return RedisValue.Equals(x, y.value);
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public static bool operator ==(RedisKey x, string y)
        {
            return RedisValue.Equals(x.value, y == null ? null : Encoding.UTF8.GetBytes(y));
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public static bool operator ==(RedisKey x, byte[] y)
        {
            return RedisValue.Equals(x.value, y);
        }

        /// <summary>
        /// See Object.Equals
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is RedisKey)
            {
                return RedisValue.Equals(this.value, ((RedisKey)obj).value);
            }
            if (obj is string)
            {
                return RedisValue.Equals(this.value, Encoding.UTF8.GetBytes((string)obj));
            }
            if (obj is byte[])
            {
                return RedisValue.Equals(this.value, (byte[])obj);
            }
            return false;
        }

        /// <summary>
        /// Indicate whether two keys are equal
        /// </summary>
        public bool Equals(RedisKey other)
        {
            return RedisValue.Equals(this.value, other.value);
        }

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode()
        {
            return RedisValue.GetHashCode(this.value);
        }

        /// <summary>
        /// Obtains a string representation of the key
        /// </summary>
        public override string ToString()
        {
            return ((string)this) ?? "(null)";
        }

        internal RedisKey Assert()
        {
            if (IsNull) throw new ArgumentException("A null key is not valid in this context");
            return this;
        }

        /// <summary>
        /// Create a key from a String
        /// </summary>
        public static implicit operator RedisKey(string key)
        {
            if (key == null) return default(RedisKey);
            return new RedisKey(Encoding.UTF8.GetBytes(key));
        }
        /// <summary>
        /// Create a key from a Byte[]
        /// </summary>
        public static implicit operator RedisKey(byte[] key)
        {
            if (key == null) return default(RedisKey);
            return new RedisKey(key);
        }
        /// <summary>
        /// Obtain the key as a Byte[]
        /// </summary>
        public static implicit operator byte[](RedisKey key)
        {
            return key.value;
        }
        /// <summary>
        /// Obtain the key as a String
        /// </summary>
        public static implicit operator string(RedisKey key)
        {
            var arr = key.value;
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

        internal RedisValue AsRedisValue()
        {
            return value;
        }
    }
}
