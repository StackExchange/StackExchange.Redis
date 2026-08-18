# Sync over async, and thread-pool starvation

If you are here from a `SER307` warning or a support conversation: this page explains why blocking on an
asynchronous redis call is worse than it looks, why the symptom shows up somewhere else entirely, and what to
do about it.

## The short version

Calling `.Result`, `.Wait()` or `.GetAwaiter().GetResult()` on an `async` method is *sync over async*. It looks
like a small convenience. What it actually does is hold one thread hostage while waiting for a reply — and
processing that reply **also needs a thread**. Do this enough times concurrently and the pool runs out of
threads to process replies with, so nothing completes, so nothing releases a thread. The client is not slow;
it is stuck.

```csharp
// the problem
var value = db.StringGetAsync(key).Result;

// the fix, if the caller can be async
var value = await db.StringGetAsync(key);

// the fix, if it genuinely cannot
var value = db.StringGet(key);
```

That last line is the one people miss. **StackExchange.Redis has a real synchronous API**, and it is not
sync-over-async internally — it is a genuinely synchronous path. If you need a blocking call, use it. Blocking
on the *async* method is the only version of this that hurts.

## Why it fails so badly, rather than just being slow

A multiplexed client makes this worse than the general case, because the thing you are waiting for needs the
same resource you are consuming while you wait.

1. Your code calls `.Result` on a redis command, and the calling thread blocks.
2. Redis replies. The bytes arrive at the socket promptly — this part is almost never the problem.
3. To turn those bytes into a completed `Task`, the client needs a thread.
4. If every thread is blocked at step 1, step 3 cannot happen.
5. Nothing completes, so no thread from step 1 is ever released.

The result is timeouts that look like a network or server problem and are neither. A characteristic sign is a
timeout message reporting **data already sitting in the socket** — the reply is *there*, it simply cannot be
processed.

## Why adding threads does not rescue you

The .NET thread-pool creates threads on demand up to its **minimum**, then throttles hard: beyond that point
it adds threads slowly and deliberately, on the order of one or two per second, using a hill-climbing
heuristic that is trying to find a *throughput* optimum. That heuristic assumes threads are working. Here they
are blocked, so more threads simply means more blocked threads.

This is why raising `MinThreads` is not a fix:

- it moves the wall further away rather than removing it, and under load you will arrive at it anyway;
- the pile of blocked threads grows with it, so the failure is bigger when it comes;
- and the underlying call pattern is unchanged.

Raising the minimum can be a reasonable *stopgap* while you fix the call sites, and it does help a genuine
burst of short-lived work. It does not help an application that is systematically blocking on I/O.

## Confirming it is this

Look at a timeout message from the client — see [Timeouts](Timeouts) for how to read one in full. The
combination that points here is:

- `Busy` at or above `Min` for `WORKER` or `IOCP`, meaning the pool is in its throttled, slow-growth regime;
- bytes waiting unread on the connection, meaning the server already answered;
- and timeouts that get *worse* under load rather than better as caches warm.

A message showing all of it at once looks something like:

	Timeout performing GET MyKey (5000ms), inst: 0, qs: 84, in: 487312, mgr: 10 of 10 available,
	IOCP: (Busy=0,Free=1000,Min=8,Max=1000),
	WORKER: (Busy=73,Free=32694,Min=64,Max=32767)

Read that as: 84 commands are awaiting replies (`qs`), **476KiB has already arrived and is sitting unread**
(`in`), the client's own dedicated pool is completely idle (`mgr: 10 of 10`) — and the global worker pool has
more busy threads than its minimum, so it is injecting new ones a couple per second at most.

The `in` figure is the tell. The server has answered; the bytes are in the buffer. Nothing is wrong with the
network, the server, or the connection — there is simply no thread available to pick the reply up. An idle
`mgr` alongside a busy `WORKER` says the same thing from the other direction: the client is not the bottleneck,
the application's thread-pool is.

If the pool is healthy and you still see timeouts, this is not your problem — look at
[Timeouts](Timeouts) and [Thread Theft](ThreadTheft) instead.

## Fixing it

In order of preference:

1. **Make the call path async.** `await` the call, all the way up. This is the only change that removes the
   problem rather than accommodating it.
2. **Use the synchronous API** where the caller genuinely cannot be async — a constructor, an interface you do
   not control, a legacy entry point. `db.StringGet(key)` is not sync-over-async.
3. **Take the client off the thread-pool**, as a mitigation while you do 1 or 2 (see below).

### Mitigation: give the client its own threads

```csharp
ConnectionMultiplexer.SetFeatureFlag("DedicatedThreads", true); // early in application startup
```

This makes the library read and write on threads it owns rather than borrowing the thread-pool, so redis
traffic keeps flowing even while the pool is saturated.

Be clear about what this does and does not do. It **does not fix the thread-pool** — nothing in this library
can, because the blocked threads are in your code. What it does is stop redis from being caught in the jam,
which usually converts "everything times out" into "the application is slow, and one part of it is obviously
blocking". That is a much better place to debug from, and for many applications it is enough to restore
service while the real fix is made. It is not a licence to keep the blocking calls.

Two caveats worth knowing before you enable it:

- it costs a reader and a writer thread **per connection**, so think about it before enabling it against a
  very wide cluster, where connection count scales with the number of shards;
- it is deliberately opt-in, and set process-wide at startup rather than per-connection.

## Fire-and-forget is a special case

`CommandFlags.FireAndForget` returns an already-completed task carrying the default value, so blocking on one
does not wait for anything and cannot starve the pool. It is still not doing what it looks like: the value is
fixed before the call returns and is never the server's answer. Discard it, or use the synchronous API if you
actually want a result — see `SER306`.

## The analyzer

The package ships a Roslyn analyzer that flags these at build time:

- **`SER307`** — blocking on a redis call instead of awaiting it; this page is what it links to.
- **`SER306`** — reading a fire-and-forget result, which is always the default value.
- **`SER305`** — waiting on a command queued to a transaction or batch before `Execute[Async]` has sent it.
  That one is an *error*, because it cannot ever complete.

See [Analyzer rules](rules/) for the full set, including how to turn any of them down.

## See also

- [Timeouts](Timeouts) — reading a timeout message, and the thread-pool statistics in it
- [Thread Theft](ThreadTheft) — a different problem with a similar smell, where the reader thread is hijacked
  by continuations rather than starved of threads
- [Pipelines and multiplexers](PipelinesMultiplexers) — why the client is shaped this way in the first place
