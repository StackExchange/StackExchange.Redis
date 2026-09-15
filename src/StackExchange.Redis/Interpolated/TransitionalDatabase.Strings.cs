using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The string commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One file per command group, named to match the group it adapts - <c>RespSurface.Strings.cs</c>
    /// declares <c>ctx.Strings.Get</c>, and this file is the <see cref="IDatabase"/> spelling of it. The
    /// pairing is the point: the two halves of a command move together, and a group is either done or
    /// visibly not.
    /// </para>
    /// <para>
    /// Implementing a member here removes it from the generated set automatically, because
    /// <c>[AutoDatabase]</c> skips whatever the class declares itself - so nothing has to be deleted from
    /// a list of stubs, and SER352's count falls by itself.
    /// </para>
    /// <para>
    /// Note how little each one is. The body is the whole implementation - no message type, no result
    /// processor, no overload ladder - because the command already exists on the context surface and this
    /// is purely the adapter from the old interface to it.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.SetAsync(key, value, expiry, when, flags));

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => Context.Strings.SetAsync(key, value, expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => StringSet(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => StringSetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags);

        // the older When/TimeSpan? shapes, which is what most existing callers actually bind to; `When`
        // converts implicitly to ValueCondition, and CreateOrKeepTtl is the same helper RedisDatabase
        // uses, so these are pure adapters with no second opinion about semantics

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
            => StringSet(key, value, expiry, keepTtl: false, when);

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSet(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
            => StringSetAsync(key, value, expiry, keepTtl: false, when);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAsync(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
            => StringSet(values, when, Expiration.Default, flags);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
            => StringSetAsync(values, when, Expiration.Default, flags);

        /// <inheritdoc/>
        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.SetAsync(Required(values, nameof(values)), expiry, when, flags));

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => Context.Strings.SetAsync(Required(values, nameof(values)), expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAndGet(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAndGetAsync(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.SetAndGetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Strings.SetAndGetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags).AsTask();

        /// <inheritdoc/>
        /// <remarks>
        /// <c>GETSET</c> has been deprecated since 6.2 in favour of <c>SET ... GET</c>, which is the same
        /// request with the same reply - so callers of the old name get the modern spelling wherever the
        /// server has it, and the old one wherever it might not. See <c>IRespServerFeatures</c>.
        /// </remarks>
        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetSet(key, value, flags));

        /// <inheritdoc cref="StringGetSet"/>
        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetSet(key, value, flags).AsTask();

        /// <inheritdoc/>
        /// <remarks>
        /// <c>GetArray</c>, not <c>Get</c>: this signature promises an array the caller owns, so it takes
        /// the sibling that produces one directly rather than renting a lease and copying out of it.
        /// </remarks>
        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetArray(Required(keys, nameof(keys)), flags));

        /// <inheritdoc cref="StringGet(RedisKey[], CommandFlags)"/>
        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetArray(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetWritableLease(key, flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetWritableLease(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetRangeAsync(key, start, end, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetRangeAsync(key, start, end, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetDeleteAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetDeleteAsync(key, flags).AsTask();

        // GETEX: two overloads and a separate PERSIST concept on the old surface, one Expiration on the
        // new one. CreateOrPersist with !expiry.HasValue is the same mapping RedisDatabase uses - a null
        // TimeSpan means "clear the TTL", which is NOT the same as Expiration.Default's "leave it alone"

        /// <inheritdoc/>
        public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetSetExpiryAsync(key, Expiration.CreateOrPersist(expiry, !expiry.HasValue), flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetSetExpiryAsync(key, Expiration.CreateOrPersist(expiry, !expiry.HasValue), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.GetSetExpiryAsync(key, new Expiration(expiry), flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Context.Strings.GetSetExpiryAsync(key, new Expiration(expiry), flags).AsTask();

        /// <inheritdoc/>
        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.AppendAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Strings.AppendAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.LengthAsync(key, flags).AsTask();

        // SETRANGE replies with an integer; the old signature says RedisValue, so the conversion happens
        // here rather than the group pretending not to know what it read

        /// <inheritdoc/>
        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.SetRangeAsync(key, offset, value, flags));

        /// <inheritdoc/>
        public async Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
            => await Context.Strings.SetRangeAsync(key, offset, value, flags).ConfigureAwait(false);

        /// <inheritdoc/>
        public bool StringDelete(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.DeleteAsync(key, when, flags));

        /// <inheritdoc/>
        public Task<bool> StringDeleteAsync(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
            => Context.Strings.DeleteAsync(key, when, flags).AsTask();

        /// <inheritdoc/>
        public ValueCondition? StringDigest(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.DigestAsync(key, flags));

        /// <inheritdoc/>
        public Task<ValueCondition?> StringDigestAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.DigestAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.IncrementAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => Context.Strings.IncrementAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.IncrementAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => Context.Strings.IncrementAsync(key, value, flags).AsTask();

        // Decrement is a negation, here as it already was in RedisDatabase - the group has no DECRBY,
        // because DECRBY n and INCRBY -n are the same request with the same reply

        /// <inheritdoc/>
        public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => StringIncrement(key, -value, flags);

        /// <inheritdoc/>
        public Task<long> StringDecrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => StringIncrementAsync(key, -value, flags);

        /// <inheritdoc/>
        public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => StringIncrement(key, -value, flags);

        /// <inheritdoc/>
        public Task<double> StringDecrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => StringIncrementAsync(key, -value, flags);

        /// <inheritdoc/>
        public StringIncrementResult<long> StringIncrement(RedisKey key, long value, Expiration expiry, long? lowerBound = null, long? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags));

        /// <inheritdoc/>
        public Task<StringIncrementResult<long>> StringIncrementAsync(RedisKey key, long value, Expiration expiry, long? lowerBound = null, long? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => Context.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags).AsTask();

        /// <inheritdoc/>
        public StringIncrementResult<double> StringIncrement(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags));

        /// <inheritdoc/>
        public Task<StringIncrementResult<double>> StringIncrementAsync(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => Context.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags).AsTask();

        /// <inheritdoc/>
        public string? StringLongestCommonSubsequence(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.LongestCommonSubsequenceAsync(first, second, flags));

        /// <inheritdoc/>
        public Task<string?> StringLongestCommonSubsequenceAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.Strings.LongestCommonSubsequenceAsync(first, second, flags).AsTask();

        /// <inheritdoc/>
        public long StringLongestCommonSubsequenceLength(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.LongestCommonSubsequenceLengthAsync(first, second, flags));

        /// <inheritdoc/>
        public Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.Strings.LongestCommonSubsequenceLengthAsync(first, second, flags).AsTask();

        /// <inheritdoc/>
        public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.LongestCommonSubsequenceWithMatchesAsync(first, second, minLength, flags));

        /// <inheritdoc/>
        public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
            => Context.Strings.LongestCommonSubsequenceWithMatchesAsync(first, second, minLength, flags).AsTask();

        /// <summary>
        /// The old surface throws on a null array where a span would quietly be empty; keep throwing,
        /// because "you passed null" and "you passed nothing" are different mistakes.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private static T[] Required<T>(T[] values, string name)
            => values ?? throw new ArgumentNullException(name);
    }
}
