Pub/Sub Message Order
===

When using the pub/sub API, there is a decision to be made as to whether messages from the same connection should be processed *sequentially* vs *concurrently*. It is strongly recommended that you use concurrent processing whenever possible

Processing them sequentially means that you don't need to worry (quite as much) about thread-safety, and means that you preserve the order of events -
they will be processed in exactly the same order in which they are received (via a queue) - but as a consequence it means that messages can delay each-other.

```csharp
var channel = multiplexer.GetSubscriber().Subscribe("messages");
channel.OnMessage(message =>
{
    Console.WriteLine((string)message.Message);
});
```

The other option is *concurrent* processing. This makes **no specific guarantees** about the order in which work gets processed, and your code is entirely
responsible for ensuring that concurrent messages don't corrupt your internal state - but it can be significantly faster and much more scalable.
This works *particularly* well if messages are generally unrelated.

```csharp
multiplexer.GetSubscriber().Subscribe("messages", (channel, message) => {
    Console.WriteLine((string)message);
});
```
Reconnection
---

Single-node subscription restoration is coordinated until Redis acknowledges the subscription. Concurrent restoration triggers cannot establish independent routes for the same logical subscription, and late acknowledgements do not replace a newer confirmed connection. This applies to callback and queue subscriptions, including pattern channels; the processing-order choices above remain unchanged.

A previously confirmed, still-live connection remains the delivery route while its replacement awaits acknowledgement. Ownership changes on the read thread when the replacement is acknowledged; later deliveries from the previous connection are rejected. Connection-state checks describe that confirmed delivery route, not a pending restore. Messages already admitted to a handler may finish after the handover. This does not make the handover atomic with publication across Redis nodes.

Redis Pub/Sub does not replay messages missed during a disconnect or failover. Work already queued for a handler may still complete after unsubscribe; applications requiring durable delivery need a replay-capable messaging design.
