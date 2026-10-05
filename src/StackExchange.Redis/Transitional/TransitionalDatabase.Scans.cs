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
    /// <b>Both halves, as the v3 surface asked them.</b> The scan command can be disabled in the
    /// command map, and a server old enough predates <c>SCAN</c> entirely - and in either case the v3
    /// path did not fail, it fell back: with no cursor and no pattern it read the whole collection as a
    /// single page, and otherwise refused with a clear error. That fallback is real behaviour the suite
    /// asserts, not a nicety.
    /// <para>
    /// It was delegated to the v3 implementation while there was one, because that knew every branch of
    /// it; with the old database gone it is emulated here (see <c>Emulate</c>). The new surface owns the
    /// cursor path; the emulation owns the answer when there is no cursor path.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>Two conditions that look alike and are not.</b> A batch or transaction cannot scan at all - the
    /// cursor for each page comes from the previous page's reply, which has not been sent - and that is a
    /// refusal, not something a fallback can serve. The command merely being unavailable IS serviceable,
    /// by the emulation. Folding them together made a batch delegate instead of refusing, which the
    /// batch test caught immediately.
    /// </remarks>
    private bool Scannable(RedisCommand command, in RedisKey key, CommandFlags flags)
    {
        if (!CanScan) throw NoScanning(); // a batch or transaction: refused outright, never delegated
        return _inner.Raw.CommandMap.IsAvailable(command)
            && (!_inner.Raw.TryGetFeatures(command, in key, flags, out var features) || features.Scan);
    }

#pragma warning disable CS0618 // the emulation is obsolete BY DESIGN - see Emulate; these are its intended callers
    // ---- HSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => HashScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Emulate(() => HashGetAllAsync(key, flags), cursor, pageOffset, RedisCommand.HSCAN, RedisCommand.HGETALL, !pattern.IsNull);

    /// <inheritdoc/>
    public IAsyncEnumerable<HashEntry> HashScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : EmulateAsync(() => HashGetAllAsync(key, flags), cursor, pageOffset, RedisCommand.HSCAN, RedisCommand.HGETALL, !pattern.IsNull);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> HashScanNoValues(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Emulate(() => HashKeysAsync(key, flags), cursor, pageOffset, RedisCommand.HSCAN, RedisCommand.HKEYS, !pattern.IsNull);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> HashScanNoValuesAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.HSCAN, key, flags)
                ? _inner.Hashes.ScanNoValuesCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : EmulateAsync(() => HashKeysAsync(key, flags), cursor, pageOffset, RedisCommand.HSCAN, RedisCommand.HKEYS, !pattern.IsNull);

    // ---- SSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.SSCAN, key, flags)
                ? _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Emulate(() => SetMembersAsync(key, flags), cursor, pageOffset, RedisCommand.SSCAN, RedisCommand.SMEMBERS, !pattern.IsNull);

    /// <inheritdoc/>
    public IAsyncEnumerable<RedisValue> SetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.SSCAN, key, flags)
                ? _inner.Sets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : EmulateAsync(() => SetMembersAsync(key, flags), cursor, pageOffset, RedisCommand.SSCAN, RedisCommand.SMEMBERS, !pattern.IsNull);

    // ---- ZSCAN --------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => SortedSetScan(key, pattern, pageSize, RedisBase.CursorUtils.Origin, 0, flags);

    /// <inheritdoc/>
    public IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.ZSCAN, key, flags)
                ? _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : Emulate(() => SortedSetRangeByRankWithScoresAsync(key, flags: flags), cursor, pageOffset, RedisCommand.ZSCAN, RedisCommand.ZRANGE, !pattern.IsNull);

    /// <inheritdoc/>
    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
            => Scannable(RedisCommand.ZSCAN, key, flags)
                ? _inner.SortedSets.ScanCore(key, pattern, pageSize, cursor, pageOffset, flags)
                : EmulateAsync(() => SortedSetRangeByRankWithScoresAsync(key, flags: flags), cursor, pageOffset, RedisCommand.ZSCAN, RedisCommand.ZRANGE, !pattern.IsNull);

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

    // ---- the pre-SCAN emulation ----------------------------------------------------------------------

    /// <summary>
    /// What a scan degrades to when the server predates <c>SCAN</c> or the command map forbids it: one
    /// page holding the entire structure, read with <c>HGETALL</c>, <c>HKEYS</c>, <c>SMEMBERS</c> or
    /// <c>ZRANGE</c>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="all">Reads the whole structure.</param>
    /// <param name="cursor">Must be the origin; there is no cursor to resume.</param>
    /// <param name="pageOffset">How many elements of the single page to skip.</param>
    /// <param name="scan">The scan command that is unavailable, for the "not supported" message.</param>
    /// <param name="whole">The command standing in for it, for the "no cursor" message.</param>
    /// <param name="patterned">Whether the caller supplied a match pattern.</param>
    /// <remarks>
    /// <para>
    /// <b>Obsolete on purpose, and not because it is going away.</b> It is here so that a deployment which
    /// worked before the surface moved keeps working - the shipped path has always degraded this way - but
    /// it is not something to reach for: it materialises the entire structure in one reply, which is the
    /// exact failure mode <c>SCAN</c> exists to prevent. The attribute is how that reaches a reader, and
    /// the call sites suppress it deliberately.
    /// </para>
    /// <para>
    /// The two refusals are the shipped ones, not new: a non-origin cursor cannot be honoured because
    /// there is no cursor to resume from, and a pattern cannot be honoured because the whole-structure
    /// commands do not filter. Both throw rather than quietly returning something else.
    /// </para>
    /// </remarks>
    [Obsolete("The server or command map does not offer SCAN, so this reads the whole structure in one reply; prefer a server that supports SCAN.")]
    private static IEnumerable<T> Emulate<T>(
        Func<Task<T[]>> all, long cursor, int pageOffset, RedisCommand scan, RedisCommand whole, bool patterned)
    {
        if (cursor != RedisBase.CursorUtils.Origin) throw ExceptionFactory.NoCursor(whole);
        if (patterned) throw ExceptionFactory.NotSupported(true, scan);

        return Iterate(all(), pageOffset);

        static IEnumerable<T> Iterate(Task<T[]> pending, int skip)
        {
            foreach (var item in pending.GetAwaiter().GetResult())
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                yield return item;
            }
        }
    }

    /// <inheritdoc cref="Emulate{T}"/>
    [Obsolete("The server or command map does not offer SCAN, so this reads the whole structure in one reply; prefer a server that supports SCAN.")]
    private static async IAsyncEnumerable<T> EmulateAsync<T>(
        Func<Task<T[]>> all, long cursor, int pageOffset, RedisCommand scan, RedisCommand whole, bool patterned)
    {
        if (cursor != RedisBase.CursorUtils.Origin) throw ExceptionFactory.NoCursor(whole);
        if (patterned) throw ExceptionFactory.NotSupported(true, scan);

        var skip = pageOffset;
        foreach (var item in await all().ForAwait())
        {
            if (skip > 0)
            {
                skip--;
                continue;
            }

            yield return item;
        }
    }
#pragma warning restore CS0618

}
