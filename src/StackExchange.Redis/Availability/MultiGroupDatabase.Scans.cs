using System.Collections.Generic;

namespace StackExchange.Redis.Availability;

// The cursor scans PIN to the member that is active when the scan starts, by forwarding to its own
// database: a cursor is only meaningful to the server that issued it, so letting the base implementation
// page through the group executor would hand member A's cursor to member B after a failover - a silently
// wrong enumeration rather than a failed one. Pinned, a failover mid-scan faults the scan, which is the
// honest outcome.
internal sealed partial class MultiGroupDatabase
{
    public override IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, flags);

    public override IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanNoValues(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().SetScan(key, pattern, pageSize, flags);

    public override IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, flags);

    public override IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerate(key, start, end, count, exclude, flags);

    public override IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    public override IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
}
