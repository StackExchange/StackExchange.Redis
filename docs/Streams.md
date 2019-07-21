Overview
===

The [Stream](https://redis.io/topics/streams-intro) data type was added in Redis version 5.0 and it represents an append-only log of messages. All of the [stream related commands](https://redis.io/commands#stream) documented on redis.io have been implemented in the StackExchange.Redis client library. Read the ["Introduction to Redis Streams"](https://redis.io/topics/streams-intro) article for further information on the raw Redis commands and how to work with streams.

Writing to Streams
===

Each message or entry in the stream is represented by the `StreamEntry` type. Each stream entry contains a unique ID and an array of name/value pairs. The name/value pairs are represented by the `NameValueEntry` type.

Use the following to add a simple message with a single name/value pair to a stream:

```csharp
var db = redis.GetDatabase();
var messageId = db.StreamAdd("event_stream", "foo_name", "bar_value");
// messageId = 1518951480106-0
```

The message ID returned by `StreamAdd` is comprised of the millisecond time when the message was added to the stream and a sequence number. The sequence number is used to prevent ID collisions if two or more messages were created at the same millisecond time.

Multiple name/value pairs can be written to a stream using the following:

```csharp
var values = new NameValueEntry[]
{
    new NameValueEntry("sensor_id", "1234"),
    new NameValueEntry("temp", "19.8")
}; 

var db = redis.GetDatabase();
var messageId = db.StreamAdd("sensor_stream", values);
```

You also have the option to override the auto-generated message ID by passing your own ID to the `StreamAdd` method. Other optional parameters allow you to trim the stream's length.

```csharp
db.StreamAdd("event_stream", "foo_name", "bar_value", messageId: "0-1", maxLength: 100);
```

Reading from Streams
===

Reading from a stream is done by using either the `StreamRead` or `StreamRange` methods.

```csharp
var messages = db.StreamRead("event_stream", "0-0");
```

The code above will read all messages from the ID `"0-0"` to the end of the stream. You have the option to limit the number of messages returned by using the optional `count` parameter.

The `StreamRead` method also allows you to read from multiple streams at once:

```csharp
var streams = db.StreamRead(new StreamPosition[]
{
    new StreamPosition("event_stream", "0-0"),
    new StreamPosition("score_stream", "0-0")
});

Console.WriteLine($"Stream = {streams.First().Key}");
Console.WriteLine($"Length = {streams.First().Entries.Length}");
```

You can limit the number of messages returned per stream by using the `countPerStream` optional parameter.

The `StreamRange` method allows you to return a range of entries within a stream. 

```csharp
var messages = db.StreamRange("event_stream", minId: "-", maxId: "+");
```

The `"-"` and `"+"` special characters indicate the smallest and greatest IDs possible. These values are the default values that will be used if no value is passed for the respective parameter. You also have the option to read the stream in reverse by using the `messageOrder` parameter. The `StreamRange` method also provides the ability to limit the number of entries returned by using the `count` parameter.

```csharp
var messages = db.StreamRange("event_stream", 
    minId: "0-0", 
    maxId: "+", 
    count: 100,
    messageOrder: Order.Descending);
```

Stream Information
===

The `StreamInfo` method provides the ability to read basic information about a stream: its first and last entry, the stream's length, the number of consumer groups, etc. This information can be used to process a stream in a more efficient manner.

```csharp
var info = db.StreamInfo("event_stream");

Console.WriteLine(info.Length);
Console.WriteLine(info.FirstEntry.Id);
Console.WriteLine(info.LastEntry.Id);
```

Consumer Groups
===

Using Consumer Groups allows you scale the processing of a stream across multiple workers or consumers. Please read the ["Introduction to Redis Streams"](https://redis.io/topics/streams-intro) article for detailed information on consumer groups.

The following creates a consumer group and tells Redis from which position within the stream to begin reading. If you call the method prior to first creating the stream, the `StreamCreateConsumerGroup` method will create the stream for you by default. You can override this default behavior by passing `false` for the `createStream` optional parameter.

```csharp
// Returns true if created, otherwise false.
db.StreamCreateConsumerGroup("events_stream", "events_consumer_group", "$");
// or
db.StreamCreateConsumerGroup("events_stream", "events_consumer_group", StreamPosition.NewMessages);
```

The `"$"` special character means that the consumer group will only read messages that are created after the consumer group is created. If you want to read messages that already exist in the stream, you can provide any position within the stream.

```csharp
// Begin reading from the first position in the stream.
db.StreamCreateConsumerGroup("events_stream", "events_consumer_group", "0-0");
```

Use the `StreamReadGroup` method to read messages into a consumer. This method accepts a message ID as one of the parameters. When an ID is passed to `StreamReadGroup`, Redis will only return pending messages for the given consumer or, in other words, it will only return messages that were ALREADY read by the consumer.

To read new messages into a consumer, you use the `">"` special character or `StreamPosition.NewMessages`. The `">"` special character means **read messages never delivered to other consumers**. Note that **consumers** within a consumer group are auto-created the first time they are used when calling the `StreamReadGroup` method.

```csharp
// Read 5 messages into two consumers.
var consumer_1_messages = db.StreamReadGroup("events_stream", "events_cg", "consumer_1", ">", count: 5);
var consumer_2_messages = db.StreamReadGroup("events_stream", "events_cg", "consumer_2", ">", count: 5);
```

Once a message has been read by a consumer its state becomes "pending" for the consumer, no other consumer can read that message via the `StreamReadGroup` method. Pending messages for a consumer can be read by using the `StreamReadGroup` method and by supplying an ID within the range of pending messages for the consumer.

```csharp
// Read the first pending message for the "consumer_1" consumer.
var message = db.StreamReadGroup("events_stream", "events_cg", "consumer_1", "0-0", count: 1);
```

Pending message information can also be retrieved by calling the `StreamPending` and `StreamPendingMessages` methods. `StreamPending` returns high level information about the number of pending messages, the pending messages per consumer, and the highest and lowest pending message IDs.

```csharp
var pendingInfo = db.StreamPending("events_stream", "events_cg");

Console.WriteLine(pendingInfo.PendingMessageCount);
Console.WriteLine(pendingInfo.LowestPendingMessageId);
Console.WriteLine(pendingInfo.HighestPendingMessageId);
Console.WriteLine($"Consumer count: {pendingInfo.Consumers.Length}.");
Console.WriteLine(pendingInfo.Consumers.First().Name);
Console.WriteLine(pendingInfo.Consumers.First().PendingMessageCount);
```

Use the `StreamPendingMessages` method to retrieve detailed information about the messages that are pending for a given consumer.

```csharp
// Read the first pending message for the consumer.
var pendingMessages = db.StreamPendingMessages("events_stream",
    "events_cg",
    count: 1,
    consumerName: "consumer_1",
    minId: pendingInfo.LowestPendingMessageId);

Console.WriteLine(pendingMessages.Single().MessageId);
Console.WriteLine(pendingMessages.Single().IdleTimeInMilliseconds);
```

Messages are pending for a consumer until they are acknowledged by calling the `StreamAcknowledge` method. A message is no longer accessible by `StreamReadGroup` after it is acknowledged.

```csharp
// Returns the number of messages acknowledged.
db.StreamAcknowledge("events_stream", "events_cg", pendingMessage.MessageId);
```

The `StreamClaim` method can be used to change ownership of messages consumed by a consumer to a different consumer.

```csharp
// Change ownership to consumer_2 for the first 5 messages pending for consumer_1.
var pendingMessages = db.StreamPendingMessages("events_stream", 
    "events_cg", 
    count: 5, 
    consumerName: "consumer_1", 
    minId: "0-0");

db.StreamClaim("events_stream",
    "events_cg",
    claimingConsumer: "consumer_2",
    minIdleTimeInMs: 0,
    messageIds: pendingMessages.Select(m => m.MessageId).ToArray());
```

There are several other methods used to process streams using consumer groups. Please reference the Streams unit tests for those methods and how they are used.




