Basic Usage
===

The central object in StackExchange.Redis is the `ConnectionMultiplexer` class in the `StackExchange.Redis` namespace; this is the object that hides away the details of multiple servers. Because the `ConnectionMultiplexer` does a lot, it is designed to be **shared and reused** between callers. You should not create a `ConnectionMultiplexer` per operation. It is fully thread-safe and ready for this usage. In all the subsequent examples it will be assumed that you have a `ConnectionMultiplexer` instance stored away for re-use. But for now, let's create one. This is done using `ConnectionMultiplexer.Connect` or `ConnectionMultiplexer.ConnectAsync`, passing in either a configuration string or a `ConfigurationOptions` object. The configuration string can take the form of a comma-delimited series of nodes, so let's just connect to an instance on the local machine on the default port (6379):

```csharp
using StackExchange.Redis;
...
ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost");
// ^^^ store and re-use this!!!
```

Note that `ConnectionMultiplexer` implements `IDisposable` and can be disposed when no longer required. This is deliberately not showing `using` statement usage, because it is exceptionally rare that you would want to use a `ConnectionMultiplexer` briefly, as the idea is to re-use this object.

A more complicated scenario might involve a master/slave setup; for this usage, simply specify all the desired nodes that make up that logical redis tier (it will automatically identify the master):

```csharp
ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("server1:6379,server2:6379");
```

If it finds both nodes are masters, a tie-breaker key can optionally be specified that can be used to resolve the issue, however such a condition is fortunately very rare.

Once you have a `ConnectionMultiplexer`, there are 3 main things you might want to do:

- access a redis database (note that in the case of a cluster, a single logical database may be spread over multiple nodes)
- make use of the [pub/sub](http://redis.io/topics/pubsub) features of redis
- access an individual server for maintenance / monitoring purposes

Using a redis database
---

Accessing a redis database is as simple as:

```csharp
IDatabase db = redis.GetDatabase();
```

The object returned from `GetDatabase` is a cheap pass-thru object, and does not need to be stored. Note that redis supports multiple databases (although this is not supported on "cluster"); this can be optionally specified in the call to `GetDatabase`. Additionally, if you plan to make use of the asynchronous API and you require the [`Task.AsyncState`][2] to have a value, this can also be specified:

```csharp
int databaseNumber = ...
object asyncState = ...
IDatabase db = redis.GetDatabase(databaseNumber, asyncState);
```

Once you have the `IDatabase`, it is simply a case of using the [redis API](http://redis.io/commands). Note that all methods have both synchronous and asynchronous implementations. In line with Microsoft's naming guidance, the asynchronous methods all end `...Async(...)`, and are fully `await`-able etc.

The simplest operation would be to store and retrieve a value:

```csharp
string value = "abcdefg";
db.StringSet("mykey", value);
...
string value = db.StringGet("mykey");
Console.WriteLine(value); // writes: "abcdefg"
```

Note that the `String...` prefix here denotes the [String redis type](http://redis.io/topics/data-types), and is largely separate to the [.NET String type][3], although both can store text data. However, redis allows raw binary data for both keys and values - the usage is identical:

```csharp
byte[] key = ..., value = ...;
db.StringSet(key, value);
...
byte[] value = db.StringGet(key);
```

The entire range of [redis database commands](http://redis.io/commands) covering all redis data types is available for use.

Using redis pub/sub
----

Another common use of redis is as a [pub/sub message](http://redis.io/topics/pubsub) distribution tool; this is also simple, and in the event of connection failure, the `ConnectionMultiplexer` will handle all the details of re-subscribing to the requested channels.

```csharp
ISubscriber sub = redis.GetSubscriber();
```

Again, the object returned from `GetSubscriber` is a cheap pass-thru object that does not need to be stored. The pub/sub API has no concept of databases, but as before we can optionally provide an async-state. Note that all subscriptions are global: they are not scoped to the lifetime of the `ISubscriber` instance. The pub/sub features in redis use named "channels"; channels do not need to be defined in advance on the server (an interesting use here is things like per-user notification channels, which is what drives parts of the realtime updates on [Stack Overflow](http://stackoverflow.com)). As is common in .NET, subscriptions take the form of callback delegates which accept the channel-name and the message:

```csharp
sub.Subscribe("messages", (channel, message) => {
    Console.WriteLine((string)message);
});
```
<sub>Note: exceptions are caught and discarded by StackExchange.Redis here, to prevent cascading failures. To handle failures, use a `try`/`catch` inside your handler to do as you wish with any exceptions.</sub>

In v2, you can subscribe without providing a callback directly to the `Subscribe()` method, and instead using the returned `ChannelMessageQueue`, which represents a message queue of ordered pub/sub notifications. This allows the usage of the `ChannelMessageQueue.OnMessage()` method, which provides overloads for both synchronous (`Action<ChannelMessage>`) and asynchronous (`Func<ChannelMessage, Task>`) handlers to execute when receiving a message.

```csharp
// Synchronous handler
sub.Subscribe("messages").OnMessage(channelMessage => {
    Console.WriteLine((string) channelMessage.Message);
});

// Asynchronous handler
sub.Subscribe("messages").OnMessage(async channelMessage => {
    await Task.Delay(1000);
    Console.WriteLine((string) channelMessage.Message);
});
```

Separately (and often in a separate process on a separate machine) you can publish to this channel:

```csharp
sub.Publish("messages", "hello");
```

This will (virtually instantaneously) write `"hello"` to the console of the subscribed process. As before, both channel-names and messages can be binary.

Please also see [Pub / Sub Message Order](PubSubOrder) for guidance on sequential versus concurrent message processing.

Accessing individual servers
---

For maintenance purposes, it is sometimes necessary to issue server-specific commands:

```csharp
IServer server = redis.GetServer("localhost", 6379);
```

The `GetServer` method will accept an [`EndPoint`](http://msdn.microsoft.com/en-us/library/system.net.endpoint(v=vs.110).aspx) or the name/value pair that uniquely identify the server. As before, the object returned from `GetServer` is a cheap pass-thru object that does not need to be stored, and async-state can be optionally specified. Note that the set of available endpoints is also available:

```csharp
EndPoint[] endpoints = redis.GetEndPoints();
```

From the `IServer` instance, the [Server commands](http://redis.io/commands#server) are available; for example:

```csharp
DateTime lastSave = server.LastSave();
ClientInfo[] clients = server.ClientList();
```

Sync vs Async vs Fire-and-Forget
---

There are 3 primary usage mechanisms with StackExchange.Redis:

- Synchronous - where the operation completes before the methods returns to the caller (note that while this may block the caller, it absolutely **does not** block other threads: the key idea in StackExchange.Redis is that it aggressively shares the connection between concurrent callers)
- Asynchronous - where the operation completes some time in the future, and a `Task` or `Task<T>` is returned immediately, which can later:
    - be `.Wait()`ed (blocking the current thread until the response is available)
    - have a continuation callback added ([`ContinueWith`](http://msdn.microsoft.com/en-us/library/system.threading.tasks.task.continuewith(v=vs.110).aspx) in the TPL)
    - be *awaited* (which is a language-level feature that simplifies the latter, while also continuing immediately if the reply is already known)
- Fire-and-Forget - where you really aren't interested in the reply, and are happy to continue irrespective of the response

The synchronous usage is already shown in the examples above. This is the simplest usage, and does not involve the [TPL][1].

For asynchronous usage, the key difference is the `Async` suffix on methods, and (typically) the use of the `await` language feature. For example:

```csharp
string value = "abcdefg";
await db.StringSetAsync("mykey", value);
...
string value = await db.StringGetAsync("mykey");
Console.WriteLine(value); // writes: "abcdefg"
```

The fire-and-forget usage is accessed by the optional `CommandFlags flags` parameter on all methods (defaults to none). In this usage, the method returns the default value immediately (so a method that normally returns a `String` will always return `null`, and a method that normally returns an `Int64` will always return `0`). The operation will continue in the background. A typical use-case of this might be to increment page-view counts:

```csharp
db.StringIncrement(pageKey, flags: CommandFlags.FireAndForget);
```




  [1]: http://msdn.microsoft.com/en-us/library/dd460717%28v=vs.110%29.aspx
  [2]: http://msdn.microsoft.com/en-us/library/system.threading.tasks.task.asyncstate(v=vs.110).aspx
  [3]: http://msdn.microsoft.com/en-us/library/system.string(v=vs.110).aspx
