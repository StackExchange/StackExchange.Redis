using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Key operations
    public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyCopy(sourceKey, destinationKey, destinationDatabase, replace, flags);

    public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyDelete(key, flags);

    public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyDelete(keys, flags);

    public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyDump(key, flags);

    public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyEncoding(key, flags);

    public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyExists(key, flags);

    public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyExists(keys, flags);

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        => GetDatabase().KeyExpire(key, expiry, flags);

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyExpire(key, expiry, when, flags);

    public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags)
        => GetDatabase().KeyExpire(key, expiry, flags);

    public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyExpire(key, expiry, when, flags);

    public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyExpireTime(key, flags);

    public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyFrequency(key, flags);

    public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyIdleTime(key, flags);

    public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyMove(key, database, flags);

    public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyPersist(key, flags);

    public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyRandom(flags);

    public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyRefCount(key, flags);

    public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyRename(key, newKey, when, flags);

    public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyRestore(key, value, expiry, flags);

    public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyTimeToLive(key, flags);

    public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyTouch(key, flags);

    public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyTouch(keys, flags);

    public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().KeyType(key, flags);
}
