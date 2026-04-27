using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
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

    [Obsolete("Use Create(withChannelPrefix: false) instead", error: true)]
    private IInternalConnectionMultiplexer Create() => Create(withChannelPrefix: false);
    private IInternalConnectionMultiplexer Create(bool withChannelPrefix) =>
        Create(channelPrefix: withChannelPrefix ? "prefix:" : null);

    private RedisKey SelectKey(RedisKey[] keys) => keys[SharedRandom.Next(0, keys.Length)];

#if NET
    private static Random SharedRandom => Random.Shared;
#else
    private static Random SharedRandom { get; } = new();
#endif

    /// <summary>
    /// Creates a connection for notification tests and asserts that the required notification tokens are available.
    /// Uses Assert.SkipUnless to skip the test if the configuration is not available.
    /// </summary>
    /// <param name="kind">The kind of notification to check support for.</param>
    /// <param name="withChannelPrefix">Whether to use a channel prefix.</param>
    /// <returns>A connection multiplexer configured for the specified notification kind.</returns>
    private async Task<IInternalConnectionMultiplexer> ConnectAsync(KeyNotificationKind kind, bool withChannelPrefix = false)
    {
        var conn = Create(channelPrefix: withChannelPrefix ? "prefix:" : null, allowAdmin: true);
        var muxer = conn;

        var requiredTokens = kind switch
        {
            KeyNotificationKind.KeySpace => "AK", // A = all events, K = keyspace
            KeyNotificationKind.KeyEvent => "AE", // A = all events, E = keyevent
            KeyNotificationKind.SubKeySpace => "AS", // A = all events, S = sub-keyspace
            KeyNotificationKind.SubKeyEvent => "AT", // A = all events, T = sub-keyevent
            KeyNotificationKind.SubKeySpaceItem => "AI", // A = all events, I = sub-keyspace-item
            KeyNotificationKind.SubKeySpaceEvent => "AV", // A = all events, V = sub-keyspace-event
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported KeyNotificationKind"),
        };

        foreach (var ep in muxer.GetEndPoints())
        {
            var server = muxer.GetServer(ep);
            var config = (await server.ConfigGetAsync("notify-keyspace-events")).Single();
            var value = config.Value.ToString() ?? "";

            // Check that the config contains all required tokens
            foreach (var token in requiredTokens)
            {
                Assert.SkipUnless(value.Contains(token), $"Server {ep} notify-keyspace-events config '{value}' missing required token '{token}' for {kind}");
            }
        }

        return conn;
    }

    [Fact]
    public async Task KeySpace_CanSubscribe_ManualPublish()
    {
        await using var conn = Create(withChannelPrefix: false);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyEvent_CanObserveSimple_ViaCallbackHandler(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.KeyEvent, withChannelPrefix);
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
#pragma warning disable CS0618 // Type or member is obsolete
                && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
#pragma warning restore CS0618 // Type or member is obsolete
            {
                OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyEvent_CanObserveSimple_ViaQueue(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.KeyEvent, withChannelPrefix);
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
#pragma warning disable CS0618 // Type or member is obsolete
                    && notification is { IsKeyEvent: true, Type: KeyNotificationType.SAdd })
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
                }
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await queue.UnsubscribeAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyNotification_CanObserveSimple_ViaCallbackHandler(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.KeySpace, withChannelPrefix);
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
#pragma warning disable CS0618 // Type or member is obsolete
                    && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
                }
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyNotification_CanObserveSimple_ViaQueue(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.KeySpace, withChannelPrefix);
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
#pragma warning disable CS0618 // Type or member is obsolete
                && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
#pragma warning restore CS0618 // Type or member is obsolete
            {
                OnNotification(notification, prefix, matchingEventCount, observedCounts, allDone);
            }
        });

        await SendAndObserveAsync(keys, db, allDone, callbackCount, observedCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task KeyNotification_CanObserveSingleKey_ViaQueue(bool withChannelPrefix, bool withKeyPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.KeySpace, withChannelPrefix);
        string keyPrefix = withKeyPrefix ? "isolated:" : "";
        byte[] keyPrefixBytes = Encoding.UTF8.GetBytes(keyPrefix);
        var db = conn.GetDatabase().WithKeyPrefix(keyPrefix);

        var keys = InventKeys(out var prefix, count: 1);
        Log($"Using {Encoding.UTF8.GetString(prefix)} as filter prefix, sample key: {SelectKey(keys)}");
        var channel = RedisChannel.KeySpaceSingleKey(RedisKey.WithPrefix(keyPrefixBytes, keys.Single()), db.Database);

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
                if (msg.TryParseKeyNotification(keyPrefixBytes, out var notification)
#pragma warning disable CS0618 // Type or member is obsolete
                    && notification is { IsKeySpace: true, Type: KeyNotificationType.SAdd })
#pragma warning restore CS0618 // Type or member is obsolete
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

#if NET10_0_OR_GREATER
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

#if NET10_0_OR_GREATER
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

    // ========== Sub-Key (Hash Field) Notification Tests ==========

    /// <summary>
    /// Helper to send hash operations and observe field-level notifications.
    /// </summary>
    private async Task SendHashOperationsAndObserveAsync(
        RedisKey hashKey,
        string[] fields,
        IDatabase db,
        TaskCompletionSource<bool> allDone,
        Counter callbackCount,
        ConcurrentDictionary<string, Counter> observedFieldCounts,
        int operationCount = DefaultEventCount)
    {
        await Task.Delay(300).ForAwait(); // give it a moment to settle

        Dictionary<string, Counter> sentCounts = new(fields.Length);
        foreach (var field in fields)
        {
            sentCounts[field] = new();
        }

        for (int i = 0; i < operationCount; i++)
        {
            var field = fields[SharedRandom.Next(0, fields.Length)];
            sentCounts[field].Increment();
            await db.HashSetAsync(hashKey, field, i);
        }

        // Wait for all events to be observed
        try
        {
            Assert.True(await allDone.Task.WithTimeout(5000));
        }
        catch (TimeoutException) when (callbackCount.Count == 0)
        {
            Assert.Fail($"Timeout with zero events; are sub-keyspace events enabled?");
        }

        foreach (var field in fields)
        {
            Assert.Equal(sentCounts[field].Count, observedFieldCounts[field].Count);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubKeyEvent_CanObserveHashFields_ViaCallback(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeyEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fields = new[] { "field1", "field2", "field3", "field4", "field5" };
        var channel = RedisChannel.SubKeyEvent(KeyNotificationType.HSet, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix); // Keyspace notifications should ignore channel prefix
        Log($"Monitoring channel: {channel}");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedFieldCounts = new();
        foreach (var field in fields)
        {
            observedFieldCounts[field] = new();
        }
        // withChannelPrefix: true, "SUBSCRIBE" "__subkeyevent@0__:hset"
        // withChannelPrefix: false, "SUBSCRIBE" "__subkeyevent@0__:hset"
        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeyEvent: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeyEvent
                && notification.Type == KeyNotificationType.HSet
                && notification.GetKey() == hashKey)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var fieldName = subKey.ToString();
                    if (observedFieldCounts.TryGetValue(fieldName, out var counter))
                    {
                        int currentCount = matchingEventCount.Increment();
                        counter.Increment();
                        Log($"Observed field: '{fieldName}' after {currentCount} events");

                        if (currentCount == DefaultEventCount)
                        {
                            allDone.TrySetResult(true);
                        }
                    }
                }
            }
        });

        await SendHashOperationsAndObserveAsync(hashKey, fields, db, allDone, callbackCount, observedFieldCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubKeySpace_CanObserveHashFields_ViaPrefix(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpace, withChannelPrefix);
        var db = conn.GetDatabase();

        var prefix = $"{Guid.NewGuid()}/";
        var hashKey = $"{prefix}myhash";
        var fields = new[] { "field1", "field2", "field3" };
        var channel = RedisChannel.SubKeySpacePrefix(prefix, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.True(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix); // Keyspace notifications should ignore channel prefix
        Log($"Monitoring channel: {channel}");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedFieldCounts = new();
        foreach (var field in fields)
        {
            observedFieldCounts[field] = new();
        }

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeySpace: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpace
                && notification.Type == KeyNotificationType.HSet)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var fieldName = subKey.ToString();
                    if (observedFieldCounts.TryGetValue(fieldName, out var counter))
                    {
                        int currentCount = matchingEventCount.Increment();
                        counter.Increment();
                        Log($"Observed field: '{fieldName}' in key '{notification.GetKey()}' after {currentCount} events");

                        if (currentCount == DefaultEventCount)
                        {
                            allDone.TrySetResult(true);
                        }
                    }
                }
            }
        });

        await SendHashOperationsAndObserveAsync(hashKey, fields, db, allDone, callbackCount, observedFieldCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubKeySpaceItem_CanObserveSpecificHashField(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpaceItem, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var targetField = "field2";
        var fields = new[] { "field1", targetField, "field3" };
        var channel = RedisChannel.SubKeySpaceItem(hashKey, targetField, db.Database);
        Assert.False(channel.IsMultiNode); // Single key subscription can route to specific node
        Assert.False(channel.IsPattern);
        Log($"Monitoring channel: {channel}");

        // Use seeded random to get deterministic field selection
        int seed = SharedRandom.Next();
        var countRandom = new Random(seed);
        int expectedCount = 0;
        for (int i = 0; i < DefaultEventCount; i++)
        {
            var field = fields[countRandom.Next(0, fields.Length)];
            if (field == targetField) expectedCount++;
        }
        Log($"Using seed {seed}, expecting exactly {expectedCount} hits on '{targetField}' out of {DefaultEventCount} operations");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), targetFieldCount = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeySpaceItem: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpaceItem
                && notification.Type == KeyNotificationType.HSet)
            {
                var subKey = notification.GetSubKeys().FirstOrDefault();
                var fieldName = subKey.ToString();
                Assert.Equal(targetField, fieldName); // Should only observe the specific field
                targetFieldCount.Increment();
                Log($"Observed target field: '{fieldName}' ({targetFieldCount.Count} of exactly {expectedCount} times)");

                if (targetFieldCount.Count >= expectedCount)
                {
                    allDone.TrySetResult(true);
                }
            }
        });

        // Verify subscription is active by doing a test operation on a DIFFERENT field
        // This ensures subscription is ready without affecting the target field count
        Log("Verifying subscription is active...");
        var testField = "test_field_verify";
        await db.HashSetAsync(hashKey, testField, "test");
        await Task.Delay(300).ForAwait(); // Give subscription time to activate

        Log($"Subscription verified. Starting {DefaultEventCount} HSET operations, expecting exactly {expectedCount} notifications for '{targetField}'");

        // Set various fields using the same seeded random, but only targetField should trigger notifications
        var operationRandom = new Random(seed);
        for (int i = 0; i < DefaultEventCount; i++)
        {
            var field = fields[operationRandom.Next(0, fields.Length)];
            await db.HashSetAsync(hashKey, field, i);
        }

        Log($"Completed all HSET operations, waiting for notifications...");
        Assert.True(await allDone.Task.WithTimeout(5000), $"Timed out waiting for notifications. Received {targetFieldCount.Count} of expected {expectedCount}");
        Assert.Equal(expectedCount, targetFieldCount.Count); // Exact match since we used seeded random
        Log($"Test completed successfully with {targetFieldCount.Count} notifications (exactly as expected)");

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubKeySpaceEvent_CanObserveHashFields_SingleKey_SpecificEvent(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpaceEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fields = new[] { "field1", "field2", "field3" };
        var channel = RedisChannel.SubKeySpaceEvent(KeyNotificationType.HSet, hashKey, db.Database);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Log($"Monitoring channel: {channel}");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new(), matchingEventCount = new();
        TaskCompletionSource<bool> allDone = new();

        ConcurrentDictionary<string, Counter> observedFieldCounts = new();
        foreach (var field in fields)
        {
            observedFieldCounts[field] = new();
        }

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeySpaceEvent: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpaceEvent
                && notification.Type == KeyNotificationType.HSet)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var fieldName = subKey.ToString();
                    if (observedFieldCounts.TryGetValue(fieldName, out var counter))
                    {
                        int currentCount = matchingEventCount.Increment();
                        counter.Increment();
                        Log($"Observed field: '{fieldName}' after {currentCount} events");

                        if (currentCount == DefaultEventCount)
                        {
                            allDone.TrySetResult(true);
                        }
                    }
                }
            }
        });

        await SendHashOperationsAndObserveAsync(hashKey, fields, db, allDone, callbackCount, observedFieldCounts);
        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public async Task SubKeyEvent_MultipleFields_SingleOperation()
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeyEvent);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var channel = RedisChannel.SubKeyEvent(KeyNotificationType.HSet, db.Database);
        Log($"Monitoring channel: {channel}");

        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);
        Counter callbackCount = new();
        TaskCompletionSource<bool> allDone = new();

        HashSet<string> observedFields = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeyEvent
                && notification.Type == KeyNotificationType.HSet
                && notification.GetKey() == hashKey)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    observedFields.Add(subKey.ToString());
                }

                if (observedFields.Count >= 3)
                {
                    allDone.TrySetResult(true);
                }
            }
        });

        await Task.Delay(300).ForAwait(); // give it a moment to settle

        // Set multiple fields in a single operation
        await db.HashSetAsync(hashKey, new HashEntry[]
        {
            new("field1", "value1"),
            new("field2", "value2"),
            new("field3", "value3"),
        });

        Assert.True(await allDone.Task.WithTimeout(5000));
        Assert.Contains("field1", observedFields);
        Assert.Contains("field2", observedFields);
        Assert.Contains("field3", observedFields);

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeyEvent_HandlesNewlineInKey(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeyEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/key\nwith\nnewlines";
        var fields = new[] { "field1", "field2" };
        var channel = RedisChannel.SubKeyEvent(KeyNotificationType.HSet, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        Counter callbackCount = new();
        HashSet<string> observedKeys = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeyEvent_HandlesNewlineInKey: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeyEvent
                && notification.Type == KeyNotificationType.HSet)
            {
                var key = notification.GetKey().ToString();
                Log($"  Parsed key: '{key}'");
                observedKeys.Add(key!);
                if (key == hashKey) allDone.TrySetResult(true);
            }
        });

        await db.HashSetAsync(hashKey, fields[0], "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for key with newlines");
        Assert.Contains(hashKey, observedKeys);

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeyEvent_HandlesNewlineInField(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeyEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fieldWithNewline = "field\nwith\nnewlines";
        var channel = RedisChannel.SubKeyEvent(KeyNotificationType.HSet, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        Counter callbackCount = new();
        HashSet<string> observedFields = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            callbackCount.Increment();
            Log($"SubKeyEvent_HandlesNewlineInField: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeyEvent
                && notification.Type == KeyNotificationType.HSet
                && notification.GetKey() == hashKey)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var field = subKey.ToString();
                    Log($"  Parsed field: '{field}'");
                    observedFields.Add(field!);
                    if (field == fieldWithNewline) allDone.TrySetResult(true);
                }
            }
        });

        await db.HashSetAsync(hashKey, fieldWithNewline, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for field with newlines");
        Assert.Contains(fieldWithNewline, observedFields);

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeySpace_HandlesNewlineInKey(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpace, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/key\nwith\nnewlines";
        var field = "field1";
        var channel = RedisChannel.SubKeySpaceSingleKey(hashKey, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        Counter receivedCount = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            receivedCount.Increment();
            Log($"SubKeySpace_HandlesNewlineInKey: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpace
                && notification.Type == KeyNotificationType.HSet)
            {
                Log($"  Parsed successfully, Type={notification.Type}");
                allDone.TrySetResult(true);
            }
        });

        await db.HashSetAsync(hashKey, field, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for key with newlines");

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeySpace_HandlesNewlineInField(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpace, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fieldWithNewline = "field\nwith\nnewlines";
        var channel = RedisChannel.SubKeySpaceSingleKey(hashKey, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        HashSet<string> observedFields = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            Log($"SubKeySpace_HandlesNewlineInField: Received on '{recvChannel}', value: '{recvValue}'");
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpace
                && notification.Type == KeyNotificationType.HSet)
            {
                Log($"  Parsed successfully, Type={notification.Type}, Key='{notification.GetKey()}'");
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var field = subKey.ToString();
                    Log($"  Parsed field: '{field}'");
                    observedFields.Add(field!);
                    if (field == fieldWithNewline) allDone.TrySetResult(true);
                }
            }
        });

        await db.HashSetAsync(hashKey, fieldWithNewline, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for field with newlines");
        Assert.Contains(fieldWithNewline, observedFields);

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeySpaceEvent_HandlesNewlineInKey(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpaceEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/key\nwith\nnewlines";
        var field = "field1";
        var channel = RedisChannel.SubKeySpaceEvent(KeyNotificationType.HSet, hashKey, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        Counter receivedCount = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            receivedCount.Increment();
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpaceEvent
                && notification.Type == KeyNotificationType.HSet)
            {
                allDone.TrySetResult(true);
            }
        });

        await db.HashSetAsync(hashKey, field, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for key with newlines");

        await sub.UnsubscribeAsync(channel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubKeySpaceEvent_HandlesNewlineInField(bool withChannelPrefix)
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpaceEvent, withChannelPrefix);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fieldWithNewline = "field\nwith\nnewlines";
        var channel = RedisChannel.SubKeySpaceEvent(KeyNotificationType.HSet, hashKey, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        HashSet<string> observedFields = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpaceEvent
                && notification.Type == KeyNotificationType.HSet)
            {
                var subKeys = notification.GetSubKeys();
                foreach (var subKey in subKeys)
                {
                    var field = subKey.ToString();
                    observedFields.Add(field!);
                    if (field == fieldWithNewline) allDone.TrySetResult(true);
                }
            }
        });

        await db.HashSetAsync(hashKey, fieldWithNewline, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for field with newlines");
        Assert.Contains(fieldWithNewline, observedFields);

        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public async Task SubKeySpaceItem_HandlesNewlineInField()
    {
        await using var conn = await ConnectAsync(KeyNotificationKind.SubKeySpaceItem, withChannelPrefix: false);
        var db = conn.GetDatabase();

        var hashKey = $"{Guid.NewGuid()}/hash";
        var fieldWithNewline = "field\nwith\nnewlines";
        var channel = RedisChannel.SubKeySpaceItem(hashKey, fieldWithNewline, db.Database);
        var sub = conn.GetSubscriber();
        await sub.UnsubscribeAsync(channel);

        Counter receivedCount = new();
        TaskCompletionSource<bool> allDone = new();

        await sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
        {
            receivedCount.Increment();
            if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification)
                && notification.Kind == KeyNotificationKind.SubKeySpaceItem
                && notification.Type == KeyNotificationType.HSet)
            {
                var subKey = notification.GetSubKeys().FirstOrDefault();
                if (subKey.ToString() == fieldWithNewline)
                {
                    allDone.TrySetResult(true);
                }
            }
        });

        await db.HashSetAsync(hashKey, fieldWithNewline, "value1");

        Assert.True(await allDone.Task.WithTimeout(5000), "Did not receive notification for field with newlines");

        await sub.UnsubscribeAsync(channel);
    }

    [Fact]
    public void SubKeySpaceItem_RejectsKeyWithNewline()
    {
        var keyWithNewline = (RedisKey)"key\nwith\nnewlines";
        var field = (RedisKey)"field1";

        var ex = Assert.Throws<ArgumentException>(() =>
            RedisChannel.SubKeySpaceItem(keyWithNewline, field, 0));

        Assert.Contains("newline", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void SubKeySpaceEvent_RejectsEventWithPipe()
    {
        byte[] eventWithPipe = "event|with|pipes"u8.ToArray();
        var key = (RedisKey)"mykey";

        var ex = Assert.Throws<ArgumentException>(() =>
            RedisChannel.SubKeySpaceEvent(eventWithPipe, key, 0));

        Assert.Contains("pipe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("type", ex.ParamName);
    }
}
