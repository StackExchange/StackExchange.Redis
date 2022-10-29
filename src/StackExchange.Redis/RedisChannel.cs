using System;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a pub/sub channel name.
    /// </summary>
    public readonly struct RedisChannel : IEquatable<RedisChannel>
    {
        internal readonly byte[]? Value;
        internal readonly bool IsPatternBased;

        /// <summary>
        /// Indicates whether the channel-name is either null or a zero-length value.
        /// </summary>
        public bool IsNullOrEmpty => Value == null || Value.Length == 0;

        internal bool IsNull => Value == null;

        /// <summary>
        /// Create a new redis channel from a buffer, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(byte[]? value, PatternMode mode) : this(value, DeterminePatternBased(value, mode)) {}

        /// <summary>
        /// Create a new redis channel from a string, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The string name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(string value, PatternMode mode) : this(value == null ? null : Encoding.UTF8.GetBytes(value), mode) {}

        private RedisChannel(byte[]? value, bool isPatternBased)
        {
            Value = value;
            IsPatternBased = isPatternBased;
        }

        private static bool DeterminePatternBased(byte[]? value, PatternMode mode) => mode switch
        {
            PatternMode.Auto => value != null && Array.IndexOf(value, (byte)'*') >= 0,
            PatternMode.Literal => false,
            PatternMode.Pattern => true,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        /// <summary>
        /// Indicate whether two channel names are not equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisChannel x, RedisChannel y) => !(x == y);

        /// <summary>
        /// Indicate whether two channel names are not equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(string x, RedisChannel y) => !(x == y);

        /// <summary>
        /// Indicate whether two channel names are not equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(byte[] x, RedisChannel y) => !(x == y);

        /// <summary>
        /// Indicate whether two channel names are not equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisChannel x, string y) => !(x == y);

        /// <summary>
        /// Indicate whether two channel names are not equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator !=(RedisChannel x, byte[] y) => !(x == y);

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisChannel x, RedisChannel y) =>
            x.IsPatternBased == y.IsPatternBased && RedisValue.Equals(x.Value, y.Value);

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(string x, RedisChannel y) =>
            RedisValue.Equals(x == null ? null : Encoding.UTF8.GetBytes(x), y.Value);

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(byte[] x, RedisChannel y) => RedisValue.Equals(x, y.Value);

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisChannel x, string y) =>
            RedisValue.Equals(x.Value, y == null ? null : Encoding.UTF8.GetBytes(y));

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(RedisChannel x, byte[] y) => RedisValue.Equals(x.Value, y);

        /// <summary>
        /// See <see cref="object.Equals(object)"/>.
        /// </summary>
        /// <param name="obj">The <see cref="RedisChannel"/> to compare to.</param>
        public override bool Equals(object? obj) => obj switch
        {
            RedisChannel rcObj => RedisValue.Equals(Value, rcObj.Value),
            string sObj => RedisValue.Equals(Value, Encoding.UTF8.GetBytes(sObj)),
            byte[] bObj => RedisValue.Equals(Value, bObj),
            _ => false
        };

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="other">The <see cref="RedisChannel"/> to compare to.</param>
        public bool Equals(RedisChannel other) => IsPatternBased == other.IsPatternBased && RedisValue.Equals(Value, other.Value);

        /// <inheritdoc/>
        public override int GetHashCode() => RedisValue.GetHashCode(Value) + (IsPatternBased ? 1 : 0);

        /// <summary>
        /// Obtains a string representation of the channel name.
        /// </summary>
        public override string ToString() => ((string?)this) ?? "(null)";

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

        internal RedisChannel Clone() => (byte[]?)Value?.Clone() ?? default;

        /// <summary>
        /// The matching pattern for this channel.
        /// </summary>
        public enum PatternMode
        {
            /// <summary>
            /// Will be treated as a pattern if it includes *.
            /// </summary>
            Auto = 0,
            /// <summary>
            /// Never a pattern.
            /// </summary>
            Literal = 1,
            /// <summary>
            /// Always a pattern.
            /// </summary>
            Pattern = 2
        }

        /// <summary>
        /// Create a channel name from a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The string to get a channel from.</param>
        public static implicit operator RedisChannel(string key)
        {
            if (key == null) return default;
            return new RedisChannel(Encoding.UTF8.GetBytes(key), PatternMode.Auto);
        }

        /// <summary>
        /// Create a channel name from a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The byte array to get a channel from.</param>
        public static implicit operator RedisChannel(byte[]? key)
        {
            if (key == null) return default;
            return new RedisChannel(key, PatternMode.Auto);
        }

        /// <summary>
        /// Obtain the channel name as a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The channel to get a byte[] from.</param>
        public static implicit operator byte[]? (RedisChannel key) => key.Value;

        /// <summary>
        /// Obtain the channel name as a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The channel to get a string from.</param>
        public static implicit operator string? (RedisChannel key)
        {
            var arr = key.Value;
            if (arr == null)
            {
                return null;
            }
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
