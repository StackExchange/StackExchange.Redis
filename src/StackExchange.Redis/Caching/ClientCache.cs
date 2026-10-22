using System;
using System.Buffers;

namespace StackExchange.Redis.Caching;

/// <summary>
/// The client-side cache of a connection, as seen from outside: what it holds, and the means to tell it that
/// something it holds is wrong.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="RespConnectionExtensions.GetClientCache"/>, which returns <see langword="null"/> when
/// <see cref="ConfigurationOptions.ClientCache"/> did not ask for a cache. One instance per cache, for the life of
/// the connection; there is nothing to dispose. A class rather than an interface, so that members can be added
/// without breaking anybody.
/// </para>
/// <para>
/// <b>Invalidation here is a statement about this process, not about the server.</b> The server already announces
/// every change it sees, and the cache acts on those without help. These methods are for the change the server
/// cannot announce: one made by a module that opted out of signalling, a key whose expiry you know has passed (the
/// server does not announce expiry), or simply a value a caller knows to be wrong. They behave as a write from this
/// process does: the entries are dropped at once, on every member of a connection group, and
/// <see cref="CachePolicy.InvalidationGracePeriod"/> does not soften it. Nothing is sent to the server, and the key
/// is matched exactly as it was sent to the server, so a key read through a prefixed context must be given with its
/// prefix.
/// </para>
/// <para>
/// The counters are cumulative since the connection was made, and are the cache's own account of itself; the server's
/// command counters remain the measure a client cannot flatter - see the documentation on checking the hit rate.
/// Hits are not counted: the hit path records nothing beyond a per-entry flag, by design.
/// </para>
/// </remarks>
public sealed class ClientCache
{
    private readonly RespClientCache _cache;

    internal ClientCache(RespClientCache cache) => _cache = cache;

    /// <summary>The options this cache was created with.</summary>
    public CacheOptions Options => _cache.Options;

    /// <summary>Entries currently held, including ones invalidated or expired but not yet swept.</summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Memory held, as <see cref="CacheOptions.MaxBytes"/> counts it: whole storage slabs, however many of their
    /// replies are still live, plus each entry's bookkeeping.
    /// </summary>
    public long Bytes => _cache.Bytes;

    /// <summary>Distinct keys that cached entries have ever depended on.</summary>
    public int TrackedKeys => _cache.TrackedKeyCount;

    /// <summary>Replies stored, including refreshes of an existing entry.</summary>
    public long Stored => _cache.Stored;

    /// <summary>Entries dropped to stay inside <see cref="CacheOptions.MaxBytes"/> or <see cref="CacheOptions.MaxEntries"/>.</summary>
    public long Evicted => _cache.Evicted;

    /// <summary>
    /// Entries carried forward out of a storage slab being evicted, because they had been read since they were
    /// stored there; the eviction policy keeping what is used.
    /// </summary>
    public long Promoted => _cache.Promoted;

    /// <summary>Reads refused because the entry had outlived <see cref="CachePolicy.TimeToLive"/>.</summary>
    public long Expired => _cache.Expired;

    /// <summary>Background refreshes started by a read past <see cref="CachePolicy.RefreshAfter"/>.</summary>
    public long Refreshes => _cache.Refreshes;

    /// <summary>Reads served from an invalidated entry inside <see cref="CachePolicy.InvalidationGracePeriod"/>.</summary>
    public long ServedStale => _cache.ServedStale;

    /// <summary>Drop whatever is cached for <paramref name="key"/>, as a write of it from this process would.</summary>
    /// <param name="key">The key, exactly as sent to the server - including any prefix a context would add.</param>
    /// <returns>Whether anything cached depended on the key.</returns>
    public bool Invalidate(RedisKey key)
    {
        if (key.IsNull) return false;
        if (key.TryGetSimpleBuffer(out var simple)) return Invalidate(simple.AsSpan());

        // a prefixed or string key is rendered first; on the stack for the usual sizes
        var max = key.MaxByteCount();
        byte[]? rented = null;
        Span<byte> buffer = max <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(max));
        try
        {
            var length = key.CopyTo(buffer);
            return Invalidate(buffer.Slice(0, length));
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Drop whatever is cached for <paramref name="key"/>, as a write of it from this process would.</summary>
    /// <param name="key">The key bytes, exactly as sent to the server.</param>
    /// <returns>Whether anything cached depended on the key.</returns>
    public bool Invalidate(ReadOnlySpan<byte> key) => _cache.OnLocalWrite(key);

    /// <summary>
    /// Drop everything, here and on every member of a connection group, as <c>FLUSHALL</c> from this process would.
    /// </summary>
    public void InvalidateAll() => _cache.OnLocalFlush();
}
