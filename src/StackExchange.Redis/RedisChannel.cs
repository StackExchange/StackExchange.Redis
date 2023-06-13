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
        internal readonly bool _isPatternBased;

        /// <summary>
        /// Indicates whether the channel-name is either null or a zero-length value.
        /// </summary>
        public bool IsNullOrEmpty => Value == null || Value.Length == 0;

        /// <summary>
        /// Indicates whether this channel represents a wildcard pattern (see <c>PSUBSCRIBE</c>)
        /// </summary>
        public bool IsPattern => _isPatternBased;

        internal bool IsNull => Value == null;


        /// <summary>
        /// Indicates whether channels should use <see cref="PatternMode.Auto"/> when no <see cref="PatternMode"/>
        /// is specified; this is enabled by default, but can be disabled to avoid unexpected wildcard scenarios.
        /// </summary>
        public static bool UseImplicitAutoPattern
        {
            get => s_DefaultPatternMode == PatternMode.Auto;
            set => s_DefaultPatternMode = value ? PatternMode.Auto : PatternMode.Literal;
        }
        private static PatternMode s_DefaultPatternMode = PatternMode.Auto;

        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that does not act as a wildcard subscription
        /// </summary>
        public static RedisChannel Literal(string value) => new RedisChannel(value, PatternMode.Literal);
        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that does not act as a wildcard subscription
        /// </summary>
        public static RedisChannel Literal(byte[] value) => new RedisChannel(value, PatternMode.Literal);
        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that acts as a wildcard subscription
        /// </summary>
        public static RedisChannel Pattern(string value) => new RedisChannel(value, PatternMode.Pattern);
        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that acts as a wildcard subscription
        /// </summary>
        public static RedisChannel Pattern(byte[] value) => new RedisChannel(value, PatternMode.Pattern);

        /// <summary>
        /// Create a new redis channel from a buffer, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(byte[]? value, PatternMode mode) : this(value, DeterminePatternBased(value, mode)) { }

        /// <summary>
        /// Create a new redis channel from a string, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The string name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(string value, PatternMode mode) : this(value == null ? null : Encoding.UTF8.GetBytes(value), mode) { }

        private RedisChannel(byte[]? value, bool isPatternBased)
        {
            Value = value;
            _isPatternBased = isPatternBased;
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
            x._isPatternBased == y._isPatternBased && RedisValue.Equals(x.Value, y.Value);

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
        public bool Equals(RedisChannel other) => _isPatternBased == other._isPatternBased && RedisValue.Equals(Value, other.Value);

        /// <inheritdoc/>
        public override int GetHashCode() => RedisValue.GetHashCode(Value) + (_isPatternBased ? 1 : 0);

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

        internal RedisChannel Clone()
        {
            if (Value is null || Value.Length == 0)
            {
                // no need to duplicate anything
                return this;
            }
            var copy = (byte[])Value.Clone(); // defensive array copy
            return new RedisChannel(copy, _isPatternBased);
        }

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
        [Obsolete("It is preferable to explicitly specify a " + nameof(PatternMode) + ", or use the " + nameof(Literal) + "/" + nameof(Pattern) + " methods", error: false)]
        public static implicit operator RedisChannel(string key)
        {
            if (key == null) return default;
            return new RedisChannel(Encoding.UTF8.GetBytes(key), s_DefaultPatternMode);
        }

        /// <summary>
        /// Create a channel name from a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The byte array to get a channel from.</param>
        [Obsolete("It is preferable to explicitly specify a " + nameof(PatternMode) + ", or use the " + nameof(Literal) + "/" + nameof(Pattern) + " methods", error: false)]
        public static implicit operator RedisChannel(byte[]? key)
        {
            if (key == null) return default;
            return new RedisChannel(key, s_DefaultPatternMode);
        }

        /// <summary>
        /// Obtain the channel name as a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="key">The channel to get a byte[] from.</param>
        public static implicit operator byte[]?(RedisChannel key) => key.Value;

        /// <summary>
        /// Obtain the channel name as a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The channel to get a string from.</param>
        public static implicit operator string?(RedisChannel key)
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

#if DEBUG
        // these exist *purely* to ensure that we never add them later *without*
        // giving due consideration to the default pattern mode (UseImplicitAutoPattern)
        // (since we don't ship them, we don't need them in release)
        [Obsolete("Watch for " + nameof(UseImplicitAutoPattern), error: true)]
        private RedisChannel(string value) => throw new NotSupportedException();
        [Obsolete("Watch for " + nameof(UseImplicitAutoPattern), error: true)]
        private RedisChannel(byte[]? value) => throw new NotSupportedException();
#endif
    }
}
