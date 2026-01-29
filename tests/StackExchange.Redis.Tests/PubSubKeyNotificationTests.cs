using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public sealed class PubSubKeyNotificationTestsCluster(ITestOutputHelper output, SharedConnectionFixture fixture)
    : PubSubKeyNotificationTests(output, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts;
}

public sealed class PubSubKeyNotificationTestsStandalone(ITestOutputHelper output, SharedConnectionFixture fixture)
    : PubSubKeyNotificationTests(output, fixture)
{
}

public abstract class PubSubKeyNotificationTests(ITestOutputHelper output, SharedConnectionFixture? fixture = null)
    : TestBase(output, fixture)
{
    private const int DefaultKeyCount = 10;
    private const int DefaultEventCount = 512;

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
        var channel = RedisChannel.KeyEvent(KeyNotificationType.SAdd);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        int count = 0, callbackCount = 0;
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedCounts = new();
        foreach (var key in keys)
        {
            observedCounts[key.ToString()] = new();
        }
#if NET9_0_OR_GREATER
        // demonstrate that we can use the alt-lookup APIs to avoid string allocations
        var altLookup = observedCounts.GetAlternateLookup<ReadOnlySpan<char>>();
        static Counter? FindViaAltLookup(
            in KeyNotification notification,
            ConcurrentDictionary<string, Counter>.AlternateLookup<ReadOnlySpan<char>> lookup)
        {
            Span<char> scratch = stackalloc char[1024];
            notification.TryCopyKey(scratch, out var bytesWritten);
            return lookup.TryGetValue(scratch.Slice(0, bytesWritten), out var counter)
                ? counter
                : null;
        }
#endif

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            Interlocked.Increment(ref callbackCount);
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
            {
                if (notification.KeyStartsWith(prefix)) // avoid problems with parallel SADD tests
                {
                    int currentCount = Interlocked.Increment(ref count);

                    // get the key and check that we expected it
                    var recvKey = notification.GetKey();
                    Assert.True(observedCounts.TryGetValue(recvKey.ToString(), out var counter));

#if NET9_0_OR_GREATER
                    var viaAlt = FindViaAltLookup(notification, altLookup);
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
        });

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
        catch (TimeoutException ex)
        {
            // if this is zero, the real problem is probably ala KeySpace_Events_Enabled
            throw new TimeoutException($"Timeout; {Volatile.Read(ref callbackCount)} events observed", ex);
        }

        foreach (var key in keys)
        {
            Assert.Equal(sentCounts[key].Count, observedCounts[key.ToString()].Count);
        }
    }
}
