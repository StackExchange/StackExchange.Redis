using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace StackExchange.Redis
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
    internal partial class RedisDatabase
    {
        /// <inheritdoc/>
        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.SetAsync(key, value, expiry, when, flags));

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.SetAsync(key, value, expiry, when, flags).AsTask(AsyncState, flags);

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
            => Required(values, nameof(values)).Length != 0
                && Wait(Blocking.Strings.SetAsync(values, expiry, when, flags));

        /// <inheritdoc/>
        /// <remarks>
        /// <b>No pairs answers <see langword="false"/> here and <see langword="true"/> on the context
        /// surface, and both are right.</b> The new surface reads the question as "did this succeed?" -
        /// writing nothing vacuously did, which is also how it answers a no-key <c>GetAsync</c>. The
        /// shipped surface reads it as "did the write happen?" and answers no, which is what
        /// <c>GetStringSetMessage</c>'s <c>case 0: return null</c> produces and what <c>MSetTests</c>
        /// asserts across forty cases.
        /// <para>
        /// The adapter is where that is reconciled, because this member IS the shipped contract: a caller
        /// compiled against <see cref="IDatabase"/> gets the answer it has always had. Changing it to
        /// match the new surface would be a silent behaviour break - the code still compiles, and a
        /// guard that used to be false starts being true.
        /// </para>
        /// </remarks>
        public Task<bool> StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => Required(values, nameof(values)).Length == 0
                ? new ValueTask<bool>(false).AsTask(AsyncState, flags)
                : _inner.Strings.SetAsync(values, expiry, when, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAndGet(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAndGetAsync(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.SetAndGetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringSetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.SetAndGetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        /// <remarks>
        /// <c>GETSET</c> has been deprecated since 6.2 in favour of <c>SET ... GET</c>, which is the same
        /// request with the same reply - so callers of the old name get the modern spelling wherever the
        /// server has it, and the old one wherever it might not. See <c>IRespServerFeatures</c>.
        /// </remarks>
        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetSet(key, value, flags));

        /// <inheritdoc cref="StringGetSet"/>
        public Task<RedisValue> StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetSet(key, value, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        /// <remarks>
        /// <c>GetArray</c>, not <c>Get</c>: this signature promises an array the caller owns, so it takes
        /// the sibling that produces one directly rather than renting a lease and copying out of it.
        /// </remarks>
        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetArray(Required(keys, nameof(keys)), flags));

        /// <inheritdoc cref="StringGet(RedisKey[], CommandFlags)"/>
        public Task<RedisValue[]> StringGetAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetArray(Required(keys, nameof(keys)), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetWritableLease(key, flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> StringGetLeaseAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetWritableLease(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetRangeAsync(key, start, end, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetRangeAsync(key, start, end, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetDeleteAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetDeleteAsync(key, flags).AsTask(AsyncState, flags);

        // GETEX: two overloads and a separate PERSIST concept on the old surface, one Expiration on the
        // new one. CreateOrPersist with !expiry.HasValue is the same mapping RedisDatabase uses - a null
        // TimeSpan means "clear the TTL", which is NOT the same as Expiration.Default's "leave it alone"

        /// <inheritdoc/>
        public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetSetExpiryAsync(key, Expiration.CreateOrPersist(expiry, !expiry.HasValue), flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetSetExpiryAsync(key, Expiration.CreateOrPersist(expiry, !expiry.HasValue), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetSetExpiryAsync(key, new Expiration(expiry), flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetSetExpiryAsync(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetSetExpiryAsync(key, new Expiration(expiry), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.AppendAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<long> StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.AppendAsync(key, value, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> StringLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.LengthAsync(key, flags).AsTask(AsyncState, flags);

        // SETRANGE replies with an integer; the old signature says RedisValue, so the conversion happens
        // here rather than the group pretending not to know what it read

        /// <inheritdoc/>
        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.SetRangeAsync(key, offset, value, flags));

        /// <inheritdoc/>
        public async Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
            => await _inner.Strings.SetRangeAsync(key, offset, value, flags).ConfigureAwait(false);

        /// <inheritdoc/>
        public bool StringDelete(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.DeleteAsync(key, when, flags, default, nameof(StringDelete)));

        /// <inheritdoc/>
        public Task<bool> StringDeleteAsync(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.DeleteAsync(key, when, flags, default, nameof(StringDeleteAsync)).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public ValueCondition? StringDigest(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.DigestAsync(key, flags));

        /// <inheritdoc/>
        public Task<ValueCondition?> StringDigestAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.DigestAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.IncrementAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<long> StringIncrementAsync(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.IncrementAsync(key, value, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.IncrementAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<double> StringIncrementAsync(RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.IncrementAsync(key, value, flags).AsTask(AsyncState, flags);

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
            => Wait(Blocking.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags));

        /// <inheritdoc/>
        public Task<StringIncrementResult<long>> StringIncrementAsync(RedisKey key, long value, Expiration expiry, long? lowerBound = null, long? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public StringIncrementResult<double> StringIncrement(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags));

        /// <inheritdoc/>
        public Task<StringIncrementResult<double>> StringIncrementAsync(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOptions options = IncrementOptions.None, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.IncrementAsync(key, value, expiry, lowerBound, upperBound, options, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public string? StringLongestCommonSubsequence(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.LongestCommonSubsequenceAsync(first, second, flags));

        /// <inheritdoc/>
        public Task<string?> StringLongestCommonSubsequenceAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.LongestCommonSubsequenceAsync(first, second, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long StringLongestCommonSubsequenceLength(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.LongestCommonSubsequenceLengthAsync(first, second, flags));

        /// <inheritdoc/>
        public Task<long> StringLongestCommonSubsequenceLengthAsync(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.LongestCommonSubsequenceLengthAsync(first, second, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.LongestCommonSubsequenceWithMatchesAsync(first, second, minLength, flags));

        /// <inheritdoc/>
        public Task<LCSMatchResult> StringLongestCommonSubsequenceWithMatchesAsync(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.LongestCommonSubsequenceWithMatchesAsync(first, second, minLength, flags).AsTask(AsyncState, flags);

        /// <summary>
        /// The old surface throws on a null array where a span would quietly be empty; keep throwing,
        /// because "you passed null" and "you passed nothing" are different mistakes.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private static T[] Required<T>(T[] values, string name)
            => values ?? throw new ArgumentNullException(name);

        /// <inheritdoc/>
        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(GetWithExpiry(Blocking, key, flags));

        /// <inheritdoc/>
        public Task<RedisValueWithExpiry> StringGetWithExpiryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => GetWithExpiry(_inner, key, flags).AsTask(AsyncState, flags);

        /// <summary>One result assembled from two replies: the value, and how long it has left.</summary>
        /// <remarks>
        /// <para>
        /// <b>The shape the old core needed a whole mechanism for.</b> There, this is an
        /// <c>IMultiMessage</c> that expands to <c>[TTL, GET]</c>, stashes the TTL in a result box created
        /// <i>inside the expansion</i>, and has the <c>GET</c>'s processor reassemble the pair - with a
        /// <c>CanWriteWithoutExpansion = false</c> backstop, because writing it without expanding leaves
        /// the expiry half unreadable. A core that can <c>await</c> needs none of that: two sends and an
        /// addition. This is the same collapse as the transaction handshake in section 7r, from the same
        /// cause.
        /// </para>
        /// <para>
        /// <b>Both are issued before either is awaited</b>, so they travel together and in that order on
        /// one connection rather than costing two round trips. They address one key, so they resolve to
        /// one server; and they were never atomic with respect to other clients in the old core either,
        /// so nothing is given up by not bracketing them.
        /// </para>
        /// <para>
        /// <b>Both are awaited even when the first fails</b>, and that is not defensive tidiness. The
        /// operations behind these are pooled and reset when their result is consumed; an abandoned one
        /// never resets, never returns to the pool, and never releases the pooled buffer holding its
        /// request - the same slow leak <c>DiscardReply</c> exists to prevent elsewhere.
        /// </para>
        /// <para>
        /// <c>PTTL</c> rather than a <c>TTL</c>/<c>PTTL</c> choice, because that decision already belongs
        /// to <c>Keys.TimeToLiveAsync</c> and re-deriving it here would be a second place to keep in step.
        /// </para>
        /// </remarks>
        /// <summary>
        /// The refusal, <b>outside</b> the async machinery so that it is thrown rather than returned.
        /// </summary>
        /// <remarks>
        /// <b>An async method does not throw; it hands back a faulted task.</b> That is the wrong shape
        /// here, and not merely stylistically: a transaction operation that does not throw returns a task
        /// which only completes when <c>Execute</c> runs, so a caller who discards it - which is exactly
        /// what a caller checking for the refusal does - is left holding a task nobody will ever complete.
        /// The guard existed and was inside the state machine, so the exception went into a task the
        /// caller had already thrown away, and the operation hung instead of being refused.
        /// </remarks>
        private ValueTask<RedisValueWithExpiry> GetWithExpiry(RespDatabaseContext context, RedisKey key, CommandFlags flags)
        {
            if (this is IBatch)
            {
                throw new NotSupportedException(
                    "This operation is not possible inside a transaction or batch; please issue separate GetString and KeyTimeToLive requests");
            }

            return GetWithExpiryCore(context, key, flags);
        }

        private async ValueTask<RedisValueWithExpiry> GetWithExpiryCore(RespDatabaseContext context, RedisKey key, CommandFlags flags)
        {
            // from a blocking context these are two round trips rather than one pipelined pair: each send
            // completes before the next is issued, which is the price of not depending on the pool
            var pendingExpiry = context.Keys.TimeToLiveAsync(key, flags);
            var pendingValue = context.Strings.GetAsync(key, flags);

            TimeSpan? expiry = null;
            Exception? expiryFault = null;
            try
            {
                expiry = await pendingExpiry.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                expiryFault = ex;
            }

            var value = await pendingValue.ConfigureAwait(false);
            if (expiryFault is not null) throw expiryFault;

            return new RedisValueWithExpiry(value, expiry);
        }

        // ---- locks ---------------------------------------------------------------------------------
        // Two of the four are sugar, and the shipped implementation says so: LockTake IS StringSet with
        // When.NotExists, and LockQuery IS StringGet. So they compose over commands the context surface
        // already has, and deliberately do NOT become methods on it - a lock is a pattern over SET and
        // GET, not a command, and the new surface should not pretend otherwise.
        //
        // LockRelease and LockExtend are the other two, and they were the last members on the fallback:
        // each is a single IFEQ-style command on a new enough server, a TRANSACTION otherwise, and a
        // plain DELETE/EXPIRE where transactions are unavailable at all (twemproxy). All three layers now
        // exist on this surface - the feature probe, MULTI/EXEC with conditions, and the plain command -
        // so they compose rather than needing a second implementation of any of them.
        //
        // Note the fire-and-forget command inside the transaction, which is how the shipped version
        // writes it too: the queued command's own reply is nobody's business, because the ANSWER is
        // whether EXEC ran at all. That is exactly the path that had to be fixed in
        // RespTransactionExecutor, and this is the caller that proves it was worth fixing.

        /// <inheritdoc/>
        public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
            => Wait(TakeLock(Blocking, key, value, expiry, flags));

        /// <inheritdoc/>
        public Task<bool> LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
            => TakeLock(_inner, key, value, expiry, flags).AsTask(AsyncState, flags);

        private ValueTask<bool> TakeLock(RespDatabaseContext context, RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
        {
            // a null token would make the lock unreleasable: SET would delete the key instead of taking it
            if (value.IsNull) throw new ArgumentNullException(nameof(value));
            return context.Strings.SetAsync(key, value, new Expiration(expiry), ValueCondition.NotExists, flags);
        }

        /// <inheritdoc/>
        public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(ReleaseLock(Blocking, key, value, flags));

        /// <inheritdoc/>
        public Task<bool> LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => ReleaseLock(_inner, key, value, flags).AsTask(AsyncState, flags);

        [SuppressMessage("Usage", "SER301:Transaction can be replaced by a single atomic operation", Justification = "Deliberate fallback for servers without DELEX; the atomic form is preferred above.")]
        private ValueTask<bool> ReleaseLock(RespDatabaseContext context, RedisKey key, RedisValue value, CommandFlags flags)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));

            // lock tokens are small, so IFEQ rather than IFDEQ - the same reasoning as the shipped version,
            // and the same command: one round trip, atomic, no transaction needed
            if (context.Raw.TryGetFeatures(RedisCommand.DELEX, in key, flags, out var features)
                && features.DeleteWithValueCheck)
            {
                return context.Strings.DeleteAsync(key, ValueCondition.Equal(value), flags);
            }

            if (TryCreateTransaction() is { } tran)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                _ = tran.KeyDeleteAsync(key, CommandFlags.FireAndForget);
                return new ValueTask<bool>(tran.ExecuteAsync(flags));
            }

            // without transactions (twemproxy etc) the "value" part cannot be enforced at all; deleting
            // unconditionally is what the shipped surface does, and it is a deliberate weakening
            return context.Keys.DeleteAsync(key, flags);
        }

        /// <inheritdoc/>
        public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
            => Wait(ExtendLock(Blocking, key, value, expiry, flags));

        /// <inheritdoc/>
        public Task<bool> LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None)
            => ExtendLock(_inner, key, value, expiry, flags).AsTask(AsyncState, flags);

        [SuppressMessage("Usage", "SER301:Transaction can be replaced by a single atomic operation", Justification = "Deliberate fallback for servers without SET IFEQ; the atomic form is preferred above.")]
        private ValueTask<bool> ExtendLock(RespDatabaseContext context, RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
        {
            if (value.IsNull) throw new ArgumentNullException(nameof(value));

            if (context.Raw.TryGetFeatures(RedisCommand.SET, in key, flags, out var features)
                && features.SetWithValueCheck)
            {
                return context.Strings.SetAsync(key, value, new Expiration(expiry), ValueCondition.Equal(value), flags);
            }

            if (TryCreateTransaction() is { } tran)
            {
                tran.AddCondition(Condition.StringEqual(key, value));
                _ = tran.KeyExpireAsync(key, expiry, CommandFlags.FireAndForget);
                return new ValueTask<bool>(tran.ExecuteAsync(flags));
            }

            return context.Keys.ExpireAsync(key, new Expiration(expiry), flags: flags);
        }

        /// <summary>A transaction, or null where the server has no <c>MULTI</c>/<c>EXEC</c> to build one from.</summary>
        /// <remarks>
        /// Asks the command map rather than assuming, exactly as <c>CreateTransactionIfAvailable</c> does:
        /// a proxy that disables transactions is the whole reason the third fallback exists.
        /// </remarks>
        private ITransaction? TryCreateTransaction()
        {
            var map = _inner.Raw.CommandMap;
            return map.IsAvailable(RedisCommand.MULTI) && map.IsAvailable(RedisCommand.EXEC)
                ? CreateTransaction(AsyncState)
                : null;
        }

        /// <inheritdoc/>
        public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Blocking.Strings.GetAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> LockQueryAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Strings.GetAsync(key, flags).AsTask(AsyncState, flags);
    }
}
