using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Tying a client-side cache to the connection whose invalidations keep it honest.
    /// </summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespCacheConnectionExtensions
    {
        /// <summary>
        /// Empty the cache whenever a connection is lost.
        /// </summary>
        /// <param name="cache">The cache to flush.</param>
        /// <param name="multiplexer">The connection to watch.</param>
        /// <returns>A subscription; dispose it to stop watching.</returns>
        /// <remarks>
        /// <para>
        /// <b>Not optional, and not a tidy-up.</b> Server-assisted invalidation only works while we are
        /// listening: anything that changes during a disconnect is never announced, because the server
        /// forgets a client it has lost. An entry that survives the gap is stale with nothing left in the
        /// system that will ever say so. The Redis documentation puts it plainly - <i>"make sure that if the
        /// connection is lost, the local cache is flushed"</i>.
        /// </para>
        /// <para>
        /// Flushes on <b>any</b> connection failure rather than trying to work out whether that particular
        /// connection was carrying invalidations. Over-flushing costs a round trip per key; under-flushing
        /// serves data that is wrong with no bound on how long for, and this design errs in the same
        /// direction everywhere else.
        /// </para>
        /// <para>
        /// It does not cover the case where a connection has failed and <i>nothing has noticed</i> - no
        /// event is raised for a socket that is quietly dead. That is what
        /// <see cref="CachePolicy.TimeToLive"/> is for: a finite lifetime bounds the damage when detection
        /// itself fails, which is why it must never be infinite.
        /// </para>
        /// <para>
        /// Explicit for now because the cache is attached to a context rather than owned by the multiplexer.
        /// When <c>GetDatabase()</c> eventually returns a cache-aware database this becomes part of building
        /// one, and callers stop having to remember it - which is the right end state, because a cache
        /// nobody remembered to wire up is a cache that goes quietly wrong.
        /// </para>
        /// </remarks>
        public static IDisposable FlushOnDisconnect(this RespClientCache cache, IConnectionMultiplexer multiplexer)
        {
            if (cache is null) throw new ArgumentNullException(nameof(cache));
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));

            return new DisconnectFlusher(cache, multiplexer);
        }

        private sealed class DisconnectFlusher : IDisposable
        {
            private readonly RespClientCache _cache;
            private IConnectionMultiplexer? _multiplexer;

            internal DisconnectFlusher(RespClientCache cache, IConnectionMultiplexer multiplexer)
            {
                _cache = cache;
                _multiplexer = multiplexer;
                multiplexer.ConnectionFailed += OnConnectionFailed;
            }

            /// <remarks>
            /// Deliberately ignores which endpoint or connection type it was. Deciding that a particular
            /// failure could not have cost us an invalidation is a judgement this has no way to make, and
            /// getting it wrong is silent.
            /// </remarks>
            private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e) => _cache.OnFlush();

            public void Dispose()
            {
                var multiplexer = Interlocked.Exchange(ref _multiplexer, null);
                if (multiplexer is not null) multiplexer.ConnectionFailed -= OnConnectionFailed;
            }
        }
    }
}
