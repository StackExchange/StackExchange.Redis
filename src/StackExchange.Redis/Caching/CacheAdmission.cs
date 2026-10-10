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
    /// </remarks>
    public enum CacheAdmission
    {
        /// <summary>Store every cacheable reply the first time it misses. The default.</summary>
        OnFirstMiss = 0,

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
        OnRepeatedMiss = 1,
    }
}
