using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
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
    internal partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(PrefersUnlink(in key, flags) ? _inner.Keys.UnlinkAsync(key, flags) : _inner.Keys.DeleteAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => (PrefersUnlink(in key, flags) ? _inner.Keys.UnlinkAsync(key, flags) : _inner.Keys.DeleteAsync(key, flags))
                .AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(DeleteMany(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => DeleteMany(Required(keys, nameof(keys)), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.ExistsAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.ExistsAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.ExistsAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.ExistsAsync(Required(keys, nameof(keys)), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.PersistAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.PersistAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.TimeToLiveAsync(key, flags));

        /// <inheritdoc/>
        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.TimeToLiveAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.ExpireTimeAsync(key, flags));

        /// <inheritdoc/>
        public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.ExpireTimeAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.TouchAsync(key, flags));

        /// <inheritdoc/>
        public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.TouchAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.TouchAsync(Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.TouchAsync(Required(keys, nameof(keys)), flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.RandomAsync(flags));

        /// <inheritdoc/>
        public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
            => _inner.Keys.RandomAsync(flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.TypeAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.TypeAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.RenameAsync(key, newKey, when, flags));

        /// <inheritdoc/>
        public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.RenameAsync(key, newKey, when, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.MoveAsync(key, database, flags));

        /// <inheritdoc/>
        public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.MoveAsync(key, database, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.CopyAsync(sourceKey, destinationKey, DestinationDatabase(destinationDatabase), replace, flags));

        /// <inheritdoc/>
        public Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.CopyAsync(sourceKey, destinationKey, DestinationDatabase(destinationDatabase), replace, flags).AsTask(AsyncState, flags);

        /// <summary>The destination database as the context surface wants it: an index, or null for "here".</summary>
        /// <remarks>
        /// <b>Only -1 means "the current database"</b>; anything below that is a caller mistake and the
        /// shipped surface says so with <see cref="ArgumentOutOfRangeException"/>. Folding every negative
        /// into null accepted <c>-10</c> silently and copied within the current database instead - the
        /// wrong operation, reported as success.
        /// </remarks>
        private static int? DestinationDatabase(int destinationDatabase) => destinationDatabase switch
        {
            < -1 => throw new ArgumentOutOfRangeException(nameof(destinationDatabase)),
            -1 => null,
            _ => destinationDatabase,
        };

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
            => KeyDumpAsyncCore(key, flags).AsTask(AsyncState, flags);

        private async ValueTask<byte[]?> KeyDumpAsyncCore(RedisKey key, CommandFlags flags)
        {
            using var lease = await _inner.Keys.DumpAsync(key, flags).ForAwait();
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
            => ExpireCore(key, expiry, ExpireWhen.Always, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => ExpireCore(key, expiry, when, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags)
            => ExpireCore(key, expiry, ExpireWhen.Always, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
            => ExpireCore(key, expiry, when, flags).AsTask(AsyncState, flags);

        /// <summary>Whether <c>UNLINK</c> should be sent in place of <c>DEL</c>.</summary>
        /// <remarks>
        /// <para>
        /// <b>The context surface exposes <c>DeleteAsync</c> and <c>UnlinkAsync</c> separately, which is
        /// right</b> - they are different commands with different costs, and a caller writing new code
        /// should say which they mean. But <c>IDatabase.KeyDelete</c> has always <i>auto-upgraded</i>
        /// to <c>UNLINK</c> where the server has it, so an adapter that always sends <c>DEL</c> turns a
        /// non-blocking delete into a blocking one. No test caught that: the reply is the same integer
        /// either way, and the difference only shows as the server stalling on a large key.
        /// </para>
        /// <para>
        /// <b>Positive confirmation is required here, where the expiry fallback deliberately does not
        /// require it.</b> The direction that is safe under uncertainty depends on what the fallback
        /// costs: falling back from <c>PEXPIRE</c> to <c>EXPIRE</c> silently loses precision, so unknown
        /// has to mean "use the modern command"; falling back from <c>UNLINK</c> to <c>DEL</c> loses
        /// nothing but latency, while guessing wrong is a hard error on an old server. Equivalent
        /// fallback, guess safe; lossy fallback, guess modern.
        /// </para>
        /// </remarks>
        private bool PrefersUnlink(in RedisKey key, CommandFlags flags)
            => _inner.Raw.CommandMap.IsAvailable(RedisCommand.UNLINK)
                && _inner.Raw.TryGetFeatures(RedisCommand.UNLINK, in key, flags, out var features)
                && features.Unlink;

        /// <inheritdoc cref="PrefersUnlink"/>
        private ValueTask<long> DeleteMany(RedisKey[] keys, CommandFlags flags)
        {
            // the routing question is asked with the FIRST key, as the shipped surface does: they share a
            // slot or the call would already have been refused, so any of them answers for all
            var probe = keys.Length == 0 ? default : keys[0];
            return PrefersUnlink(in probe, flags)
                ? _inner.Keys.UnlinkAsync(keys, flags)
                : _inner.Keys.DeleteAsync(keys, flags);
        }

        /// <summary>A null deadline is not an expiry; it is <c>PERSIST</c>.</summary>
        /// <remarks>
        /// <b>And neither is <see cref="DateTime.MaxValue"/>.</b> <see cref="Expiration"/>'s constructor
        /// already maps it to "no deadline" - it is how a caller says "never expire" with a value type
        /// that has no null - so it is the same request as passing null, and the shipped surface answers
        /// it the same way: <c>ExpiryTests</c> asserts the TTL is gone afterwards.
        /// <para>
        /// Asked of the <see cref="Expiration"/> rather than re-tested here, so the rule lives in one
        /// place. Testing the input instead was the bug: this threw "a deadline is required" for a
        /// perfectly ordinary call that the old surface had always accepted.
        /// </para>
        /// </remarks>
        private ValueTask<bool> ExpireCore(RedisKey key, TimeSpan? expiry, ExpireWhen when, CommandFlags flags)
            => expiry is null ? _inner.Keys.PersistAsync(key, flags) : ExpireDeadline(key, expiry.Value, when, flags);

        /// <inheritdoc cref="ExpireCore(RedisKey, TimeSpan?, ExpireWhen, CommandFlags)"/>
        private ValueTask<bool> ExpireCore(RedisKey key, DateTime? expiry, ExpireWhen when, CommandFlags flags)
            => expiry is null ? _inner.Keys.PersistAsync(key, flags) : ExpireDeadline(key, expiry.Value, when, flags);

        /// <inheritdoc cref="ExpireCore(RedisKey, TimeSpan?, ExpireWhen, CommandFlags)"/>
        private ValueTask<bool> ExpireDeadline(RedisKey key, Expiration expiry, ExpireWhen when, CommandFlags flags)
            => expiry.IsAbsolute || expiry.IsRelative
                ? _inner.Keys.ExpireAsync(key, expiry, when, flags)
                : _inner.Keys.PersistAsync(key, flags);

        /// <inheritdoc/>
        public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.EncodingAsync(key, flags));

        /// <inheritdoc/>
        public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.EncodingAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.RefCountAsync(key, flags));

        /// <inheritdoc/>
        public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.RefCountAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.FrequencyAsync(key, flags));

        /// <inheritdoc/>
        public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.FrequencyAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.IdleTimeAsync(key, flags));

        /// <inheritdoc/>
        public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.IdleTimeAsync(key, flags).AsTask(AsyncState, flags);

        // ---- RESTORE / MIGRATE / DEBUG OBJECT ----------------------------------------------------
        // The array and the EndPoint are the shipped spellings; the new surface takes a span and a
        // host/port, so this is where each is unpicked - the same division of labour as elsewhere here.

        /// <inheritdoc/>
        public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.RestoreAsync(key, Required(value, nameof(value)), expiry, flags));

        /// <inheritdoc/>
        public Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.RestoreAsync(key, Required(value, nameof(value)), expiry, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public void KeyMigrate(RedisKey key, System.Net.EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
            => Wait(Migrate(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags));

        /// <inheritdoc/>
        public Task KeyMigrateAsync(RedisKey key, System.Net.EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None)
            => Migrate(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags).AsTask(AsyncState, flags);

        /// <summary>Unpick the endpoint and apply the multiplexer's default timeout.</summary>
        /// <remarks>
        /// Both halves are the shipped behaviour: a non-positive timeout means "use the connection's", and
        /// an endpoint that is not host-and-port is rejected here rather than sent.
        /// </remarks>
        private ValueTask Migrate(RedisKey key, System.Net.EndPoint toServer, int toDatabase, int timeoutMilliseconds, MigrateOptions options, CommandFlags flags)
        {
            if (toServer is null) throw new ArgumentNullException(nameof(toServer));
            if (!Format.TryGetHostPort(toServer, out string? host, out int? port))
            {
                throw new ArgumentException($"Couldn't get host and port from {toServer}", nameof(toServer));
            }

            if (timeoutMilliseconds <= 0) timeoutMilliseconds = multiplexer.TimeoutMilliseconds;
            return _inner.Keys.MigrateAsync(key, host!, port!.Value, toDatabase, TimeSpan.FromMilliseconds(timeoutMilliseconds), options, flags);
        }

        /// <inheritdoc/>
        public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Keys.DebugObjectAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> DebugObjectAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => _inner.Keys.DebugObjectAsync(key, flags).AsTask(AsyncState, flags);
    }
}
