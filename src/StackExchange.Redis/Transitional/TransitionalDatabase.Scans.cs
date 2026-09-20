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
internal partial class TransitionalDatabase
{
    /// <summary>Whether the cursor form of a scan can actually be used here.</summary>
    /// <remarks>
    /// <b>Both halves, as the shipped surface asks them.</b> The scan command can be disabled in the
    /// command map, and a server old enough predates <c>SCAN</c> entirely - and in either case the shipped
    /// path does not fail, it falls back: with no cursor and no pattern it reads the whole collection as a
    /// single page, and otherwise refuses with a clear error. That fallback is real behaviour the suite
    /// asserts, not a nicety.
    /// <para>
    /// Delegated to the fallback rather than rebuilt, because the shipped implementation already knows
    /// every branch of it and a second copy would be a second thing to keep in step. The new surface owns
    /// the cursor path; the old one owns the answer when there is no cursor path.
    /// </para>
    /// </remarks>
    private bool Scannable(RedisCommand command, in RedisKey key, CommandFlags flags)
        => CanScan
            && _inner.Raw.CommandMap.IsAvailable(command)
            && (!_inner.Raw.TryGetFeatures(command, in key, flags, out var features) || features.Scan);

    // ---- HSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => HashScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().HashScan(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().HashScanNoValues(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().HashScanNoValuesAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- SSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.SSCAN, key, flags)
                ? _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().SetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.SSCAN, key, flags)
                ? _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- ZSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SortedSetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.ZSCAN, key, flags)
                ? _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().SortedSetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    /// <inheritdoc/>
    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.ZSCAN, key, flags)
                ? _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Fallback<RedisKey>().SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

    // ---- VectorSetRangeEnumerate --------------------------------------------------------------------
    // NOT a cursor scan, and the shipped code says so: "intentionally not using scan naming in case a
    // VSCAN command is added later". It is keyset pagination over VRANGE - take a page, then ask again
    // from the last member with the start excluded - so it needs none of the machinery above, and has
    // RespKeysetEnumerable of its own instead.
    //
    // The old signature's `count` is a PAGE size, not a total; the name is kept here because the
    // signature is shipped, and spelled `pageSize` on the context surface where it is not.

    /// <inheritdoc/>
    public IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => CanScan ? _inner.VectorSets.RangeEnumerateCore(key, start, end, count, exclude, flags) : throw NoScanning();

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => CanScan ? _inner.VectorSets.RangeEnumerateCore(key, start, end, count, exclude, flags) : throw NoScanning();
}
