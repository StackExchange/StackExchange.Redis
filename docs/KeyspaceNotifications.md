# Redis Keyspace Notifications

Redis keyspace notifications let you monitor operations happening on your Redis keys in real-time. StackExchange.Redis provides a strongly-typed API for subscribing to and consuming these events.
This could be used for example to implement a cache invalidation strategy.

## Prerequisites

### Redis Configuration

You must [enable keyspace notifications](https://redis.io/docs/latest/develop/pubsub/keyspace-notifications/#configuration) in your Redis server config,
for example:

``` conf
notify-keyspace-events AKE
```

- **A** - All event types
- **K** - Keyspace notifications (`__keyspace@<db>__:<key>`)
- **E** - Keyevent notifications (`__keyevent@<db>__:<event>`)

The two types of event (keyspace and keyevent) encode the same information, but in different formats.
To simplify consumption, StackExchange.Redis provides a unified API for both types of event, via the `KeyNotification` type.

### Event Broadcasting in Redis Cluster

Importantly, in Redis Cluster, keyspace notifications are **not** broadcast to all nodes - they are only received by clients connecting to the
individual node where the keyspace notification originated, i.e. where the key was modified.
This is different to how regular pub/sub events are handled, where a subscription to a channel on one node will receive events published on any node.
Clients must explicitly subscribe to the same channel on each node they wish to receive events from, which typically means: every primary node in the cluster.
To make this easier, StackExchange.Redis provides dedicated APIs for subscribing to keyspace and keyevent notifications that handle this for you.

## Quick Start

As an example, we'll subscribe to all keys with a specific prefix, and print out the key and event type for each notification. First,
we need to create a `RedisChannel`: 

```csharp
// this will subscribe to __keyspace@0__:user:*, including supporting Redis Cluster
var channel = RedisChannel.KeySpacePrefix(prefix: "user:"u8, database: 0);
```

Note that there are a range of other `KeySpace...` and `KeyEvent...` methods for different scenarios, including:

- `KeySpaceSingleKey` - subscribe to notifications for a single key in a specific database
- `KeySpacePattern` - subscribe to notifications for a key pattern, optionally in a specific database
- `KeySpacePrefix` - subscribe to notifications for all keys with a specific prefix, optionally in a specific database
- `KeyEvent` - subscribe to notifications for a specific event type, optionally in a specific database

The `KeySpace*` methods are similar, and are presented separately to make the intent clear. For example, `KeySpacePattern("foo*")` is equivalent to `KeySpacePrefix("foo")`, and will subscribe to all keys beginning with `"foo"`.

Next, we subscribe to the channel and process the notifications using the normal pub/sub subscription API; there are two
main approaches: queue-based and callback-based.

Queue-based:

```csharp
var queue = await sub.SubscribeAsync(channel);
_ = Task.Run(async () =>
{
    await foreach (var msg in queue)
    {
        if (msg.TryParseKeyNotification(out var notification))
        {
            Console.WriteLine($"Key: {notification.GetKey()}");
            Console.WriteLine($"Type: {notification.Type}");
            Console.WriteLine($"Database: {notification.Database}");
        }
    }
});
```

Callback-based:

```csharp
sub.Subscribe(channel, (recvChannel, recvValue) =>
{
    if (KeyNotification.TryParse(in recvChannel, in recvValue, out var notification))
    {
        Console.WriteLine($"Key: {notification.GetKey()}");
        Console.WriteLine($"Type: {notification.Type}");
        Console.WriteLine($"Database: {notification.Database}");
    }
});
```

Note that the channels created by the `KeySpace...` and `KeyEvent...` methods cannot be used to manually *publish* events,
only to subscribe to them. The events are published automatically by the Redis server when keys are modified. If you
want to simulate keyspace notifications by publishing events manually, you should use regular pub/sub channels that avoid
the `__keyspace@` and `__keyevent@` prefixes.

## Performance considerations for KeyNotification

The `KeyNotification` struct provides parsed notification data, including (as already shown) the key, event type,
database, etc. Note that using `GetKey()` will allocate a copy of the key bytes; to avoid allocations,
you can use `TryCopyKey()` to copy the key bytes into a provided buffer (potentially with `GetKeyByteCount()`,
`GetKeyMaxCharCount()`, etc in order to size the buffer appropriately). Similarly, `KeyStartsWith()` can be used to
efficiently check the key prefix without allocating a string. This approach is designed to be efficient for high-volume
notification processing, and in particular: for use with the alt-lookup (span) APIs that are slowly being introduced
in various .NET APIs.

For example, with a `ConcurrentDictionary<string, T>` (for some `T`), you can use `GetAlternateLookup<ReadOnlySpan<char>>()`
to get an alternate lookup API that takes a `ReadOnlySpan<char>` instead of a `string`, and then use `TryCopyKey()` to copy
the key bytes into a buffer, and then use the alt-lookup API to find the value. This means that we avoid allocating a string
for the key entirely, and instead just copy the bytes into a buffer. If we consider that commonly a local cache will *not*
contain the key for the majority of notifications (since they are for cache invalidation), this can be a significant
performance win.

## Considerations when using keyspace or channel isolation

StackExchange.Redis supports the concept of keyspace and channel (pub/sub) isolation.

Channel isolation is controlled using the `ConfigurationOptioons.ChannelPrefix` option when connecting to Redis. Intentionally, this feature
*is ignored* by the `KeySpace...` and `KeyEvent...` APIs, because they are designed to subscribe to specific channels
that are outside of the control of the client.

Keyspace isolation is controlled using the `WithKeyPrefix` extension method on `IDatabase`. This is *not* ignored
by the `KeySpace...` and `KeyEvent...` APIs. Since the database and pub/sub APIs are independent, keyspace isolation
*is not applied*. The caller is responsible for ensuring that the prefix is applied consistently when constructing
the `RedisChannel`, and note that when using the `GetKey()` etc features; the key returned will represent the full key,
including any prefix. Consequently, when using keyspace isolation, you should ensure that your notification processing
takes the prefix into account. The `KeyStartsWith` method can be used to efficiently filter out notifications that do not
have the prefix, and then you can slice the retrieved key accordingly.
