using System;
using System.Collections.Generic;

namespace StackExchange.Redis;

// Streaming cursor scans (IEnumerable / IAsyncEnumerable) are deferred-execution and do not fit the
// capture-and-replay shape, so [AutoDatabase] skips them by category - they are the one part of the
// interface that does have to be listed by hand, and the only part of this type that a new command could
// oblige someone to touch.
//
// Like the generated members, these throw unless a fallback was supplied; see TransitionalDatabase._fallback.
internal sealed partial class TransitionalDatabase
{
    private IDatabase Scans => _fallback ?? throw NotMoved();

    private static NotImplementedException NotMoved()
        => new("This command has not yet moved to the RESP context surface.");

    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => Scans.HashScan(key, pattern, pageSize, flags);

    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.HashScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.HashScanNoValues(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => Scans.SetScan(key, pattern, pageSize, flags);

    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.SetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => Scans.SortedSetScan(key, pattern, pageSize, flags);

    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.SortedSetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => Scans.VectorSetRangeEnumerate(key, start, end, count, exclude, flags);

    public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => Scans.SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => Scans.VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
}
