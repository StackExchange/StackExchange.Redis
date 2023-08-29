using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    /// <summary>
    /// Enable the <a href="https://redis.io/commands/client-tracking/">client tracking</a> feature of redis
    /// </summary>
    /// <remarks>see also https://redis.io/docs/manual/client-side-caching/</remarks>
    /// <param name="keyInvalidated">The callback to be invoked when keys are determined to be invalidated</param>
    /// <param name="options">Additional flags to influence the behavior of client tracking</param>
    /// <param name="prefixes">Optionally restricts client-side caching notifications for these connections to a subset of key prefixes; this has performance implications (see the PREFIX option in CLIENT TRACKING)</param>
    public void EnableServerAssistedClientSideTracking(Func<RedisKey, ValueTask> keyInvalidated, ClientTrackingOptions options = ClientTrackingOptions.None, ReadOnlyMemory<RedisKey> prefixes = default)
    {
        if (_clientSideTracking is not null) ThrowOnceOnly();
        if (!prefixes.IsEmpty && (options & ClientTrackingOptions.Broadcast) == 0) ThrowPrefixNeedsBroadcast();
        var obj = new ClientSideTrackingState(this, keyInvalidated, options, prefixes);
        if (Interlocked.CompareExchange(ref _clientSideTracking, obj, null) is not null) ThrowOnceOnly();

        static void ThrowOnceOnly() => throw new InvalidOperationException("The " + nameof(EnableServerAssistedClientSideTracking) + " method can be invoked once-only per multiplexer instance");
        static void ThrowPrefixNeedsBroadcast() => throw new ArgumentException("Prefixes can only be specified when " + nameof(ClientTrackingOptions) + "." + nameof(ClientTrackingOptions.Broadcast) + " is used", nameof(prefixes));
    }

    private ClientSideTrackingState? _clientSideTracking;
    internal ClientSideTrackingState? ClientSideTracking => _clientSideTracking;
    internal sealed class ClientSideTrackingState
    {
        public bool IsAlive { get; private set; }
        private readonly Func<RedisKey, ValueTask> _keyInvalidated;
        public ClientTrackingOptions Options { get; }
        public ReadOnlyMemory<RedisKey> Prefixes { get; }

        private readonly Channel<RedisKey> _notifications;
        private readonly WeakReference<ConnectionMultiplexer> _multiplexer;
#if NETCOREAPP3_1_OR_GREATER
        private readonly Action<RedisKey>? _concurrentCallback;
#else
            private readonly WaitCallback? _concurrentCallback;
#endif

        public ClientSideTrackingState(ConnectionMultiplexer multiplexer, Func<RedisKey, ValueTask> keyInvalidated, ClientTrackingOptions options, ReadOnlyMemory<RedisKey> prefixes)
        {
            _keyInvalidated = keyInvalidated;
            Options = options;
            Prefixes = prefixes;
            _notifications = Channel.CreateUnbounded<RedisKey>(ChannelOptions);
            _ = Task.Run(RunAsync);
            IsAlive = true;
            _multiplexer = new(multiplexer);

            if ((options & ClientTrackingOptions.ConcurrentInvalidation) != 0)
            {
                _concurrentCallback = OnInvalidate;
            }
        }

#if !NETCOREAPP3_1_OR_GREATER
            private void OnInvalidate(object state) => OnInvalidate((RedisKey)state);
#endif

        private void OnInvalidate(RedisKey key)
        {
            try // not optimized for sync completions
            {
                var pending = _keyInvalidated(key);
                if (pending.IsCompleted)
                {   // observe result
                    pending.GetAwaiter().GetResult();
                }
                else
                {
                    _ = ObserveAsyncInvalidation(pending);
                }
            }
            catch (Exception ex) // handle sync failure (via immediate throw or faulted ValueTask)
            {
                OnCallbackError(ex);
            }
        }

        private async Task ObserveAsyncInvalidation(ValueTask pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnCallbackError(ex);
            }
        }

        private ConnectionMultiplexer? Multiplexer => _multiplexer.TryGetTarget(out var multiplexer) ? multiplexer : null;


        private void OnCallbackError(Exception error) => Multiplexer?.Logger?.LogError(error, "Client-side tracking invalidation callback failure");

        private async Task RunAsync()
        {
            while (await _notifications.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_notifications.Reader.TryRead(out var key))
                {
                    if (_concurrentCallback is not null)
                    {
#if NETCOREAPP3_1_OR_GREATER
                        ThreadPool.QueueUserWorkItem(_concurrentCallback, key, preferLocal: false);
#else
                            // eat the box
                            ThreadPool.QueueUserWorkItem(_concurrentCallback, key);
#endif
                    }
                    else
                    {
                        try
                        {
                            await _keyInvalidated(key).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            OnCallbackError(ex);
                        }
                    }
                }
            }
        }

        public void Write(RedisKey key) => _notifications.Writer.TryWrite(key);

        public void Shutdown()
        {
            IsAlive = false;
            _notifications.Writer.TryComplete(null);
        }

        private static readonly UnboundedChannelOptions ChannelOptions = new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = true };


    }
}

/// <summary>
/// Additional flags to influence the behavior of client tracking
/// </summary>
[Flags]
public enum ClientTrackingOptions
{
    /// <summary>
    /// No additional options
    /// </summary>
    None = 0,
    /// <summary>
    /// Enable tracking in broadcasting mode. In this mode invalidation messages are reported for all the prefixes specified, regardless of the keys requested by the connection. Instead when the broadcasting mode is not enabled, Redis will track which keys are fetched using read-only commands, and will report invalidation messages only for such keys.
    /// </summary>
    /// <remarks>This corresponds to CLIENT TRACKING ... BCAST; using <see cref="Broadcast"/> mode consumes less server memory, at the cost of more invalidation messages (i.e. clients are
    /// likely to receive invalidation messages for keys that the individual client is not using); this can be partially mitigated by using prefixes</remarks>
    Broadcast = 1 << 0,
    /// <summary>
    /// Send notifications about keys modified by this connection itself.
    /// </summary>
    /// <remarks>This corresponds to the <b>inverse</b> of CLIENT TRACKING ... NOLOOP; setting <see cref="NotifyForOwnCommands"/> means that your own writes will cause self-notification; this
    /// may mean that you discard a locally updated copy of the new value, hence this is disabled by default</remarks>
    NotifyForOwnCommands = 1 << 1,

    /// <summary>
    /// Indicates that the callback specified for key invalidation should be invoked concurrently rather than sequentially 
    /// </summary>
    ConcurrentInvalidation = 1 << 2,

    // to think about: OPTIN / OPTOUT ? I'm happy to implement on the basis of OPTIN for now, though
}
