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

**From Redis 8.8**, you can also optionally enable sub-key (hash field) notifications, using additional tokens:

``` conf
notify-keyspace-events AKESTIV
```

- **S** - SubKeySpace notifications (`__subkeyspace@<db>__:<key>`)
- **T** - SubKeyEvent notifications (`__subkeyevent@<db>__:<event>`)
- **I** - SubKeySpaceItem notifications (`__subkeyspaceitem@<db>__:<key>\n<subkey>`)
- **V** - SubKeySpaceEvent notifications (`__subkeyspaceevent@<db>__:<event>|<key>`)

These sub-key notification types allow you to monitor operations on hash fields (subkeys) in addition to key-level operations.
The different formats provide the same information but organized differently, and StackExchange.Redis provides a unified API
via the same `KeyNotification` type.

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

**From Redis 8.8**, there are corresponding `SubKeySpace...` and `SubKeyEvent...` methods for sub-key (hash field) notifications:

- `SubKeySpaceSingleKey` - subscribe to sub-key notifications for a single key in a specific database
- `SubKeySpacePattern` - subscribe to sub-key notifications for a key pattern, optionally in a specific database
- `SubKeySpacePrefix` - subscribe to sub-key notifications for all keys with a specific prefix, optionally in a specific database
- `SubKeySpaceItem` - subscribe to sub-key notifications for a specific key and field combination in a specific database
- `SubKeyEvent` - subscribe to sub-key notifications for a specific event type, optionally in a specific database
- `SubKeySpaceEvent` - subscribe to sub-key notifications for a specific event type and key, optionally in a specific database

These work similarly to their key-level counterparts, but monitor hash field operations instead of key operations.

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
        Console.WriteLine($"Kind: {notification.Kind}");

        // For sub-key notifications (Redis 8.8+), you can access sub-keys in a uniform way,
        // regardless of the notification type
        if (notification.HasSubKey)
        {
            // Get the first sub-key
            Console.WriteLine($"First SubKey: {notification.GetSubKeys().First()}");

            // Or iterate all sub-keys (for notifications with multiple fields)
            foreach (var subKey in notification.GetSubKeys())
            {
                Console.WriteLine($"SubKey: {subKey}");
            }
        }
    }
});
```

Note that the channels created by the `KeySpace...` and `KeyEvent...` methods cannot be used to manually *publish* events,
only to subscribe to them. The events are published automatically by the Redis server when keys are modified. If you
want to simulate keyspace notifications by publishing events manually, you should use regular pub/sub channels that avoid
the `__keyspace@` and `__keyevent@` prefixes (and similarly for sub-key events).

## Performance considerations for KeyNotification

The `KeyNotification` struct provides parsed notification data, including (as already shown) the key, event type,
database, kind, etc. Note that using `GetKey()` will allocate a copy of the key bytes; to avoid allocations,
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

## Working with Sub-Key (Hash Field) Notifications

**From Redis 8.8**, Redis supports notifications for hash field (sub-key) operations. These notifications provide
more granular monitoring of hash operations, allowing you to observe changes to individual hash fields rather than
just key-level operations.

### Understanding Sub-Key Notification Types

There are four sub-key notification kinds, analogous to the two key-level notification kinds:

- **SubKeySpace** (`__subkeyspace@<db>__:<key>`) - Notifications for a specific hash key, with the event type and sub-key in the payload
- **SubKeyEvent** (`__subkeyevent@<db>__:<event>`) - Notifications for a specific event type, with the key and sub-key in the payload
- **SubKeySpaceItem** (`__subkeyspaceitem@<db>__:<key>\n<subkey>`) - Notifications for a specific hash key and field combination
- **SubKeySpaceEvent** (`__subkeyspaceevent@<db>__:<event>|<key>`) - Notifications for a specific event and key, with the sub-key in the payload

In most cases, the application code already knows the kind of event being consumed, but if that logic is centralized,
you can determine the notification family using  the `notification.Kind` property (which returns a
`KeyNotificationKind` enum value), and optionally extract sub-keys using `notification.GetSubKeys()`.

### Example: Monitoring Hash Field Changes

```csharp
// Subscribe to all sub-key changes for hashes with prefix "user:"
var channel = RedisChannel.SubKeySpacePrefix("user:", database: 0);

sub.Subscribe(channel, (recvChannel, recvValue) =>
{
    if (KeyNotification.TryParse(recvChannel, recvValue, out var notification))
    {
        Console.WriteLine($"Hash Key: {notification.GetKey()}");
        Console.WriteLine($"Operation: {notification.Type}");
        Console.WriteLine($"Kind: {notification.Kind}");

        // Process all affected fields
        foreach (var field in notification.GetSubKeys())
        {
            Console.WriteLine($"Field: {field}");
        }

        // Or get just the first field for single-field operations
        var firstField = notification.GetSubKeys().FirstOrDefault();

        // Utility methods available:
        // - Count() - get the number of fields
        // - First() / FirstOrDefault() - get the first field
        // - Single() / SingleOrDefault() - get the only field (throws if multiple)
        // - ToArray() / ToList() - convert to collection
        // - CopyTo(Span<RedisValue>) - copy to a span (allocation-free)
    }
});

// Or subscribe to specific hash field events (e.g., HSET operations)
var eventChannel = RedisChannel.SubKeyEvent(KeyNotificationType.HSet, database: 0);
```

### Sub-Key and Key Prefix Filtering

When using key-prefix filtering with sub-key notifications, the prefix is applied to the **key** only, not to the
sub-key (hash field). The sub-key is always returned as-is from the notification, without any prefix stripping.

## Considerations when using database isolation

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

**From Redis 8.8**, the sub-key notification methods work similarly:

- `RedisChannel.SubKeySpaceSingleKey("myhash", 0)` maps to `SUBSCRIBE __subkeyspace@0__:myhash`
- `RedisChannel.SubKeySpacePrefix("hash:", 0)` maps to `PSUBSCRIBE __subkeyspace@0__:hash:*`
- `RedisChannel.SubKeySpaceItem("myhash", "field1", 0)` maps to `SUBSCRIBE __subkeyspaceitem@0__:myhash\nfield1`
- `RedisChannel.SubKeyEvent(KeyNotificationType.HSet, 0)` maps to `SUBSCRIBE __subkeyevent@0__:hset`
- `RedisChannel.SubKeySpaceEvent(KeyNotificationType.HSet, "myhash", 0)` maps to `SUBSCRIBE __subkeyspaceevent@0__:hset|myhash`

Additionally, note that while most of these examples require multi-node subscriptions on Redis Cluster, `KeySpaceSingleKey`
is an exception, and will only subscribe to the single node that owns the key `foo`.

When subscribing without specifying a database (i.e. listening to changes in all database), the database relating
to the notification can be fetched via `KeyNotification.Database`:

``` c#
var channel = RedisChannel.KeySpacePrefix("foo");
sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
{
    if (KeyNotification.TryParse(recvChannel, recvValue, out var notification))
    {
        var key = notification.GetKey();
        var db = notification.Database;
        // ...
    }
}
```

## Considerations when using keyspace or channel isolation

StackExchange.Redis supports the concept of keyspace and channel (pub/sub) isolation.

Channel isolation is controlled using the `ConfigurationOptions.ChannelPrefix` option when connecting to Redis.
Intentionally, this feature *is ignored* by the `KeySpace...` and `KeyEvent...` APIs, because they are designed to
subscribe to specific (server-defined) channels that are outside the control of the client.

Keyspace isolation is controlled using the `WithKeyPrefix` extension method on `IDatabase`. This is *not* used
by the `KeySpace...` and `KeyEvent...` APIs. Since the database and pub/sub APIs are independent, keyspace isolation
*is not applied* (and cannot be; consuming code could have zero, one, or multiple databases with different prefixes).
The caller is responsible for ensuring that the prefix is applied appropriately when constructing the `RedisChannel`.

By default, key-related features of `KeyNotification` will return the full key reported by the server,
including any prefix. However, the `TryParseKeyNotification` and `TryParse` methods can optionally be passed a
key prefix, which will be used both to filter unwanted notifications and strip the prefix from the key when reading.
It is *possible* to handle keyspace isolation manually by checking the key with `KeyNotification.KeyStartsWith` and
manually trimming the prefix, but it is *recommended* to do this via `TryParseKeyNotification` and `TryParse`.

As an example, with a multi-tenant scenario using keyspace isolation, we might have in the database code:

``` c#
// multi-tenant scenario using keyspace isolation
byte[] keyPrefix = Encoding.UTF8.GetBytes("client1234:");
var db = conn.GetDatabase().WithKeyPrefix(keyPrefix);

// we will later commit order data for example:
await db.StringSetAsync("order/123", "ISBN 9789123684434");
```

To observe this, we could use:

``` c#
var sub = conn.GetSubscriber();

// subscribe to the specific tenant as a prefix:
var channel = RedisChannel.KeySpacePrefix("client1234:order/", db.Database);

sub.SubscribeAsync(channel, (recvChannel, recvValue) =>
{
    // by including prefix in the TryParse, we filter out notifications that are not for this client
    // *and* the key is sliced internally to remove this prefix when reading
    if (KeyNotification.TryParse(keyPrefix, recvChannel, recvValue, out var notification))
    {
        // if we get here, the key prefix was a match
        var key = notification.GetKey(); // "order/123" - note no prefix
        // ...
    }

    /*
    // for contrast only: this is *not* usually the recommended approach when using keyspace isolation
    if (KeyNotification.TryParse(recvChannel, recvValue, out var notification)
        && notification.KeyStartsWith(keyPrefix))
    {
        var key = notification.GetKey(); // "client1234:order/123" - note prefix is included
        // ...
    }
    */
});

```

Alternatively, if we wanted a single handler that observed *all* tenants, we could use:

``` c#
var channel = RedisChannel.KeySpacePattern("client*:order/*", db.Database);
```

with similar code, parsing the client from the key manually, using the full key length.