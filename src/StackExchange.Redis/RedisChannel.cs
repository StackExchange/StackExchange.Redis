using System;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a pub/sub channel name
    /// </summary>
    public readonly struct RedisChannel : IEquatable<RedisChannel>
    {
        internal readonly byte[] Value;
        internal readonly bool IsPatternBased;

        /// <summary>
        /// Indicates whether the channel-name is either null or a zero-length value
        /// </summary>
        public bool IsNullOrEmpty => Value == null || Value.Length == 0;

        internal bool IsNull => Value == null;

        /// <summary>
        /// Create a new redis channel from a buffer, explicitly controlling the pattern mode
        /// </summary>
        /// <param name="value">The name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(byte[] value, PatternMode mode) : this(value, DeterminePatternBased(value, mode)) {}

        /// <summary>
        /// Create a new redis channel from a string, explicitly controlling the pattern mode
        /// </summary>
        /// <param name="value">The string name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(string value, PatternMode mode) : this(value == null ? null : Encoding.UTF8.GetBytes(value), mode) {}

        private RedisChannel(byte[] value, bool isPatternBased)
        {
            Value = value;
            IsPatternBased = isPatternBased;
        }

        private static bool DeterminePatternBased(byte[] value, PatternMode mode)
        {
            switch (mode)
            {
                case PatternMode.Auto:
                    return value != null && Array.IndexOf(value, (byte)'*') >= 0;
                case PatternMode.Literal: return false;
                case PatternMode.Pattern: return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator !=(RedisChannel x, RedisChannel y) => !(x == y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator !=(string x, RedisChannel y) => !(x == y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator !=(byte[] x, RedisChannel y) => !(x == y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator !=(RedisChannel x, string y) => !(x == y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are not equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator !=(RedisChannel x, byte[] y) => !(x == y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator ==(RedisChannel x, RedisChannel y) =>
            x.IsPatternBased == y.IsPatternBased && RedisValue.Equals(x.Value, y.Value);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator ==(string x, RedisChannel y) =>
            RedisValue.Equals(x == null ? null : Encoding.UTF8.GetBytes(x), y.Value);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator ==(byte[] x, RedisChannel y) => RedisValue.Equals(x, y.Value);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator ==(RedisChannel x, string y) =>
            RedisValue.Equals(x.Value, y == null ? null : Encoding.UTF8.GetBytes(y));
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static bool operator ==(RedisChannel x, byte[] y) => RedisValue.Equals(x.Value, y);
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// See Object.Equals
        /// </summary>
        /// <param name="obj">The <see cref="RedisChannel"/> to compare to.</param>
        public override bool Equals(object obj)
        {
            if (obj is RedisChannel rcObj)
            {
                return RedisValue.Equals(Value, (rcObj).Value);
            }
            if (obj is string sObj)
            {
                return RedisValue.Equals(Value, Encoding.UTF8.GetBytes(sObj));
            }
            if (obj is byte[] bObj)
            {
                return RedisValue.Equals(Value, bObj);
            }
            return false;
        }

        /// <summary>
        /// Indicate whether two channel names are equal
        /// </summary>
        /// <param name="other">The <see cref="RedisChannel"/> to compare to.</param>
        public bool Equals(RedisChannel other) => IsPatternBased == other.IsPatternBased && RedisValue.Equals(Value, other.Value);

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode() => RedisValue.GetHashCode(Value) + (IsPatternBased ? 1 : 0);

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

        internal void AssertNotNull()
        {
            if (IsNull) throw new ArgumentException("A null key is not valid in this context");
        }

        internal RedisChannel Clone() => (byte[])Value?.Clone();

        /// <summary>
        /// The matching pattern for this channel
        /// </summary>
        public enum PatternMode
        {
            /// <summary>
            /// Will be treated as a pattern if it includes *
            /// </summary>
            Auto = 0,
            /// <summary>
            /// Never a pattern
            /// </summary>
            Literal = 1,
            /// <summary>
            /// Always a pattern
            /// </summary>
            Pattern = 2
        }

        /// <summary>
        /// Create a channel name from a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The string to get a channel from.</param>
        public static implicit operator RedisChannel(string key)
        {
            if (key == null) return default(RedisChannel);
            return new RedisChannel(Encoding.UTF8.GetBytes(key), PatternMode.Auto);
        }

        /// <summary>
        /// Create a channel name from a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The byte array to get a channel from.</param>
        public static implicit operator RedisChannel(byte[] key)
        {
            if (key == null) return default(RedisChannel);
            return new RedisChannel(key, PatternMode.Auto);
        }

        /// <summary>
        /// Obtain the channel name as a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The channel to get a byte[] from.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static implicit operator byte[] (RedisChannel key) => key.Value;
#pragma warning restore RCS1231 // Make parameter ref read-only.

        /// <summary>
        /// Obtain the channel name as a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The channel to get a string from.</param>
#pragma warning disable RCS1231 // Make parameter ref read-only. - public API
        public static implicit operator string (RedisChannel key)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            var arr = key.Value;
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
