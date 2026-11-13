namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// Where the values read under a <see cref="CachePolicy"/> are kept.
    /// </summary>
    public enum CacheStorage
    {
        /// <summary>
        /// Replies are stored in the client-side cache, subject to admission and budgets: the ordinary
        /// client-side caching that the rest of <see cref="CacheOptions"/> describes.
        /// </summary>
        Local = 0,

        /// <summary>
        /// Replies are never stored in the client-side cache - an external layer keeps the values - while
        /// key tracking and <see cref="CachePolicy.RemovalHandler"/> enrollment still ride every read.
        /// See <see cref="CachePolicy.Storage"/>.
        /// </summary>
        External = 1,
    }
}
