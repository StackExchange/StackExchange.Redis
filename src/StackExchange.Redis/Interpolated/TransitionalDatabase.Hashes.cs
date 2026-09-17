using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The hash commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clearest illustration so far of what the new surface costs the old one: nothing. Six
    /// <c>HashFieldGetAndSetExpiry</c> overloads, six <c>HashFieldSetAndSetExpiry</c> overloads and four
    /// <c>HashFieldExpire</c> overloads all land on one group method each, because
    /// <see cref="Expiration"/> already says everything their <c>TimeSpan?</c>/<c>DateTime</c>/
    /// <c>persist</c>/<c>keepTtl</c> parameters were spelling out between them.
    /// </para>
    /// <para>
    /// <c>HashScan</c> stays in <c>TransitionalDatabase.Scans.cs</c>. <c>HashImport</c> stays with the
    /// generated members, and the reason is worth stating precisely because the obvious version of it is
    /// wrong: see <see cref="HashImport"/>.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetAsync(key, hashField, flags));

        /// <inheritdoc/>
        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetAsync(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetArray(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetArray(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetWritableLease(key, hashField, flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetWritableLease(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetAllArray(key, flags));

        /// <inheritdoc/>
        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetAllArray(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.KeysArray(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashKeysAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.KeysArray(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.ValuesArray(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashValuesAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.ValuesArray(key, flags).AsTask();

        /// <inheritdoc/>
        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> HashLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.StringLengthAsync(key, hashField, flags));

        /// <inheritdoc/>
        public Task<long> HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.StringLengthAsync(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.ExistsAsync(key, hashField, flags));

        /// <inheritdoc/>
        public Task<bool> HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.ExistsAsync(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.RandomFieldAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> HashRandomFieldAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.RandomFieldAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.RandomFieldsArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashRandomFieldsAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.RandomFieldsArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.RandomFieldsWithValuesArray(key, count, flags));

        /// <inheritdoc/>
        public Task<HashEntry[]> HashRandomFieldsWithValuesAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.RandomFieldsWithValuesArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.SetAsync(key, hashField, value, when, flags));

        /// <inheritdoc/>
        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.SetAsync(key, hashField, value, when, flags).AsTask();

        /// <inheritdoc/>
        public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.SetAsync(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.SetAsync(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.DeleteAsync(key, hashField, flags));

        /// <inheritdoc/>
        public Task<bool> HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.DeleteAsync(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.DeleteAsync(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<long> HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.DeleteAsync(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.IncrementAsync(key, hashField, value, flags));

        /// <inheritdoc/>
        public Task<long> HashIncrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.IncrementAsync(key, hashField, value, flags).AsTask();

        /// <inheritdoc/>
        public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.IncrementAsync(key, hashField, value, flags));

        /// <inheritdoc/>
        public Task<double> HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.IncrementAsync(key, hashField, value, flags).AsTask();

        // Decrement is a negation, here as it already was in RedisDatabase; the server has no HDECRBY

        /// <inheritdoc/>
        public long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
            => HashIncrement(key, hashField, -value, flags);

        /// <inheritdoc/>
        public Task<long> HashDecrementAsync(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None)
            => HashIncrementAsync(key, hashField, -value, flags);

        /// <inheritdoc/>
        public double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
            => HashIncrement(key, hashField, -value, flags);

        /// <inheritdoc/>
        public Task<double> HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None)
            => HashIncrementAsync(key, hashField, -value, flags);

        // ---- per-field lifetimes -----------------------------------------------------------------------
        // The TimeSpan/DateTime pairs collapse onto one group method: an Expiration built from a TimeSpan
        // is relative and one built from a DateTime is absolute, which is precisely the distinction the two
        // overloads existed to make.

        /// <inheritdoc/>
        public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.ExpireArray(key, Required(hashFields, nameof(hashFields)), expiry, when, flags));

        /// <inheritdoc/>
        public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.ExpireArray(key, Required(hashFields, nameof(hashFields)), expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public ExpireResult[] HashFieldExpire(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.ExpireArray(key, Required(hashFields, nameof(hashFields)), expiry, when, flags));

        /// <inheritdoc/>
        public Task<ExpireResult[]> HashFieldExpireAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.ExpireArray(key, Required(hashFields, nameof(hashFields)), expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public PersistResult[] HashFieldPersist(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.PersistArray(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<PersistResult[]> HashFieldPersistAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.PersistArray(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public long[] HashFieldGetTimeToLive(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetTimeToLiveArray(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<long[]> HashFieldGetTimeToLiveAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetTimeToLiveArray(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public long[] HashFieldGetExpireDateTime(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetExpireDateTimeArray(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<long[]> HashFieldGetExpireDateTimeAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetExpireDateTimeArray(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        // ---- read/write combinations -------------------------------------------------------------------

        /// <inheritdoc/>
        public RedisValue HashFieldGetAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetDeleteAsync(key, hashField, flags));

        /// <inheritdoc/>
        public Task<RedisValue> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetDeleteAsync(key, hashField, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashFieldGetAndDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetDeleteArray(key, Required(hashFields, nameof(hashFields)), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashFieldGetAndDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetDeleteArray(key, Required(hashFields, nameof(hashFields)), flags).AsTask();

        /// <inheritdoc/>
        public Lease<byte>? HashFieldGetLeaseAndDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetWritableLeaseDelete(key, hashField, flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> HashFieldGetLeaseAndDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetWritableLeaseDelete(key, hashField, flags).AsTask();

        // HGETEX: a null TimeSpan means "clear the TTL", which is NOT Expiration.Default's "leave it
        // alone" - the same mapping RedisDatabase uses, and the same one StringGetSetExpiry needs

        /// <inheritdoc/>
        public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetSetExpiryAsync(key, hashField, Expiration.CreateOrPersist(expiry, persist), flags));

        /// <inheritdoc/>
        public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetSetExpiryAsync(key, hashField, Expiration.CreateOrPersist(expiry, persist), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue HashFieldGetAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetSetExpiryAsync(key, hashField, new Expiration(expiry), flags));

        /// <inheritdoc/>
        public Task<RedisValue> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetSetExpiryAsync(key, hashField, new Expiration(expiry), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetSetExpiryArray(key, Required(hashFields, nameof(hashFields)), Expiration.CreateOrPersist(expiry, persist), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetSetExpiryArray(key, Required(hashFields, nameof(hashFields)), Expiration.CreateOrPersist(expiry, persist), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] HashFieldGetAndSetExpiry(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetSetExpiryArray(key, Required(hashFields, nameof(hashFields)), new Expiration(expiry), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> HashFieldGetAndSetExpiryAsync(RedisKey key, RedisValue[] hashFields, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetSetExpiryArray(key, Required(hashFields, nameof(hashFields)), new Expiration(expiry), flags).AsTask();

        /// <inheritdoc/>
        public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetWritableLeaseSetExpiry(key, hashField, Expiration.CreateOrPersist(expiry, persist), flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, TimeSpan? expiry = null, bool persist = false, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetWritableLeaseSetExpiry(key, hashField, Expiration.CreateOrPersist(expiry, persist), flags).AsTask();

        /// <inheritdoc/>
        public Lease<byte>? HashFieldGetLeaseAndSetExpiry(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Hashes.GetWritableLeaseSetExpiry(key, hashField, new Expiration(expiry), flags));

        /// <inheritdoc/>
        public Task<Lease<byte>?> HashFieldGetLeaseAndSetExpiryAsync(RedisKey key, RedisValue hashField, DateTime expiry, CommandFlags flags = CommandFlags.None)
            => Context.Hashes.GetWritableLeaseSetExpiry(key, hashField, new Expiration(expiry), flags).AsTask();

        // HSETEX replies with whether the write happened; the old signature says RedisValue, so the
        // conversion happens here rather than the group pretending not to know what it read

        /// <inheritdoc/>
        public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue field, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(Wait(Context.Hashes.SetWithExpiryAsync(key, field, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags)));

        /// <inheritdoc/>
        public async Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(await Context.Hashes.SetWithExpiryAsync(key, field, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags).ConfigureAwait(false));

        /// <inheritdoc/>
        public RedisValue HashFieldSetAndSetExpiry(RedisKey key, RedisValue field, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(Wait(Context.Hashes.SetWithExpiryAsync(key, field, value, new Expiration(expiry), when, flags)));

        /// <inheritdoc/>
        public async Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, RedisValue field, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(await Context.Hashes.SetWithExpiryAsync(key, field, value, new Expiration(expiry), when, flags).ConfigureAwait(false));

        /// <inheritdoc/>
        public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(Wait(Context.Hashes.SetWithExpiryAsync(key, Required(hashFields, nameof(hashFields)), Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags)));

        /// <inheritdoc/>
        public async Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(await Context.Hashes.SetWithExpiryAsync(key, Required(hashFields, nameof(hashFields)), Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags).ConfigureAwait(false));

        /// <inheritdoc/>
        public RedisValue HashFieldSetAndSetExpiry(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(Wait(Context.Hashes.SetWithExpiryAsync(key, Required(hashFields, nameof(hashFields)), new Expiration(expiry), when, flags)));

        /// <inheritdoc/>
        public async Task<RedisValue> HashFieldSetAndSetExpiryAsync(RedisKey key, HashEntry[] hashFields, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => AsValue(await Context.Hashes.SetWithExpiryAsync(key, Required(hashFields, nameof(hashFields)), new Expiration(expiry), when, flags).ConfigureAwait(false));

        /// <summary>
        /// The 1/0 the old surface reports as a <see cref="RedisValue"/> for HSETEX; an empty hash-field
        /// set never reaches the server, and reports the same "nothing was written" it would have.
        /// </summary>
        private static RedisValue AsValue(bool written) => written ? 1 : 0;
    }
}
