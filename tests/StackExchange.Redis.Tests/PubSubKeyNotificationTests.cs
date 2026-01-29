using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

// ReSharper disable once UnusedMember.Global - used via test framework
public sealed class PubSubKeyNotificationTestsCluster(ITestOutputHelper output, ITestContextAccessor context, SharedConnectionFixture fixture)
    : PubSubKeyNotificationTests(output, context, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts;
}

// ReSharper disable once UnusedMember.Global - used via test framework
public sealed class PubSubKeyNotificationTestsStandalone(ITestOutputHelper output, ITestContextAccessor context, SharedConnectionFixture fixture)
    : PubSubKeyNotificationTests(output, context, fixture)
{
}

public abstract class PubSubKeyNotificationTests(ITestOutputHelper output, ITestContextAccessor context, SharedConnectionFixture? fixture = null)
    : TestBase(output, fixture)
{
    private const int DefaultKeyCount = 10;
    private const int DefaultEventCount = 512;
    private CancellationToken CancellationToken => context.Current.CancellationToken;

    private RedisKey[] InventKeys(out byte[] prefix, int count = DefaultKeyCount)
    {
        RedisKey[] keys = new RedisKey[count];
        var prefixString = $"{Guid.NewGuid()}/";
        prefix = Encoding.UTF8.GetBytes(prefixString);
        for (int i = 0; i < count; i++)
        {
            keys[i] = $"{prefixString}{Guid.NewGuid()}";
        }
        return keys;
    }

    private RedisKey SelectKey(RedisKey[] keys) => keys[SharedRandom.Next(0, keys.Length)];

#if NET6_0_OR_GREATER
    private static Random SharedRandom => Random.Shared;
#else
    private static Random SharedRandom { get; } = new();
#endif

    [Fact]
    public async Task KeySpace_Events_Enabled()
    {
        // see https://redis.io/docs/latest/develop/pubsub/keyspace-notifications/#configuration
        await using var conn = Create(allowAdmin: true);
        int failures = 0;
        foreach (var ep in conn.GetEndPoints())
        {
            var server = conn.GetServer(ep);
            var config = (await server.ConfigGetAsync("notify-keyspace-events")).Single();
            Log($"[{Format.ToString(ep)}] notify-keyspace-events: '{config.Value}'");

            // this is a very broad config, but it's what we use in CI (and probably a common basic config)
            if (config.Value != "AKE")
            {
                failures++;
            }
        }
        // for details, check the log output
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task KeySpace_CanSubscribe_ManualPublish()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var channel = RedisChannel.KeyEvent("nonesuch"u8, database: null);
        Log($"Monitoring channel: {channel}");
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        int count = 0;
        await sub.SubscribeAsync(channel, (_, _) => Interlocked.Increment(ref count));

        // to publish, we need to remove the marker that this is a multi-node channel
        var asLiteral = RedisChannel.Literal(channel.ToString());
        await sub.PublishAsync(asLiteral, Guid.NewGuid().ToString());

        int expected = GetConnectedCount(conn, channel);
        await Task.Delay(100).ForAwait();
        Assert.Equal(expected, count);
    }

    // this looks past the horizon to see how many connections we actually have for a given channel,
    // which could be more than 1 in a cluster scenario
    private static int GetConnectedCount(IConnectionMultiplexer muxer, in RedisChannel channel)
        => muxer is ConnectionMultiplexer typed && typed.TryGetSubscription(channel, out var sub)
            ? sub.GetConnectionCount() : 1;

    private sealed class Counter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public int Increment() => Interlocked.Increment(ref _count);
    }

    [Fact]
    public async Task KeyEvent_CanObserveSimple_ViaCallbackHandler()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys(out var prefix);
        var channel = RedisChannel.KeyEvent(KeyNotificationType.SAdd, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Log($"Monitoring channel: {channel}");
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
            {
                OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public async Task KeyEvent_CanObserveSimple_ViaQueue()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys(out var prefix);
        var channel = RedisChannel.KeyEvent(KeyNotificationType.SAdd, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Log($"Monitoring channel: {channel}");
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }

        var queue = await sub.SubscribeAsync(channel);
        _ = Task.Run(async () =>
        {
            await foreach (var msg in queue.WithCancellation(CancellationToken))
            {
                callbackCount.Increment();
                if (msg.TryParseKeyNotification(out var notification)
                    && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
                {
                    OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
                }
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await queue.UnsubscribeAsync();
    }

    [Fact]
    public async Task KeyNotification_CanObserveSimple_ViaCallbackHandler()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys(out var prefix);
        var channel = RedisChannel.KeySpacePrefix(prefix, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.True(channel.IsPattern);
        Log($"Monitoring channel: {channel}");
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }

        var queue = await sub.SubscribeAsync(channel);
        _ = Task.Run(async () =>
        {
            await foreach (var msg in queue.WithCancellation(CancellationToken))
            {
                callbackCount.Increment();
                if (msg.TryParseKeyNotification(out var notification)
                    && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
                {
                    OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
                }
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public async Task KeyNotification_CanObserveSimple_ViaQueue()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys(out var prefix);
        var channel = RedisChannel.KeySpacePrefix(prefix, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.True(channel.IsPattern);
        Log($"Monitoring channel: {channel}");
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
            {
                OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public async Task KeyNotification_CanObserveSingleKey_ViaQueue()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys(out var prefix, count: 1);
        var channel = RedisChannel.KeySpaceSingleKey(keys.Single(), db.Database);
        Assert.False(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Log($"Monitoring channel: {channel}, routing via {Encoding.UTF8.GetString(channel.RoutingSpan)}");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }

        var queue = await sub.SubscribeAsync(channel);
        _ = Task.Run(async () =>
        {
            await foreach (var msg in queue.WithCancellation(CancellationToken))
            {
                callbackCount.Increment();
                if (msg.TryParseKeyNotification(out var notification)
                    && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
                {
                    OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
                }
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    private void OnNotification(
        in KeyNotification notification,
        ReadOnlySpan<byte> prefix,
        Counter matchingEventCount,
        ConcurrentDictionary<string, Counter> observedCounts,
        TaskCompletionSource<bool> allDone)
    {
        if (notification.KeyStartsWith(prefix)) // avoid problems with parallel SADD tests
        {
            int currentCount = matchingEventCount.Increment();

            // get the key and check that we expected it
            var recvKey = notification.GetKey();
            Assert.True(observedCounts.TryGetValue(recvKey.ToString(), out var counter));

#if NET9_0_OR_GREATER
            // it would be more efficient to stash the alt-lookup, but that would make our API here non-viable,
            // since we need to support multiple frameworks
            var viaAlt = FindViaAltLookup(notification, observedCounts.GetAlternateLookup<ReadOnlySpan<char>>());
            Assert.Same(counter, viaAlt);
#endif

            // accounting...
            if (counter.Increment() == 1)
            {
                Log($"Observed key: '{recvKey}' after {currentCount} events");
            }

            if (currentCount == DefaultEventCount)
            {
                allDone.TrySetResult(true);
            }
        }
    }

    private async Task SendAndObserveAsync(
        RedisKey[] keys,
        IDatabase db,
        TaskCompletionSource<bool> allDone,
        Counter callbackCount,
        ConcurrentDictionary<string, Counter> observedCounts)
    {
        await Task.Delay(300).ForAwait(); // give it a moment to settle

        Dictionary<RedisKey, Counter> sentCounts = new(keys.Length);
        foreach (var key in keys)
        {
            sentCounts[key] = new();
        }

        for (int i = 0; i < DefaultEventCount; i++)
        {
            var key = SelectKey(keys);
            sentCounts[key].Increment();
            await db.SetAddAsync(key, i);
        }

        // Wait for all events to be observed
        try
        {
            Assert.True(await allDone.Task.WithTimeout(5000));
        }
        catch (TimeoutException) when (callbackCount.Count == 0)
        {
            Assert.Fail($"Timeout with zero events; are keyspace events enabled?");
        }

        foreach (var key in keys)
        {
            Assert.Equal(sentCounts[key].Count, observedCounts[key.ToString()].Count);
        }
    }

#if NET9_0_OR_GREATER
    // demonstrate that we can use the alt-lookup APIs to avoid string allocations
    private static Counter? FindViaAltLookup(
        in KeyNotification notification,
        ConcurrentDictionary<string, Counter>.AlternateLookup<ReadOnlySpan<char>> lookup)
    {
        // Demonstrate typical alt-lookup usage; this is an advanced topic, so it
        // isn't trivial to grok, but: this is typical of perf-focused APIs.
        char[]? lease = null;
        const int MAX_STACK = 128;
        var maxLength = notification.GetKeyMaxCharCount();
        Span<char> scratch = maxLength <= MAX_STACK
            ? stackalloc char[MAX_STACK]
            : (lease = ArrayPool<char>.Shared.Rent(maxLength));
        Assert.True(notification.TryCopyKey(scratch, out var length));
        if (!lookup.TryGetValue(scratch.Slice(0, length), out var counter)) counter = null;
        if (lease is not null) ArrayPool<char>.Shared.Return(lease);
        return counter;
    }
#endif
}
