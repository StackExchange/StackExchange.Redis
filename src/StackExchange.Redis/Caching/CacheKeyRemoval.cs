using System;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// Why a key the caller had read through the client-side cache stopped being servable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A correctness-only consumer treats every reason as "drop".</b> The split exists for smarter
    /// consumers, separating "the value changed, or may have" (<see cref="Invalidated"/>,
    /// <see cref="LocalWrite"/>, <see cref="Replaced"/>) from cache hygiene the server knows nothing
    /// about (<see cref="Evicted"/>, <see cref="Expired"/>) and from the bulk case
    /// (<see cref="Flushed"/>).
    /// </para>
    /// </remarks>
    public enum CacheKeyRemovalReason
    {
        /// <summary>The server said the key changed: an invalidation push or keyspace notification.</summary>
        Invalidated,

        /// <summary>This process wrote the key itself - a stronger statement than a server push, because it is a fact rather than a race.</summary>
        LocalWrite,

        /// <summary>The cached value was replaced by a fresh fill; the value that was read is gone.</summary>
        Replaced,

        /// <summary>The entry aged past the cache's lifetime - the backstop against missed invalidations - and was reclaimed.</summary>
        /// <remarks>
        /// Expiry is decided lazily, so this fires at physical reclamation rather than at the moment the
        /// lifetime passed. A consumer with its own freshness bound should keep enforcing it; this reason
        /// is the backstop's backstop, not a timer.
        /// </remarks>
        Expired,

        /// <summary>The entry was evicted to stay inside the cache's budget; the server may still be tracking the key.</summary>
        Evicted,

        /// <summary>Everything went at once: a server flush, a lost connection, or tracking being torn down.</summary>
        Flushed,
    }

    /// <summary>
    /// Told when a key read through an enrolled context stops being servable from the client-side
    /// cache; see <see cref="RespDatabaseContext.WithRemovalHandler"/>.
    /// </summary>
    /// <param name="key">
    /// The key exactly as stored - prefixes included - valid only for the duration of the callback.
    /// </param>
    /// <param name="reason">Why the key stopped being servable.</param>
    /// <remarks>
    /// <para>
    /// <b>Called on a thread-pool thread, never on the connection's reader</b> - the same dispatch model
    /// as pub/sub handlers and connection events - so being notified cannot stall the connection.
    /// Exceptions are swallowed: a notification is advice, and advice must not break the cache.
    /// </para>
    /// <para>
    /// <b>One enrollment, one notification.</b> An enrollment is armed by a serve and consumed by the
    /// first removal at or after it; reading the key again re-arms it. A notification can therefore
    /// arrive after the consumer has already re-read the key - dropping derived state is always safe, so
    /// a late notification costs a rebuild, never correctness.
    /// </para>
    /// </remarks>
    public delegate void CacheKeyRemovedHandler(ReadOnlySpan<byte> key, CacheKeyRemovalReason reason);
}
