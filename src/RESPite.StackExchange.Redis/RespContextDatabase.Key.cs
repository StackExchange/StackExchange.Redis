using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    public bool KeyCopy(
        RedisKey sourceKey,
        RedisKey destinationKey,
        int destinationDatabase = -1,
        bool replace = false,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Copy(sourceKey, destinationKey, destinationDatabase, replace).Wait(SyncTimeout);

    public Task<bool> KeyCopyAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        int destinationDatabase = -1,
        bool replace = false,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Copy(sourceKey, destinationKey, destinationDatabase, replace).AsTask();

    public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Del(key).Wait(SyncTimeout);

    public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Del(keys).Wait(SyncTimeout);

    public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Del(key).AsTask();

    public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Del(keys).AsTask();

    public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Dump(key).Wait(SyncTimeout);

    public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Dump(key).AsTask();

    public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectEncoding(key).Wait(SyncTimeout);

    public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectEncoding(key).AsTask();

    public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Exists(key).Wait(SyncTimeout);

    public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Exists(keys).Wait(SyncTimeout);

    public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Exists(key).AsTask();

    public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Exists(keys).AsTask();

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        => Context(flags).Keys().Expire(key, expiry).Wait(SyncTimeout);

    public bool KeyExpire(
        RedisKey key,
        TimeSpan? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Expire(key, expiry, when).Wait(SyncTimeout);

    public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags)
        => Context(flags).Keys().ExpireAt(key, expiry).Wait(SyncTimeout);

    public bool KeyExpire(
        RedisKey key,
        DateTime? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ExpireAt(key, expiry, when).Wait(SyncTimeout);

    public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        => Context(flags).Keys().Expire(key, expiry).AsTask();

    public Task<bool> KeyExpireAsync(
        RedisKey key,
        TimeSpan? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Expire(key, expiry, when).AsTask();

    public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags)
        => Context(flags).Keys().ExpireAt(key, expiry).AsTask();

    public Task<bool> KeyExpireAsync(
        RedisKey key,
        DateTime? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ExpireAt(key, expiry, when).AsTask();

    public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().PExpireTime(key).Wait(SyncTimeout);

    public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().PExpireTime(key).AsTask();

    public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectFreq(key).Wait(SyncTimeout);

    public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectFreq(key).AsTask();

    public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectIdleTime(key).Wait(SyncTimeout);

    public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectIdleTime(key).AsTask();

    public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Move(key, database).Wait(SyncTimeout);

    public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Move(key, database).AsTask();

    public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Persist(key).Wait(SyncTimeout);

    public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Persist(key).AsTask();

    public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().RandomKey().Wait(SyncTimeout);

    public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().RandomKey().AsTask();

    public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectRefCount(key).Wait(SyncTimeout);

    public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().ObjectRefCount(key).AsTask();

    public bool KeyRename(
        RedisKey key,
        RedisKey newKey,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Rename(key, newKey, when).Wait(SyncTimeout);

    public Task<bool> KeyRenameAsync(
        RedisKey key,
        RedisKey newKey,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Rename(key, newKey, when).AsTask();

    public void KeyRestore(
        RedisKey key,
        byte[] value,
        TimeSpan? expiry = null,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Restore(key, expiry, value).Wait(SyncTimeout);

    public Task KeyRestoreAsync(
        RedisKey key,
        byte[] value,
        TimeSpan? expiry = null,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Restore(key, expiry, value).AsTask();

    public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Pttl(key).Wait(SyncTimeout);

    public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Pttl(key).AsTask();

    public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Touch(key).Wait(SyncTimeout);

    public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Touch(keys).Wait(SyncTimeout);

    public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Touch(key).AsTask();

    public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Touch(keys).AsTask();

    public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Type(key).Wait(SyncTimeout);

    public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).Keys().Type(key).AsTask();
}
