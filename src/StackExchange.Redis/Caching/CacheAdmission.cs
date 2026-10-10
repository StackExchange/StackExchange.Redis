namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// When a reply that missed the cache is stored in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Storing a reply is not free: it copies the reply, registers what it depends on, and keeps an entry alive until
    /// it is evicted or invalidated - so on a workload that rarely reads the same thing twice, the cache costs more than
    /// it saves, and the entries it does keep get pushed out by ones nobody will read again.
    /// </para>
    /// <para>
    /// <see cref="OnRepeatedMiss"/> answers that the way TinyLFU's "doorkeeper" does: the first miss for a request is
    /// remembered rather than stored, and only a request that misses again soon afterwards is cached. A request that
    /// is read once never touches the cache; one that is read often costs one extra miss before it is cached.
    /// </para>
    /// <para>
    /// <b>The default since 4.0</b>, because it wins on the workloads that were measured: on a skewed one it raised the
    /// hit rate (one-off reads no longer evict the hot keys) as well as throughput, and on one that never repeats it
    /// removed the cache's overhead entirely. It loses only where every key is read exactly twice and never again.
    /// </para>
    /// </remarks>
    public enum CacheAdmission
    {
        /// <summary>
        /// Let the library choose; today that is <see cref="OnRepeatedMiss"/>.
        /// </summary>
        /// <remarks>
        /// Zero means "no opinion", as it does for <see cref="CacheTrackingMode.Default"/>: an unset value is not a
        /// considered choice, and keeping the two apart lets the library change what it resolves to without
        /// overriding anybody who named a mode. Name one explicitly if you depend on its behaviour.
        /// </remarks>
        Default = 0,

        /// <summary>Store every cacheable reply the first time it misses.</summary>
        /// <remarks>
        /// Right when nearly every read repeats and the data fits the cache; otherwise one-off reads cost a fill each
        /// and push out the entries that are re-read. Measured on a skewed workload (Zipf, s = 0.99), it held a 72.4%
        /// hit rate where <see cref="OnRepeatedMiss"/> held 76.6%, at two thirds of the throughput.
        /// </remarks>
        OnFirstMiss = 1,

        /// <summary>
        /// Store a reply only when the same request has missed recently before; a first miss is served without
        /// touching the cache.
        /// </summary>
        /// <remarks>
        /// "Recently" is a window of misses, not of time: the record of first misses is a small fixed-size filter,
        /// cleared once it has seen a few times as many requests as the cache can hold. It can be wrong in one
        /// direction only - occasionally admitting a request on its first miss - which costs no more than
        /// <see cref="OnFirstMiss"/> would have.
        /// </remarks>
        OnRepeatedMiss = 2,
    }
}
