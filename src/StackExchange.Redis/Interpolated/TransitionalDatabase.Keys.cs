using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The key commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IDatabase"/> spelling of <c>RespSurface.Keys.cs</c>; see
    /// <c>TransitionalDatabase.Strings.cs</c> for why the two halves live in matching files.
    /// </para>
    /// <para>
    /// <b>One adapter here is not a pass-through.</b> The old <c>KeyExpire</c> overloads take a nullable
    /// deadline where <see langword="null"/> means "remove the expiry" - so a null routes to <c>PERSIST</c>,
    /// a different command with a different reply. The new surface refuses to accept
    /// <see cref="Expiration.Persist"/> as an expiry precisely so that choice has to be made in the open,
    /// and this is where the old signature makes it.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.DeleteAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.DeleteAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.DeleteAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Keys.DeleteAsync(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.ExistsAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.ExistsAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.ExistsAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Keys.ExistsAsync(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.PersistAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.PersistAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.TimeToLiveAsync(key, flags));

        /// <inheritdoc/>
        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.TimeToLiveAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.ExpireTimeAsync(key, flags));

        /// <inheritdoc/>
        public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.ExpireTimeAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.TouchAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.TouchAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.TouchAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Keys.TouchAsync(Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.RandomAsync(flags));

        /// <inheritdoc/>
        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
            => Context.Keys.RandomAsync(flags).AsTask();

        /// <inheritdoc/>
        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.TypeAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.TypeAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.RenameAsync(key, newKey, when, flags));

        /// <inheritdoc/>
        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Context.Keys.RenameAsync(key, newKey, when, flags).AsTask();

        /// <inheritdoc/>
        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.MoveAsync(key, database, flags));

        /// <inheritdoc/>
        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
            => Context.Keys.MoveAsync(key, database, flags).AsTask();

        /// <inheritdoc/>
        public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.CopyAsync(sourceKey, destinationKey, destinationDatabase, replace, flags));

        /// <inheritdoc/>
        public Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
            => Context.Keys.CopyAsync(sourceKey, destinationKey, destinationDatabase, replace, flags).AsTask();

        /// <inheritdoc/>
        /// <remarks>
        /// The one place this group copies: the old signature promises a <c>byte[]</c> the caller owns,
        /// where the surface hands out a lease that can share. For <c>DUMP</c> the payload is the whole
        /// value, so this is a real copy - which is a reason to prefer <c>Keys.Dump</c> in new code, and
        /// the reason that method documents the choice.
        /// </remarks>
        public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(KeyDumpAsyncCore(key, flags));

        /// <inheritdoc cref="KeyDump"/>
        public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => KeyDumpAsyncCore(key, flags).AsTask();

        private async ValueTask<byte[]?> KeyDumpAsyncCore(RedisKey key, CommandFlags flags)
        {
            using var lease = await Context.Keys.DumpAsync(key, flags).ForAwait();
            return lease?.ToArray();
        }

        /// <inheritdoc/>
        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags)
            => KeyExpire(key, expiry, ExpireWhen.Always, flags);

        /// <inheritdoc/>
        public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(ExpireCore(key, expiry, when, flags));

        /// <inheritdoc/>
        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags)
            => KeyExpire(key, expiry, ExpireWhen.Always, flags);

        /// <inheritdoc/>
        public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(ExpireCore(key, expiry, when, flags));

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags)
            => ExpireCore(key, expiry, ExpireWhen.Always, flags).AsTask();

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => ExpireCore(key, expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags)
            => ExpireCore(key, expiry, ExpireWhen.Always, flags).AsTask();

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => ExpireCore(key, expiry, when, flags).AsTask();

        /// <summary>A null deadline is not an expiry; it is <c>PERSIST</c>.</summary>
        private ValueTask<bool> ExpireCore(RedisKey key, TimeSpan? expiry, ExpireWhen when, CommandFlags flags)
            => expiry is null ? Context.Keys.PersistAsync(key, flags) : Context.Keys.ExpireAsync(key, expiry.Value, when, flags);

        /// <inheritdoc cref="ExpireCore(RedisKey, TimeSpan?, ExpireWhen, CommandFlags)"/>
        private ValueTask<bool> ExpireCore(RedisKey key, DateTime? expiry, ExpireWhen when, CommandFlags flags)
            => expiry is null ? Context.Keys.PersistAsync(key, flags) : Context.Keys.ExpireAsync(key, expiry.Value, when, flags);

        /// <inheritdoc/>
        public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.EncodingAsync(key, flags));

        /// <inheritdoc/>
        public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.EncodingAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.RefCountAsync(key, flags));

        /// <inheritdoc/>
        public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.RefCountAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.FrequencyAsync(key, flags));

        /// <inheritdoc/>
        public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.FrequencyAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.IdleTimeAsync(key, flags));

        /// <inheritdoc/>
        public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Keys.IdleTimeAsync(key, flags).AsTask();
}
}
