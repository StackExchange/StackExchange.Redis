using System.Collections.Generic;

namespace StackExchange.Redis.Availability;

// Streaming cursor scans (IEnumerable / IAsyncEnumerable) are deferred-execution and don't fit the
// capture-and-replay shape, so [AutoDatabase] skips them; they forward straight to the active member.
internal sealed partial class MultiGroupDatabase
{
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, flags);

    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanNoValues(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().SetScan(key, pattern, pageSize, flags);

    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, flags);

    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerate(key, start, end, count, exclude, flags);

    public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
}
