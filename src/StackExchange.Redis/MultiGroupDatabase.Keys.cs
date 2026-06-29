using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Key operations
    public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyCopy(sourceKey, destinationKey, destinationDatabase, replace, flags);

    public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDelete(key, flags);

    public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDelete(keys, flags);

    public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyDump(key, flags);

    public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyEncoding(key, flags);

    public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExists(key, flags);

    public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExists(keys, flags);

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        => GetActiveDatabase().KeyExpire(key, expiry, flags);

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpire(key, expiry, when, flags);

    public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags)
        => GetActiveDatabase().KeyExpire(key, expiry, flags);

    public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpire(key, expiry, when, flags);

    public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyExpireTime(key, flags);

    public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyFrequency(key, flags);

    public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyIdleTime(key, flags);

    public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyMove(key, database, flags);

    public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyPersist(key, flags);

    public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRandom(flags);

    public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRefCount(key, flags);

    public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRename(key, newKey, when, flags);

    public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyRestore(key, value, expiry, flags);

    public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTimeToLive(key, flags);

    public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTouch(key, flags);

    public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyTouch(keys, flags);

    public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().KeyType(key, flags);
}
