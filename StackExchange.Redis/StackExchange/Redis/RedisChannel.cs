using System;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a pub/sub channel name
    /// </summary>
    public struct RedisChannel : IEquatable<RedisChannel>
    {

        internal static readonly RedisChannel[] EmptyArray = new RedisChannel[0];

        private readonly byte[] value;

        private RedisChannel(byte[] value)
        {
            this.value = value;
        }

        /// <summary>
        /// Indicates whether the channel-name is either null or a zero-length value
        /// </summary>
        public bool IsNullOrEmpty
        {
            get
            {
                return value == null || value.Length == 0;
            }
        }

        internal bool IsNull
        {
            get { return value == null; }
        }

        internal byte[] Value { get { return value; } }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        public static bool operator !=(RedisChannel x, RedisChannel y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        public static bool operator !=(string x, RedisChannel y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        public static bool operator !=(byte[] x, RedisChannel y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        public static bool operator !=(RedisChannel x, string y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        public static bool operator !=(RedisChannel x, byte[] y)
        {
            return !(x == y);
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        public static bool operator ==(RedisChannel x, RedisChannel y)
        {
            return RedisValue.Equals(x.value, y.value);
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        public static bool operator ==(string x, RedisChannel y)
        {
            return RedisValue.Equals(x == null ? null : Encoding.UTF8.GetBytes(x), y.value);
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        public static bool operator ==(byte[] x, RedisChannel y)
        {
            return RedisValue.Equals(x, y.value);
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        public static bool operator ==(RedisChannel x, string y)
        {
            return RedisValue.Equals(x.value, y == null ? null : Encoding.UTF8.GetBytes(y));
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        public static bool operator ==(RedisChannel x, byte[] y)
        {
            return RedisValue.Equals(x.value, y);
        }

        /// <summary>
        /// See Object.Equals
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is RedisChannel)
            {
                return RedisValue.Equals(this.value, ((RedisChannel)obj).value);
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
        /// Indicate whether two channel names are equal
        /// </summary>
        public bool Equals(RedisChannel other)
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
        /// Obtains a string representation of the channel name
        /// </summary>
        public override string ToString()
        {
            return ((string)this) ?? "(null)";
        }

        internal static bool AssertStarts(byte[] value, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != value[i]) return false;
            }
            return true;
        }

        internal RedisChannel Assert()
        {
            if (IsNull) throw new ArgumentException("A null key is not valid in this context");
            return this;
        }

        internal RedisChannel Clone()
        {
            byte[] clone = value == null ? null : (byte[])value.Clone();
            return clone;
        }

        internal bool Contains(byte value)
        {
            return this.value != null && Array.IndexOf(this.value, value) >= 0;
        }
        /// <summary>
        /// Create a channel name from a String
        /// </summary>
        public static implicit operator RedisChannel(string key)
        {
            if (key == null) return default(RedisChannel);
            return new RedisChannel(Encoding.UTF8.GetBytes(key));
        }
        /// <summary>
        /// Create a channel name from a Byte[]
        /// </summary>
        public static implicit operator RedisChannel(byte[] key)
        {
            if (key == null) return default(RedisChannel);
            return new RedisChannel(key);
        }
        /// <summary>
        /// Obtain the channel name as a Byte[]
        /// </summary>
        public static implicit operator byte[] (RedisChannel key)
        {
            return key.value;
        }
        /// <summary>
        /// Obtain the channel name as a String
        /// </summary>
        public static implicit operator string (RedisChannel key)
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
    }
}
