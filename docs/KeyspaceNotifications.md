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
    if (KeyNotification.TryParse(recvChannel, recvValue, out var notification))
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

## Considerations when database isolation

Database isolation is controlled either via the `ConfigurationOptions.DefaultDatabase` option when connecting to Redis,
or by using the `GetDatabase(int? db = null)` method to get a specific database instance. Note that the
`KeySpace...` and `KeyEvent...` APIs may optionally take a database. When a database is specified, subscription will only
respond to notifications for keys in that database. If a database is not specified, the subscription will respond to
notifications for keys in all databases. Often, you will want to pass `db.Database` from the `IDatabase` instance you are
using for your application logic, to ensure that you are monitoring the correct database. When using Redis Cluster,
this usually means database `0`, since Redis Cluster does not usually support multiple databases.

For example:

- `RedisChannel.KeySpaceSingleKey("foo", 0)` maps to `SUBSCRIBE __keyspace@0__:foo`
- `RedisChannel.KeySpacePrefix("foo", 0)` maps to `PSUBSCRIBE __keyspace@0__:foo*`
- `RedisChannel.KeySpacePrefix("foo")` maps to `PSUBSCRIBE __keyspace@*__:foo*`
- `RedisChannel.KeyEvent(KeyNotificationType.Set, 0)` maps to `SUBSCRIBE __keyevent@0__:set`
- `RedisChannel.KeyEvent(KeyNotificationType.Set)` maps to `PSUBSCRIBE __keyevent@*__:set`

Additionally, note that while most of these examples require multi-node subscriptions on Redis Cluster, `KeySpaceSingleKey`
is an exception, and will only subscribe to the single node that owns the key `foo`.

## Considerations when using keyspace or channel isolation

StackExchange.Redis supports the concept of keyspace and channel (pub/sub) isolation.

Channel isolation is controlled using the `ConfigurationOptions.ChannelPrefix` option when connecting to Redis.
Intentionally, this feature *is ignored* by the `KeySpace...` and `KeyEvent...` APIs, because they are designed to
subscribe to specific (server-defined) channels that are outside the control of the client.

Keyspace isolation is controlled using the `WithKeyPrefix` extension method on `IDatabase`. This is *not* used
by the `KeySpace...` and `KeyEvent...` APIs. Since the database and pub/sub APIs are independent, keyspace isolation
*is not applied* (and cannot be; consuming code could have zero, one, or multiple databases with different prefixes).
The caller is responsible for ensuring that the prefix is applied appropriately when constructing the `RedisChannel`.

By default, key-related featured of `KeyNotification` will return the full key reported by the server,
including any prefix. However, the `TryParseKeyNotification` and `TryParse` methods can optionally be passed a
key prefix, which will be used both to filter unwanted notifications and strip the prefix from the key when reading.
It is *possible* to handle keyspace isolation manually by checking the key with `KeyNotification.KeyStartsWith` and
manually trimming the prefix, but it is *recommended* to do this via `TryParseKeyNotification` and `TryParse`.

As an example, with a multi-tenant scenario using keyspace isolation, we might have in the database code:

``` c#
// multi-tenant scenario using keyspace isolation
var db = conn.GetDatabase().WithKeyPrefix("client1234:");

// we will later commit order data for example:
await db.StringSetAsync("order/123", "ISBN 9789123684434");
```

To observe this, we could use:

``` c#

var sub = conn.GetSubscriber();
 
// we could subscribe to the specific client as a prefix:
var channel = RedisChannel.KeySpacePrefix("client1234:order/", db.Database);

byte[] prefix = Encoding.UTF8.GetBytes("client1234:");
sub.SubscribeAsync(channel, (channel, value) =>
{
    // by including prefix in the TryParse, we filter out notifications that are not for this client
    // *and* the key is sliced internally to remove this prefix when reading
    if (KeyNotification.TryParse(prefix, channel, value, out var notification))
    {
        // if we get here, the key prefix was a match
        var key = notification.GetKey(); // will *not* include the "client1234:" prefix
    }
});

```

Alternatively, if we wanted a single handler that observed all clients, we could use:

``` c#
var channel = RedisChannel.KeySpacePattern("client*:order/*", db.Database);
```

with similar code, parsing the client from the key manually, using the full key length.