Overview
===

The [Stream](https://redis.io/topics/streams-intro) data type was added in Redis version 5.0 and it represents an append-only log of messages. All of the [stream related commands](https://redis.io/commands#stream) documented on redis.io have been implemented in the StackExchange.Redis client library. Read the ["Introduction to Redis Streams"](https://redis.io/topics/streams-intro) article for further information on the raw Redis commands and how to work with streams.

> **A note on the API used here.** From 4.0, commands are grouped by data type - `db.Streams.AddAsync(...)` rather than `db.StreamAdd(...)`; see [The original `IDatabase` API](LegacyApi). This page uses the grouped spelling throughout. The `IDatabase` members (`StreamAdd`, `StreamRead`, etc) are unchanged and remain fully supported; both spellings are the same connection and can be mixed freely.
>
> The one difference worth knowing about is the shape of the replies. Where `IDatabase.StreamRange` returns a `StreamEntry[]`, `db.Streams.RangeAsync` (and likewise `ReadAsync`, `ReadGroupAsync`, `ClaimAsync`, `PendingAsync` and `PendingMessagesAsync`) answers a reply object whose entries are windows over the buffer they arrived in, rather than allocating an array per entry. On a thousand-entry read that measured at ~2,100x less memory. The price is that the reply must be disposed (hence the `using` in the examples below), and nothing read from it outlives it; call `ToArray()` on the reply to get the old array shape, or `.AsRedisValue()` on an individual value to keep it.

Writing to Streams
===

Each message or entry in the stream is represented by the `StreamEntry` type. Each stream entry contains a unique ID and an array of name/value pairs. The name/value pairs are represented by the `NameValueEntry` type.

Use the following to add a simple message with a single name/value pair to a stream:

```csharp
IDatabase db = redis.GetDatabase();
var messageId = await db.Streams.AddAsync("events_stream", "foo_name", "bar_value");
// messageId = 1518951480106-0
```

The message ID returned by `AddAsync` is comprised of the millisecond time when the message was added to the stream and a sequence number. The sequence number is used to prevent ID collisions if two or more messages were created at the same millisecond time.

Multiple name/value pairs can be written to a stream using the following:

```csharp
var values = new NameValueEntry[]
{
    new NameValueEntry("sensor_id", "1234"),
    new NameValueEntry("temp", "19.8")
};

IDatabase db = redis.GetDatabase();
var messageId = await db.Streams.AddAsync("sensor_stream", values);
```

You also have the option to override the auto-generated message ID by passing your own ID to the `AddAsync` method, via `StreamAddOptions`. Other options allow you to trim the stream's length.

```csharp
await db.Streams.AddAsync("events_stream", "foo_name", "bar_value",
    new StreamAddOptions { MessageId = "0-1", MaxLength = 100 });
```

Trimming, the entry ID and the other `XADD` options can be supplied together:

```csharp
// append only if the stream already exists, trimming anything older than the given entry ID
var id = await db.Streams.AddAsync("events_stream", "foo_name", "bar_value", new StreamAddOptions
{
    CreateStream = false,       // NOMKSTREAM
    MinId = "1526919030474-55", // or MaxLength, for MAXLEN
    Approximate = true,         // the "~" form, which is cheaper
});
```

With `CreateStream = false`, adding to a stream that does not exist returns `RedisValue.Null` and the key is
not created. `MinId` and `Approximate` require server version 6.2 or above.

Idempotent write-at-most-once production
===

From Redis 8.6, streams support idempotent write-at-most-once production. This is achieved by passing a `StreamIdempotentId` to the `AddAsync` method, via `StreamAddOptions.IdempotentId`. Using idempotent ids avoids
duplicate entries in the stream, even in the event of a failure and retry.

The `StreamIdempotentId` contains a producer id and an optional idempotent id. The producer id should be unique for a given data generator and should be stable and consistent between runs.
The optional idempotent id should be unique for a given data item. If the idempotent id is not provided, the server will generate it from the content of the data item.

```csharp
// int someUniqueExternalSourceId = ... // optional
var idempotentId = new StreamIdempotentId("ticket_generator");
// optionally, new StreamIdempotentId("ticket_generator", someUniqueExternalSourceId)
var messageId = await db.Streams.AddAsync("events_stream", "foo_name", "bar_value",
    new StreamAddOptions { IdempotentId = idempotentId });
```

The `ConfigureAsync` method can be used to configure the stream, in particular the IDMP map. The `StreamConfiguration` class has properties for the idempotent producer (IDMP) duration and max-size.

Reading from Streams
===

Reading from a stream is done by using either the `ReadAsync` or `RangeAsync` methods.

```csharp
using var messages = await db.Streams.ReadAsync("events_stream", "0-0");
foreach (var entry in messages.Entries)
{
    Console.WriteLine(entry.Id);
    foreach (var field in entry.Fields)
    {
        Console.WriteLine($"  {field.Name} = {field.Value}");
    }
}
```

The code above will read all messages from the ID `"0-0"` to the end of the stream. You have the option to limit the number of messages returned by using the optional `count` parameter.

The `ReadAsync` method also allows you to read from multiple streams at once:

```csharp
using var streams = await db.Streams.ReadAsync(new StreamPosition[]
{
    new StreamPosition("events_stream", "0-0"),
    new StreamPosition("score_stream", "0-0")
});

foreach (var stream in streams.Streams)
{
    Console.WriteLine($"Stream = {stream.Name}");
    Console.WriteLine($"Length = {stream.Count}");
}
```

You can limit the number of messages returned per stream by using the `countPerStream` optional parameter.

The `RangeAsync` method allows you to return a range of entries within a stream.

```csharp
using var messages = await db.Streams.RangeAsync("events_stream", minId: "-", maxId: "+");
```

The `"-"` and `"+"` special characters indicate the smallest and greatest IDs possible. These values are the default values that will be used if no value is passed for the respective parameter. You also have the option to read the stream in reverse by using the `messageOrder` parameter. The `RangeAsync` method also provides the ability to limit the number of entries returned by using the `count` parameter.

```csharp
using var messages = await db.Streams.RangeAsync("events_stream",
    minId: "0-0",
    maxId: "+",
    count: 100,
    messageOrder: Order.Descending);
```

Stream Information
===

The `InfoAsync` method provides the ability to read basic information about a stream: its first and last entry, the stream's length, the number of consumer groups, etc. This information can be used to process a stream in a more efficient manner.

```csharp
var info = await db.Streams.InfoAsync("events_stream");

Console.WriteLine(info.Length);
Console.WriteLine(info.FirstEntry.Id);
Console.WriteLine(info.LastEntry.Id);
```

Consumer Groups
===

Using Consumer Groups allows you scale the processing of a stream across multiple workers or consumers. Please read the ["Introduction to Redis Streams"](https://redis.io/topics/streams-intro) article for detailed information on consumer groups.

The following creates a consumer group and tells Redis from which position within the stream to begin reading. If you call the method prior to first creating the stream, the `CreateConsumerGroupAsync` method will create the stream for you by default. You can override this default behavior by passing `false` for the `createStream` optional parameter.

```csharp
// Returns true if created, otherwise false.
await db.Streams.CreateConsumerGroupAsync("events_stream", "events_consumer_group", "$");
// or
await db.Streams.CreateConsumerGroupAsync("events_stream", "events_consumer_group", StreamPosition.NewMessages);
```

The `"$"` special character means that the consumer group will only read messages that are created after the consumer group is created. If you want to read messages that already exist in the stream, you can provide any position within the stream.

```csharp
// Begin reading from the first position in the stream.
await db.Streams.CreateConsumerGroupAsync("events_stream", "events_consumer_group", "0-0");
```

Use the `ReadGroupAsync` method to read messages into a consumer. This method accepts a message ID as one of the parameters. When an ID is passed to `ReadGroupAsync`, Redis will only return pending messages for the given consumer or, in other words, it will only return messages that were ALREADY read by the consumer.

To read new messages into a consumer, you use the `">"` special character or `StreamPosition.NewMessages`. The `">"` special character means **read messages never delivered to other consumers**. Note that **consumers** within a consumer group are auto-created the first time they are used when calling the `ReadGroupAsync` method.

```csharp
// Read 5 messages into two consumers.
using var consumer_1_messages = await db.Streams.ReadGroupAsync("events_stream", "events_cg", "consumer_1", ">", count: 5);
using var consumer_2_messages = await db.Streams.ReadGroupAsync("events_stream", "events_cg", "consumer_2", ">", count: 5);
```

Once a message has been read by a consumer its state becomes "pending" for the consumer, no other consumer can read that message via the `ReadGroupAsync` method. Pending messages for a consumer can be read by using the `ReadGroupAsync` method and by supplying an ID within the range of pending messages for the consumer.

```csharp
// Read the first pending message for the "consumer_1" consumer.
using var message = await db.Streams.ReadGroupAsync("events_stream", "events_cg", "consumer_1", "0-0", count: 1);
```

Pending message information can also be retrieved by calling the `PendingAsync` and `PendingMessagesAsync` methods. `PendingAsync` returns high level information about the number of pending messages, the pending messages per consumer, and the highest and lowest pending message IDs.

```csharp
using var pendingInfo = await db.Streams.PendingAsync("events_stream", "events_cg");

Console.WriteLine(pendingInfo.PendingMessageCount);
Console.WriteLine(pendingInfo.LowestPendingMessageId);
Console.WriteLine(pendingInfo.HighestPendingMessageId);
Console.WriteLine($"Consumer count: {pendingInfo.Consumers.Count}.");
foreach (var consumer in pendingInfo.Consumers)
{
    Console.WriteLine($"{consumer.Name}: {consumer.PendingMessageCount}");
}
```

Use the `PendingMessagesAsync` method to retrieve detailed information about the messages that are pending for a given consumer.

```csharp
// Read the first pending message for the consumer.
using var pendingMessages = await db.Streams.PendingMessagesAsync("events_stream",
    "events_cg",
    count: 1,
    consumer: "consumer_1",
    minId: pendingInfo.LowestPendingMessageId.AsRedisValue());

foreach (var pendingMessage in pendingMessages.Messages)
{
    Console.WriteLine(pendingMessage.MessageId);
    Console.WriteLine(pendingMessage.IdleTime); // a TimeSpan
}
```

Messages are pending for a consumer until they are acknowledged by calling the `AcknowledgeAsync` method. A message is no longer accessible by `ReadGroupAsync` after it is acknowledged.

```csharp
// Copy the ids out of the reply first: the reply's enumerator cannot be held across an await.
var messageIds = new List<RedisValue>(pendingMessages.Count);
foreach (var pendingMessage in pendingMessages.Messages)
{
    messageIds.Add(pendingMessage.MessageId.AsRedisValue());
}

// Returns the number of messages acknowledged.
await db.Streams.AcknowledgeAsync("events_stream", "events_cg", messageIds.ToArray());
```

The `ClaimAsync` method can be used to change ownership of messages consumed by a consumer to a different consumer.

```csharp
// Change ownership to consumer_2 for the first 5 messages pending for consumer_1.
using var pendingMessages = await db.Streams.PendingMessagesAsync("events_stream",
    "events_cg",
    count: 5,
    consumer: "consumer_1",
    minId: "0-0");

var messageIds = new List<RedisValue>(pendingMessages.Count);
foreach (var pendingMessage in pendingMessages.Messages)
{
    messageIds.Add(pendingMessage.MessageId.AsRedisValue());
}

using var claimed = await db.Streams.ClaimAsync("events_stream",
    "events_cg",
    consumer: "consumer_2",
    minIdleTime: TimeSpan.Zero,
    messageIds: messageIds.ToArray());
```

There are several other methods used to process streams using consumer groups. Please reference the Streams unit tests for those methods and how they are used.
