Pipelines and Multiplexers
===

Latency sucks. Modern computers can churn data at an alarming rate, and high speed networking (often with multiple parallel links between important servers) provides enormous bandwidth, but... that damned latency means that computers spend an awful lot of time *waiting for data* (one of the several reasons that continuation-based programming is becoming increasingly popular. Let's consider some regular procedural code:

```C#
string a = db.StringGet("a");
string b = db.StringGet("b");
```

In terms of steps involved, this looks like:

    [req1]                         # client: the client library constructs request 1
         [c=>s]                    # network: request one is sent to the server
              [server]             # server: the server processes request 1
                     [s=>c]        # network: response one is sent back to the client
                          [resp1]  # client: the client library parses response 1
                                [req2]
                                     [c=>s]
                                          [server]
                                                 [s=>c]
                                                      [resp2]

Now let's highlight *just the bits where the client is doing something*:

    [req1]
         [====waiting=====]
                          [resp1]
                                [req2]
                                     [====waiting=====]
                                                      [resp2]

And keep in mind that this is **not to scale** - if this was scaled by time, it would be *utterly dominated* by `waiting`.

Pipelining
---

Because of this, many redis clients allow you to make use of *pipelining*; this is the process of sending multiple messages down the pipe without waiting on the reply from each - and (typically) processing the replies later when they come in. In .NET, the idea of an operation that can be initiated but is not yet complete, and which may complete or fault later is encapsulated by the [TPL][1] via the [`Task`][2] / [`Task<T>`][3] APIs. Essentially, a `Task<T>` represents a "future possible value of type `T`" (a non-generic `Task` is essentially a `Task<void>`). You can then either:

- at a later point block (`.Wait()`) until the operation has completed
- schedule a *continuation* (`.ContinueWith(...)` or `await`) to occur when the operation has completed

For example, to pipeline the two gets using procedural (blocking) code, we could use:

```C#
var aPending = db.StringGetAsync("a");
var bPending = db.StringGetAsync("b");
var a = db.Wait(aPending);
var b = db.Wait(bPending);
```

Note that I'm using `db.Wait` here because it will automatically apply the configured synchronous timeout, but you can use `aPending.Wait()` or `Task.WaitAll(aPending, bPending);` if you prefer. Using pipelining allows us to get both requests onto the network immediately, eliminating most of the latency. Additionally, it also helps reduce packet fragmentation: 20 requests sent individually (waiting for each response) will require at least 20 packets, but 20 requests sent in a pipeline could fit into much fewer packets (perhaps even just one).

Fire and Forget
---

A special-case of pipelining is when we expressly don't care about the response from a particular operation, which allows our code to continue immediately while the enqueued operation proceeds in the background. Often, this means that we can put concurrent work on the connection from a single caller. This is achieved using the `flags` parameter:

```C#
// sliding expiration
db.KeyExpire(key, TimeSpan.FromMinutes(5), flags: CommandFlags.FireAndForget);
var value = (string)db.StringGet(key);
```

The `FireAndForget` flag causes the client library to queue the work as normal, but immediately return a default value (since `KeyExpire` returns a `bool`, this will return `false`, because `default(bool)` is `false` - however the return value is meaningless and should be ignored). This works for `*Async` methods too: an already-completed `Task<T>` is returned with the default value (or an already-completed `Task` is returned for `void` methods).

Multiplexing
---

Pipelining is all well and good, but often any single block of code only wants a single value (or maybe wants to perform a few operations, but which depend on each-other). This means that we still have the problem that we spend most of our time waiting for data to transfer between client and server.  Now consider a busy application, perhaps a web-server. Such applications are generally inherently concurrent, so if you have 20 parallel application requests all requiring data, you might think of spinning up 20 connections, or you could synchronize access to a single connection (which would mean the last caller would need to wait for the latency of all the other 19 before it even got started). Or as a compromise, perhaps a pool of 5 connections which are leased - no matter how you are doing it, there is going to be a lot of waiting. **StackExchange.Redis does not do this**; instead, it does a *lot* of work for you to make effective use of all this idle time by *multiplexing* a single connection. When used concurrently by different callers, it **automatically pipelines the separate requests**, so regardless of whether the requests use blocking or asynchronous access, the work is all pipelined. So we could have 10 or 20 of our "get a and b" scenario from earlier (from different application requests), and they would all get onto the connection as soon as possible. Essentially, it fills the `waiting` time with work from other callers.

For this reason, the only redis features that StackExchange.Redis does not offer (and *will not ever offer*) are the "blocking pops" ([BLPOP](http://redis.io/commands/blpop), [BRPOP](http://redis.io/commands/brpop) and [BRPOPLPUSH](http://redis.io/commands/brpoplpush)) - because this would allow a single caller to stall the entire multiplexer, blocking all other callers. The only other time that StackExchange.Redis needs to hold work is when verifying pre-conditions for a transaction, which is why StackExchange.Redis encapsulates such conditions into internally managed `Condition` instances. [Read more about transactions here](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Transactions.md). If you feel you want "blocking pops", then I strongly suggest you consider pub/sub instead:

```C#
sub.Subscribe(channel, delegate {
    string work = db.ListRightPop(key);
    if (work != null) Process(work);
});
//...
db.ListLeftPush(key, newWork, flags: CommandFlags.FireAndForget);
sub.Publish(channel, "");
```

This achieves the same intent without requiring blocking operations. Notes:

- the *data* is not sent via pub/sub; the pub/sub API is only used to notify workers to check for more work
- if there are no workers, the new items remain buffered in the list; work does not fall on the floor
- only one worker can pop a single value; when there are more consumers than producers, some consumers will be notified and then find there is nothing to do
- when you restart a worker, you should *assume* there is work so that you process any backlog
- but other than that, the semantic is identical to blocking pops

The multiplexed nature of StackExchange.Redis makes it possible to reach extremely high throughput on a single connection while using regular uncomplicated code.

Concurrency
---

It should be noted that the pipeline / multiplexer / future-value approach also plays very nicely with continuation-based asynchronous code; for example you could write:

```C#
string value = await db.StringGet(key);
if (value == null) {
    value = await ComputeValueFromDatabase(...);
    db.StringSet(key, value, flags: CommandFlags.FireAndForget);
}
return value;
```

  [1]: http://msdn.microsoft.com/en-us/library/dd460717(v=vs.110).aspx
  [2]: http://msdn.microsoft.com/en-us/library/system.threading.tasks.task(v=vs.110).aspx
  [3]: http://msdn.microsoft.com/en-us/library/dd321424(v=vs.110).aspx
