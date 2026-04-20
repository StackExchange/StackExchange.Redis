using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Key Async
    public Task<bool> KeyCopyAsync(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyCopyAsync(sourceKey, destinationKey, destinationDatabase, replace, flags);

    public Task<bool> KeyDeleteAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDeleteAsync(key, flags);

    public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDeleteAsync(keys, flags);

    public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDumpAsync(key, flags);

    public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyEncodingAsync(key, flags);

    public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExistsAsync(key, flags);

    public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExistsAsync(keys, flags);

    public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        => GetActiveDatabase().KeyExpireAsync(key, expiry, flags);

    public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpireAsync(key, expiry, when, flags);

    public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags)
        => GetActiveDatabase().KeyExpireAsync(key, expiry, flags);

    public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpireAsync(key, expiry, when, flags);

    public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpireTimeAsync(key, flags);

    public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyFrequencyAsync(key, flags);

    public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyIdleTimeAsync(key, flags);

    public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyMoveAsync(key, database, flags);

    public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyPersistAsync(key, flags);

    public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRandomAsync(flags);

    public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRefCountAsync(key, flags);

    public Task<bool> KeyRenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRenameAsync(key, newKey, when, flags);

    public Task KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRestoreAsync(key, value, expiry, flags);

    public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTimeToLiveAsync(key, flags);

    public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTouchAsync(key, flags);

    public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTouchAsync(keys, flags);

    public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTypeAsync(key, flags);
}
