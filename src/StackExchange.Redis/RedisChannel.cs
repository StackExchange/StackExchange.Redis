using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a pub/sub channel name.
    /// </summary>
    public readonly struct RedisChannel : IEquatable<RedisChannel>
    {
        internal readonly byte[]? Value;

        internal ReadOnlySpan<byte> Span => Value is null ? default : Value.AsSpan();

        internal readonly RedisChannelOptions Options;

        [Flags]
        internal enum RedisChannelOptions
        {
            None = 0,
            Pattern = 1 << 0,
            Sharded = 1 << 1,
            KeyRouted = 1 << 2,
            MultiNode = 1 << 3,
        }

        // we don't consider Routed for equality - it's an implementation detail, not a fundamental feature
        private const RedisChannelOptions EqualityMask = ~(RedisChannelOptions.KeyRouted | RedisChannelOptions.MultiNode);

        internal RedisCommand PublishCommand => IsSharded ? RedisCommand.SPUBLISH : RedisCommand.PUBLISH;

        /// <summary>
        /// Should we use cluster routing for this channel? This applies *either* to sharded (SPUBLISH) scenarios,
        /// or to scenarios using <see cref="RedisChannel.WithKeyRouting" />.
        /// </summary>
        internal bool IsKeyRouted => (Options & RedisChannelOptions.KeyRouted) != 0;

        internal bool IsMultiNode => (Options & RedisChannelOptions.MultiNode) != 0;

        /// <summary>
        /// Indicates whether the channel-name is either null or a zero-length value.
        /// </summary>
        public bool IsNullOrEmpty => Value == null || Value.Length == 0;

        /// <summary>
        /// Indicates whether this channel represents a wildcard pattern (see <c>PSUBSCRIBE</c>).
        /// </summary>
        public bool IsPattern => (Options & RedisChannelOptions.Pattern) != 0;

        /// <summary>
        /// Indicates whether this channel represents a shard channel (see <c>SSUBSCRIBE</c>).
        /// </summary>
        public bool IsSharded => (Options & RedisChannelOptions.Sharded) != 0;

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
        /// Creates a new <see cref="RedisChannel"/> that does not act as a wildcard subscription. In cluster
        /// environments, this channel will be freely routed to any applicable server - different client nodes
        /// will generally connect to different servers; this is suitable for distributing pub/sub in scenarios with
        /// very few channels. In non-cluster environments, routing is not a consideration.
        /// </summary>
        public static RedisChannel Literal(string value) => new(value, RedisChannelOptions.None);

        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that does not act as a wildcard subscription. In cluster
        /// environments, this channel will be freely routed to any applicable server - different client nodes
        /// will generally connect to different servers; this is suitable for distributing pub/sub in scenarios with
        /// very few channels. In non-cluster environments, routing is not a consideration.
        /// </summary>
        public static RedisChannel Literal(byte[] value) => new(value, RedisChannelOptions.None);

        /// <summary>
        /// In cluster environments, this channel will be routed using similar rules to <see cref="RedisKey"/>, which is suitable
        /// for distributing pub/sub in scenarios with lots of channels. In non-cluster environments, routing is not
        /// a consideration.
        /// </summary>
        /// <remarks>Note that channels from <c>Sharded</c> are always routed.</remarks>
        public RedisChannel WithKeyRouting() => new(Value, Options | RedisChannelOptions.KeyRouted);

        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that acts as a wildcard subscription. In cluster
        /// environments, this channel will be freely routed to any applicable server - different client nodes
        /// will generally connect to different servers; this is suitable for distributing pub/sub in scenarios with
        /// very few channels. In non-cluster environments, routing is not a consideration.
        /// </summary>
        public static RedisChannel Pattern(string value) => new(value, RedisChannelOptions.Pattern);

        /// <summary>
        /// Creates a new <see cref="RedisChannel"/> that acts as a wildcard subscription. In cluster
        /// environments, this channel will be freely routed to any applicable server - different client nodes
        /// will generally connect to different servers; this is suitable for distributing pub/sub in scenarios with
        /// very few channels. In non-cluster environments, routing is not a consideration.
        /// </summary>
        public static RedisChannel Pattern(byte[] value) => new(value, RedisChannelOptions.Pattern);

        /// <summary>
        /// Create a new redis channel from a buffer, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        public RedisChannel(byte[]? value, PatternMode mode) : this(value, DeterminePatternBased(value, mode) ? RedisChannelOptions.Pattern : RedisChannelOptions.None)
        {
        }

        /// <summary>
        /// Create a new redis channel from a string, explicitly controlling the pattern mode.
        /// </summary>
        /// <param name="value">The string name of the channel to create.</param>
        /// <param name="mode">The mode for name matching.</param>
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        public RedisChannel(string value, PatternMode mode) : this(value is null ? null : Encoding.UTF8.GetBytes(value), mode)
        {
        }

        /// <summary>
        /// Create a new redis channel from a buffer, representing a sharded channel. In cluster
        /// environments, this channel will be routed using similar rules to <see cref="RedisKey"/>, which is suitable
        /// for distributing pub/sub in scenarios with lots of channels. In non-cluster environments, routing is not
        /// a consideration.
        /// </summary>
        /// <param name="value">The name of the channel to create.</param>
        /// <remarks>Note that sharded subscriptions are completely separate to regular subscriptions; subscriptions
        /// using sharded channels must also be published with sharded channels (and vice versa).</remarks>
        public static RedisChannel Sharded(byte[]? value) => new(value, RedisChannelOptions.Sharded | RedisChannelOptions.KeyRouted);

        /// <summary>
        /// Create a new redis channel from a string, representing a sharded channel. In cluster
        /// environments, this channel will be routed using similar rules to <see cref="RedisKey"/>, which is suitable
        /// for distributing pub/sub in scenarios with lots of channels. In non-cluster environments, routing is not
        /// a consideration.
        /// </summary>
        /// <param name="value">The string name of the channel to create.</param>
        /// <remarks>Note that sharded subscriptions are completely separate to regular subscriptions; subscriptions
        /// using sharded channels must also be published with sharded channels (and vice versa).</remarks>
        public static RedisChannel Sharded(string value) => new(value, RedisChannelOptions.Sharded | RedisChannelOptions.KeyRouted);

        /// <summary>
        /// Create a key-notification channel for a single key in a single database.
        /// </summary>
        public static RedisChannel KeySpace(in RedisKey key, int database)
            => BuildKeySpace(key, database, RedisChannelOptions.None);

        /// <summary>
        /// Create a key-notification channel for a pattern, optionally in a specified database.
        /// </summary>
        public static RedisChannel KeySpacePattern(in RedisKey pattern, int? database = null)
            => BuildKeySpace(pattern, database, RedisChannelOptions.Pattern | RedisChannelOptions.MultiNode);

        private const int DatabaseScratchBufferSize = 16; // largest non-negative int32 is 10 digits

        private static ReadOnlySpan<byte> AppendDatabase(Span<byte> target, int? database, RedisChannelOptions options)
        {
            if (database is null)
            {
                if ((options & RedisChannelOptions.Pattern) == 0) throw new ArgumentNullException(nameof(database));
                return "*"u8; // don't worry about the inbound scratch buffer, this is fine
            }
            else
            {
                var db32 = database.GetValueOrDefault();
                if (db32 == 0) return "0"u8; // so common, we might as well special case
                if (db32 < 0) throw new ArgumentOutOfRangeException(nameof(database));
                return target.Slice(0, Format.FormatInt32(db32, target));
            }
        }

        /// <summary>
        /// Create a key-notification channel for a pattern, optionally in a specified database.
        /// </summary>
        public static RedisChannel KeyEvent(KeyNotificationType type, int? database = null)
        {
            RedisChannelOptions options = RedisChannelOptions.MultiNode;
            if (database is null) options |= RedisChannelOptions.Pattern;
            var db = AppendDatabase(stackalloc byte[DatabaseScratchBufferSize], database, options);
            var typeBytes = KeyNotificationTypeFastHash.GetRawBytes(type);

            // __keyevent@{db}__:{type}
            var arr = new byte[14 + db.Length + typeBytes.Length];

            Span<byte> target = AppendAndAdvance(arr.AsSpan(), "__keyevent@"u8);
            target = AppendAndAdvance(target, db);
            target = AppendAndAdvance(target, "__:"u8);
            target = AppendAndAdvance(target, typeBytes);
            Debug.Assert(target.IsEmpty); // should have calculated length correctly

            return new RedisChannel(arr, options);
        }

        private static Span<byte> AppendAndAdvance(Span<byte> target, scoped ReadOnlySpan<byte> value)
        {
            value.CopyTo(target);
            return target.Slice(value.Length);
        }

        private static RedisChannel BuildKeySpace(in RedisKey key, int? database, RedisChannelOptions options)
        {
            int keyLen;
            if (key.IsNull)
            {
                if ((options & RedisChannelOptions.Pattern) == 0) throw new ArgumentNullException(nameof(key));
                keyLen = 1;
            }
            else
            {
                keyLen = key.TotalLength();
                if (keyLen == 0) throw new ArgumentOutOfRangeException(nameof(key));
            }

            var db = AppendDatabase(stackalloc byte[DatabaseScratchBufferSize], database, options);

            // __keyspace@{db}__:{key}
            var arr = new byte[14 + db.Length + keyLen];

            Span<byte> target = AppendAndAdvance(arr.AsSpan(), "__keyspace@"u8);
            target = AppendAndAdvance(target, db);
            target = AppendAndAdvance(target, "__:"u8);
            Debug.Assert(keyLen == target.Length); // should have exactly "len" bytes remaining
            if (key.IsNull)
            {
                target[0] = (byte)'*';
                target = target.Slice(1);
            }
            else
            {
                target = target.Slice(key.CopyTo(target));
            }
            Debug.Assert(target.IsEmpty); // should have calculated length correctly
            return new RedisChannel(arr, options);
        }

        internal RedisChannel(byte[]? value, RedisChannelOptions options)
        {
            Value = value;
            Options = options;
        }

        internal RedisChannel(string? value, RedisChannelOptions options)
        {
            Value = value is null ? null : Encoding.UTF8.GetBytes(value);
            Options = options;
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
            (x.Options & EqualityMask) == (y.Options & EqualityMask)
            && RedisValue.Equals(x.Value, y.Value);

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="x">The first <see cref="RedisChannel"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisChannel"/> to compare.</param>
        public static bool operator ==(string x, RedisChannel y) =>
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            RedisValue.Equals(x is null ? null : Encoding.UTF8.GetBytes(x), y.Value);

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
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            RedisValue.Equals(x.Value, y is null ? null : Encoding.UTF8.GetBytes(y));

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
            _ => false,
        };

        /// <summary>
        /// Indicate whether two channel names are equal.
        /// </summary>
        /// <param name="other">The <see cref="RedisChannel"/> to compare to.</param>
        public bool Equals(RedisChannel other) => (Options & EqualityMask) == (other.Options & EqualityMask)
                                                  && RedisValue.Equals(Value, other.Value);

        /// <inheritdoc/>
        public override int GetHashCode() => RedisValue.GetHashCode(Value) ^ (int)(Options & EqualityMask);

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
            return new RedisChannel(copy, Options);
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
            Pattern = 2,
        }

        /// <summary>
        /// Create a channel name from a <see cref="string"/>.
        /// </summary>
        /// <param name="key">The string to get a channel from.</param>
        [Obsolete("It is preferable to explicitly specify a " + nameof(PatternMode) + ", or use the " + nameof(Literal) + "/" + nameof(Pattern) + " methods", error: false)]
        public static implicit operator RedisChannel(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (key is null) return default;
            return new RedisChannel(Encoding.UTF8.GetBytes(key), s_DefaultPatternMode);
        }

        /// <summary>
        /// Create a channel name from a <c>byte[]</c>.
        /// </summary>
        /// <param name="key">The byte array to get a channel from.</param>
        [Obsolete("It is preferable to explicitly specify a " + nameof(PatternMode) + ", or use the " + nameof(Literal) + "/" + nameof(Pattern) + " methods", error: false)]
        public static implicit operator RedisChannel(byte[]? key)
            => key is null ? default : new RedisChannel(key, s_DefaultPatternMode);

        /// <summary>
        /// Obtain the channel name as a <c>byte[]</c>.
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
            if (arr is null)
            {
                return null;
            }
            try
            {
                return Encoding.UTF8.GetString(arr);
            }
            catch (Exception e) when // Only catch exception throwed by Encoding.UTF8.GetString
                (e is DecoderFallbackException or ArgumentException or ArgumentNullException)
            {
                    return BitConverter.ToString(arr);
            }
        }

#if DEBUG
        // these exist *purely* to ensure that we never add them later *without*
        // giving due consideration to the default pattern mode (UseImplicitAutoPattern)
        // (since we don't ship them, we don't need them in release)
        [Obsolete("Watch for " + nameof(UseImplicitAutoPattern), error: true)]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        private RedisChannel(string value) => throw new NotSupportedException();
        [Obsolete("Watch for " + nameof(UseImplicitAutoPattern), error: true)]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        private RedisChannel(byte[]? value) => throw new NotSupportedException();
#endif
    }
}
