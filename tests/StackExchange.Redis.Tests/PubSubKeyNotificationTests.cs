using System;
using System.Collections.Generic;
using System.Linq;
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

    private RedisKey[] InventKeys(int count = DefaultKeyCount)
    {
        RedisKey[] keys = new RedisKey[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = Guid.NewGuid().ToString();
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

    [Fact]
    public async Task KeyEvent_CanObserveSimple_ViaCallbackHandler()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        var keys = InventKeys();
        var channel = RedisChannel.KeyEvent(KeyNotificationType.SAdd);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        HashSet<RedisKey> observedKeys = [];
        int count = 0, callbackCount = 0;
        TaskCompletionSource<bool> allDone = new();
        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            Interlocked.Increment(ref callbackCount);
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
            {
                var recvKey = notification.GetKey();
                lock (observedKeys)
                {
                    int currentCount = ++count;
                    var newKey = observedKeys.Add(recvKey);
                    if (newKey)
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

        HashSet<RedisKey> sentKeys = new(keys.Length);
        for (int i = 0; i < DefaultEventCount; i++)
        {
            var key = SelectKey(keys);
            await db.SetAddAsync(key, i);
            sentKeys.Add(key); // just in case Random has a bad day (obvious Dilbert link is obvious)
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

        lock (observedKeys)
        {
            Assert.Equal(sentKeys.Count, observedKeys.Count);
            foreach (var key in sentKeys)
            {
                Assert.Contains(key, observedKeys);
            }
        }
    }
}
