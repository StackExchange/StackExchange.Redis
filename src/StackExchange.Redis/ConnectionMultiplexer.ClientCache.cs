using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private int _cacheSubscribed;

    /// <summary>
    /// When the client-side cache's mode in force is <see cref="CacheTrackingMode.KeyspaceNotifications"/>, subscribe it
    /// to the keyspace channels that invalidate it; nothing to do otherwise, or if that has already been done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called after connecting, for a cache configured that way, and by the handshake that makes
    /// <see cref="CacheTrackingMode.Default"/> fall back to it - which can happen at any later reconnect, so this is
    /// not only a connect-time step. Once only: a second handler would invalidate every key twice.
    /// </para>
    /// <para>
    /// One pattern per prefix (<c>__keyspace@*__:{prefix}*</c>), or a single wildcard without prefixes. Every
    /// database, because the cache keys its entries by database but invalidates by key alone - as a tracking push
    /// does - so a notification from another database costs a miss, never a stale read.
    /// </para>
    /// <para>
    /// <b>Cluster</b>: these are multi-node channels, so the subscriber makes them on every primary, which is
    /// what keyspace notifications need - each node raises them only for the keys it owns.
    /// </para>
    /// <para>
    /// <b>Not fatal.</b> The subscription is registered before it is sent, so it is restored with the
    /// subscription connection whatever happens here; a failure to establish it now (pub/sub refused, say) is
    /// logged, and leaves the cache retired by <see cref="CachePolicy.TimeToLive"/> - which is what this mode
    /// promises when the server is not raising notifications anyway.
    /// </para>
    /// </remarks>
    internal async Task SubscribeCacheInvalidationAsync()
    {
        if (ClientCache is not { EffectiveTrackingMode: CacheTrackingMode.KeyspaceNotifications } cache) return;
        if (Interlocked.Exchange(ref _cacheSubscribed, 1) != 0) return;

        // the channel is __keyspace@{db}__:{key}, and the key is what the cache matches on, as the bytes it was sent as
        Action<RedisChannel, RedisValue> handler = (channel, _) => cache.OnInvalidate(RedisChannel.StripKeySpacePrefix(channel.Span));

        ISubscriber subscriber = DefaultSubscriber;
        var prefixes = cache.Options.PrefixBytes;
        try
        {
            if (prefixes.Length == 0)
            {
                await subscriber.SubscribeAsync(RedisChannel.KeySpacePattern(RedisKey.Null), handler).ForAwait();
            }
            else
            {
                var pending = new Task[prefixes.Length];
                for (var i = 0; i < prefixes.Length; i++)
                {
                    pending[i] = subscriber.SubscribeAsync(RedisChannel.KeySpacePrefix(prefixes[i].AsSpan()), handler);
                }

                await Task.WhenAll(pending).ForAwait();
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarningCacheSubscriptionFailed(ex, ex.Message);
        }
    }
}
