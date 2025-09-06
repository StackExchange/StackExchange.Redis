using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class ProxiedDatabase
{
    // Async Key methods
    public Task<bool> KeyCopyAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        int destinationDatabase = -1,
        bool replace = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> KeyDeleteAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<byte[]?> KeyDumpAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<string?> KeyEncodingAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> KeyExistsAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags) =>
        throw new NotImplementedException();

    public Task<bool> KeyExpireAsync(
        RedisKey key,
        TimeSpan? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags) =>
        throw new NotImplementedException();

    public Task<bool> KeyExpireAsync(
        RedisKey key,
        DateTime? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<DateTime?> KeyExpireTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long?> KeyFrequencyAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<TimeSpan?> KeyIdleTimeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyMoveAsync(RedisKey key, int database, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyPersistAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisKey> KeyRandomAsync(CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long?> KeyRefCountAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyRenameAsync(
        RedisKey key,
        RedisKey newKey,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task KeyRestoreAsync(
        RedisKey key,
        byte[] value,
        TimeSpan? expiry = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> KeyTouchAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> KeyTouchAsync(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisType> KeyTypeAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Key methods
    public bool KeyCopy(
        RedisKey sourceKey,
        RedisKey destinationKey,
        int destinationDatabase = -1,
        bool replace = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    [RespCommand("del")]
    public partial bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None);

    public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags) =>
        throw new NotImplementedException();

    public bool KeyExpire(
        RedisKey key,
        TimeSpan? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags) =>
        throw new NotImplementedException();

    public bool KeyExpire(
        RedisKey key,
        DateTime? expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyRename(
        RedisKey key,
        RedisKey newKey,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public void KeyRestore(
        RedisKey key,
        byte[] value,
        TimeSpan? expiry = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
