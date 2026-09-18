using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// The cursor scans, over the context surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are hand-written because <c>[AutoDatabase]</c> skips them</b>, and it skips them for a real
/// reason: deferred execution does not fit capture-and-replay, so an <see cref="IEnumerable{T}"/>-returning
/// member cannot be generated. That also means the generator cannot see whether they are implemented, so
/// SER352 has never counted them - <c>TransitionalScanGapTests</c> is what watches this file instead.
/// </para>
/// <para>
/// <b>Each pair of members returns the same object</b>, because the shipped contract says a scan is both
/// sequences at once: <c>HashTests.ScanAsync</c> enumerates <c>HashScan</c> as an
/// <see cref="IAsyncEnumerable{T}"/> and <c>HashScanAsync</c> as an <see cref="IEnumerable{T}"/>, so which
/// method produced it cannot decide which interfaces work. <c>RespScanEnumerable</c> carries a fetcher for
/// each, so neither face blocks on the other.
/// </para>
/// </remarks>
internal sealed partial class TransitionalDatabase
{
    // ---- HSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => HashScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- SSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- ZSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SortedSetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- VectorSetRangeEnumerate --------------------------------------------------------------------
    // NOT a cursor scan, and the shipped code says so: "intentionally not using scan naming in case a
    // VSCAN command is added later". It is keyset pagination over VRANGE - take a page, then ask again
    // from the last member with the start excluded - so it needs none of the machinery above.

    /// <inheritdoc/>
    public IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => Fallback<RedisValue>().VectorSetRangeEnumerate(key, start, end, count, exclude, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => Fallback<RedisValue>().VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
}
