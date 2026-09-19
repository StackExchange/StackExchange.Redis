# Replacing the message core — plan

**Status: planning only. Nothing here is built.** Written 2026-09-19, after the batch/transaction work
ran into the shim rather than through it.

---

## 1. Why now, and why not carry on composing

The framing in `interpolated-resp-writer.queue.md` argued — correctly, at the time — that the vexing
commands need *composition inside the `Message` layer*, not a second write path, because that layer owns
ordering, the backlog, retry accounting and the reconnect handshake. Two of the three frame-shaped
participants it called for now exist and work: `FramePairMessage` (write-lock injection, for
`SCRIPT LOAD`+`EVALSHA` and `HIMPORT PREPARE`+`SET`) and `FrameRunMessage` (`IMultiMessage`, for a batch
written as one run).

The third one — `MULTI`/`WATCH`/`EXEC` — is where it stops paying. Marc:

> I strongly suspect that we're going to be tying ourselves in knots trying to force that transaction /
> batch work into the existing Message queue, which will then need to be reworked. […] better to leave
> those alone / throw NIE / whatever, and come back to them when we aren't on shifting sands.

**The evidence that this is right is already on disk.** `origin/core-respite` (2025-08-29) contains a
`BatchConnection` that is, line for line, the thing `RespBatchExecutor` was re-derived into this week —
accumulate a list, flush to the tail, fault the outstanding entries on dispose — except that it is built
on a primitive where *the operation is the completion*, so it needs no `TaskCompletionSource` per
element, no `Span<ValueTask<RespPayload>>` out-parameter, and no scatter-back. Its tail already takes a
run:

```csharp
void Send(ReadOnlySpan<RespOperation> message);          // IRespConnection
```

Today's `IRespRunExecutor` is a worse version of a primitive that was designed a year ago. It is marked
provisional for an unrelated reason (its spans forbid an `async` implementation, so `RespRetryExecutor`
cannot offer it and retry+batch silently degrades) — but the deeper reason is that it is the wrong shape
because the thing underneath is the wrong shape.

**So: stop. Leave `CreateTransaction` and the lock members throwing. Plan the core.**

What that costs: `Fallback<T>()` does not reach zero, and SER352 stays at 8. Both were framed as release
gates. They remain gates — this defers them, it does not cancel them, and the exit is this document.

---

## 2. What already exists, and what of it we want

`origin/core-respite`, branched ~2025-08, "moved most of the machinery to RESPite; next: migrate
testing". It is not a finished product and we do not need all of it.

### Take

| piece | what it is | why |
|---|---|---|
| `RespMessageBase<TResponse>` | `IValueTaskSource<T>` over `ManualResetValueTaskSourceCore<T>`, with `Reset(bool recycle)` | the core idea: one object that is the request, the completion and the awaitable |
| `RespOperation` / `RespOperation<T>` | `readonly struct (IRespMessage message, short token)`, implicitly a `ValueTask` | a cheap handle; identical layout between the typed and untyped forms is load-bearing |
| the ref-counted request | `TryReserveRequest`/`ReleaseRequest` with `Interlocked` on `_requestRefCount`, returning to an `ArrayPool<byte>` at zero | answers "who owns the bytes, and until when" once, instead of per feature |
| cancellation | `CancellationTokenRegistration` taken at init, unregistered on any definite outcome | the current core has none at all; every `SendAsync` on the new surface takes a token it cannot honour |
| `Wait(short token, TimeSpan timeout)` | synchronous completion off the same core | replaces the `Monitor.PulseAll` box — see §6 |
| `BatchConnection` | accumulate, flush to the tail, fault outstanding on dispose | already correct; today's re-derivation is evidence of that |

### Leave (for now)

- The connection/transport plumbing on that branch (`StreamConnection`, the parser interfaces). The
  transport story has moved on since — `DuplexTransport` is live and measured.
- `RespContext` as it exists there. Ours has diverged a long way and is better.
- The `Alt`/downlevel shims.

### Do not copy blindly

That branch predates: the retry category work, the client-side cache, key-marking on frames, and the
high-integrity token. Every one of those touches the message's lifetime. The inventory in §5 is the
guard against copying a design that silently lacks them.

---

## 3. Target shape

### 3a. The operation

One type replaces `Message` **and** `IResultBox`:

- It is an `IValueTaskSource<T>` (and the untyped `IValueTaskSource`), so an awaited command allocates
  **one** object, not a message plus a box plus a `Task`.
- It carries the rendered request (ref-counted, pooled) and the outcome.
- It is token-versioned (`_asyncCore.Version`), so a recycled instance cannot be completed twice by a
  stale holder — a real hazard once pooling exists.
- `RespOperation` is the handle callers hold; it converts implicitly to `ValueTask`.

**This is also the answer to the question the batch work kept failing to answer.** "How does element 3
of 5 get its result out?" — element 3 *is* a `RespOperation`, whose message is its own completion. There
is nothing to hand back, so there is no return channel to design, no out-span, and no parallel arrays.

### 3b. Executors

Marc's topology, which the current `RespExecutorBase` is already shaped to host (it became an abstract
class on 2026-09-19, so capabilities are virtuals with defaults rather than type-tested interfaces):

| executor | routes by |
|---|---|
| server-endpoint | straight to that endpoint's connections |
| multiplexer | slot → endpoint → connection |
| group multiplexer | tracks the active node, then as above |

This is a genuine simplification over today, where routing is spread across
`ServerSelectionStrategy.Select`, `ServerEndPoint.GetBridge` and `PhysicalBridge`, and where a per-call
server hint is not expressible at all — which is exactly why `Publish` (send via the subscribed
connection) and `IsConnected`/`IdentifyEndpoint` are still on the fallback.

Two things fall out that are currently blocked:

- **`Publish`**: the server-endpoint executor *is* the per-call hint. Pick it, send through it.
- **Batch across a cluster**: a batch groups by slot today because grouping by server races a reshard.
  With a slot-directing executor the grouping is the executor's, and the race is confined to it.

### 3c. Batch and transaction — and the scope of `RespOperation`

**`RespOperation` is the batch API's currency, not the whole core's.** Marc: *"maybe RespOperation
(request/handler tuples) only apply to the batch API"*. That settles what was going to be this plan's
hardest open question.

The ordinary send is unchanged: `SendAsync<T>(context, ref frame, flags, handler, ct)` probes the cache,
misses, hands a request to a **non-generic** executor and applies the handler above it. That path is
already right, already fast, and already has the cache in the correct place; making the whole core speak
in typed operations would push generics through every layer to serve the one case that needs them.

The batch API takes `ReadOnlySpan<RespOperation>` where each element is (request, handler, completion) -
so the handler travels *in*, the element completes itself, and the awkwardness that `RespBatchExecutor`
has today (a `TaskCompletionSource` per element, a `Span<ValueTask<RespPayload>>` out-parameter, a
scatter-back into caller positions) simply does not arise. It does not arise *anywhere else either*,
because nowhere else needs it.

This is also what makes §3d affordable: if batches are the only thing speaking in operations, then
"batches behave differently around the cache" is a statement about one API rather than a special case
threaded through the core.



Both become connection/executor decorators over the operation list, as `BatchConnection` already is:

- **batch** = accumulate, flush as a run, each operation completes itself.
- **transaction** = the same, plus a preamble (`WATCH`, `MULTI`), a constraint check that may need a
  reply before the tail is issued, and `EXEC`.

**The constraint that must survive into the design:** a pause for a reply is a *contiguity boundary*.
Today's `IMultiMessage` expansion runs inside the write lock, and an enumerator that blocks for a reply
cannot hold that lock — the reader has to make progress. So "flush point" and "pause point" are
different things and the type must say which it means, or a transaction will silently promise adjacency
it does not have.

---

## 3d. The cache, which the old design predates

`origin/core-respite` is from before the client-side cache existed, so nothing in it accounts for one.
This is the largest gap between that branch and what we need, and it cuts two ways.

### The win: cache the payload, do not copy it

Today `PayloadProcessor.SetResult` rents an array per reply and copies the bytes into it, wrapped in a
`RefCountedBuffer` - the deliberate single-owner choice recorded at §6.16 of the queue. In the new core
the operation already owns a ref-counted response buffer, so **filling the cache is a retain rather than
a copy**. That removes a copy per reply on the cached path, and possibly on every path.

That makes the response payload itself the cached artefact, which is the shape the cache already wants:
it is keyed on the rendered frame's bytes and stores a `RespPayload`, so nothing about the cache's model
has to change - only who allocated the buffer it holds.

### The exclusion: batches and transactions do not participate

Marc: *"I do not propose that cache needs to support either batches or transactions, so: different
behaviour there may make sense."* Agreed, and it is stronger than a simplification:

**The cache has no guard of its own.** Probed with a fake whose first reply was `+QUEUED`, a second
identical read was served from the cache without a send (`sends=1`). `IsCacheableReply` rejects errors
and nothing else, so a simple string is stored happily. Anything that ever hands back a non-final reply
for a command - which is exactly what a queued command inside `MULTI` is - would poison the entry. That
the current transaction path probably completes its tasks from the `EXEC` array rather than from
`+QUEUED` makes today's behaviour accidental rather than designed.

**Note this is a deliberate behaviour change.** Batched commands participate fully in the cache today,
because the probe sits *above* the batch executor: a batched read can be served from cache, and a
batched reply fills it.

**A middle worth considering rather than all-or-nothing:** *probe on the way in, do not fill on the way
out.* A batched command that hits is answered before it is ever queued - keeping the round trip saved,
which is the whole value - while the batch's replies never populate the cache, which is where the
complexity lives (in-flight coalescing across a deferred flush, and the stampede question of what a
second caller waits on). Transactions should do neither.

---

## 4. What the new token must keep

The highest-risk part of this work, because it is all *diagnostics* — nothing fails a test when it goes
missing, and it is the difference between a timeout exception that names the problem and one that says
"it timed out".

Today's `Message` carries, and the replacement needs:

| on `Message` | used for |
|---|---|
| `CreatedDateTime`, `CreatedTimestamp` | age, and the `ProfiledCommand` timeline |
| `Status` (`CommandStatus`: `WaitingToBeSent` → `WaitingInBacklog` → `Sent` → …) | `FaultContext.NotApplied`, which is what makes an unsent command safely retryable |
| `_enqueuedTo` (the `PhysicalConnection`) | `TryGetHeadMessages`, `TryGetPhysicalState` |
| `_queuedStampSent`, `_queuedStampReceived` | byte counters snapshotted at enqueue, differenced at timeout |
| `_writeTickCount` | backlog timeout detection (`HasTimedOut`) |
| `performance` (`ProfiledCommand`) | the profiling API |
| `HighIntegrityToken` | the high-integrity response check |

That is what produces the timeout text this library is known for — *"inst: 0, qu: 0, qs: 0, aw: False,
bw: CheckingForTimeout, last-in: 0, cur-in: 0, lm: 18/1814/1718/78, sync-ops: 0, async-ops: 21…"*.

**`Status` is not merely diagnostic.** `RetryPolicy.CanRetry` reads `FaultContext.NotApplied`, which is
derived from it: a command known not to have been applied bypasses the side-effect cap. Lose the status
ladder and retry silently becomes more conservative.

**Pooling makes this harder, not easier.** A recycled operation must not leak a previous life's
timestamps into a new one's timeout report. `Reset(bool recycle)` has to be exhaustive, and the
token-version check is what turns a stale read into an exception rather than a wrong answer.

---

## 5. What we deliberately lose

**The synchronous result box and its lock.** Today a sync caller waits on:

```csharp
lock (this) { _completed = true; Monitor.PulseAll(this); }   // SimpleResultBox.ActivateContinuations
```

…which means every synchronous command allocates a box, takes a lock on completion, and pulses. The
replacement is `Wait(token, timeout)` on the same value-task core the async path uses: one object, one
mechanism, no monitor. `SimpleResultBox`, `TaskResultBox` and `IResultBox` all go.

That also removes the awkwardness noted during the retry and batch work — that `IResultBox` is the
transitional layer's own plumbing and must not leak into the context surface.

---

## 6. Pooling policy

From Marc: pool the core queue types **in the success / definite-response cases**; *"timeouts are
undefined chaos"*.

Concretely:

- Recycle on a **definite** outcome: a result parsed, a server error, a cancellation observed.
- **Do not recycle** on timeout, or on a connection fault that leaves the message possibly-still-queued.
  The pipeline may still hold a reference it will write or complete later; handing that instance back to
  a pool is how a reply lands on somebody else's command.
- The token version is the backstop, not the policy. It turns a late completion into a detected error;
  it does not make recycling safe.

This is the same asymmetry `FrameMessage` already lives with: it copies for fire-and-forget precisely
because that path completes before the bytes are used.

---

## 6a. Measured: how much of the suite the context surface already carries

Before planning any deletion, the cheapest possible experiment — one edit to `GetDatabase`, routing
**every** `IDatabase` in the suite through `TransitionalDatabase` over the context, with the classic
database as the fallback:

```
Failed: 110, Passed: 8051, Skipped: 163, Total: 8324   —   69 distinct tests
```

**~98.7% of the whole suite already runs through the new surface.** That is the number to plan against,
and it was not knowable by reading. Grouped by cause:

| n | cause | what it is |
|---|---|---|
| 27 | `Assert.Equal` values differ | genuine behavioural differences — the real work |
| 25 | `InvalidCastException: …Transitional…` | **test coupling**, not a gap: tests casting `IDatabase` to the concrete type |
| 14 | `RedisCommandException: disabled in the command map` | the context resolves commands through the map; these tests disable one and expect the old path's behaviour |
| 14 | `Assert.Throws`: nothing thrown | argument validation the new surface does not do (the null-key tests) |
| 6 | `NOSCRIPT` | script-cache belief across the two paths |
| 3 | `A deadline is required; KEEPTTL and PERSIST are not expirations` | expiry-shape difference |
| ~5 | pool/rent assertions | tests asserting the *shim's* buffer behaviour specifically |

So of 69 distinct failures, roughly 30 are tests coupled to the old implementation rather than gaps in
the new surface. **The genuine gap is around 40 tests**, concentrated in behaviour differences, argument
validation, and the command map.

The spike was reverted, not committed — it changes `GetDatabase` for everyone. The sanctioned mechanism
for re-running a suite through the context surface already exists: `TransitionalSurfaceFixture.Wrap`,
used by 12 `TransitionalXxxTests : XxxTests` classes. Widening that from 12 towards the 64 candidates is
the non-destructive way to hold this ground permanently.

---

## 6b. What deleting actually buys, and what it does not

Message construction, by file:

| sites | file |
|---|---|
| 504 | `RedisDatabase` |
| 122 | `RedisServer` |
| 30 | `ServerEndPoint` |
| 5 | `RedisTransaction` |
| 5 | `PhysicalConnection` |
| 4 | `RedisSubscriber` |
| 3 | `ConnectionMultiplexer` |
| **0** | `RedisBatch` |

Two things follow.

**The target is `RedisDatabase`'s command methods, not "the database implementations".** `RedisBatch`
builds no messages at all; `KeyPrefixed*`, `MultiGroupDatabase`, `RetryDatabase` and `RetryTransaction`
are decorators. Deleting them removes API and behaviour while freeing no message machinery. `RedisDatabase`
alone is ~75% of it.

**Deleting them does not let `Message` go.** ~150 sites live in `RedisServer` and `ServerEndPoint`, and
the context surface has exactly one server group (`Keyspace`) against `IServer`'s ~70 members. The
server surface is the long pole, and it is barely started.

**And `RedisBase` must survive the cut.** `RespMessageExecutor` holds a `RedisBase` target and calls
`_target.ExecuteAsync(message, processor)` — so the execute plumbing is what the *new* surface stands
on. The cut is the ~504 command-building methods, not the type that owns dispatch.

---

## 7. Phasing

Big-bang is not available: the `Message` layer is load-bearing for ordering, backlog, retry and
reconnect, and the existing `IDatabase` surface is a released API sitting on top of it.

Proposed order, each step independently shippable:

1. **Land the operation type in RESPite**, with pooling and cancellation, and unit tests for the token
   lifecycle — completion, double-completion, recycle-then-stale-complete, cancellation races. No
   consumers yet. This is the piece most worth getting right in isolation.
2. **Port the diagnostics inventory** (§4) onto it, with a test that renders a timeout report from a
   synthetic operation. Do this *before* any consumer, so the shape is decided while it is cheap.
3. **One executor, one connection**: the server-endpoint executor over a real connection, behind
   `RespExecutorBase`. The context surface already talks to that abstraction, so this is swappable.
4. **Routing executors**: multiplexer (slot) and group (active node). `Publish` and the endpoint-identity
   members come off the fallback here.
5. **Batch, then transaction**, as decorators. `RespBatchExecutor` is replaced by the `BatchConnection`
   shape; `IRespRunExecutor` is deleted rather than fixed.
6. **Retire the shim**: `RespMessageExecutor`, `FrameMessage`, `FramePairMessage`, `FrameRunMessage`,
   `PayloadProcessor` and its copy-per-reply.

`TransitionalDatabase` is unaffected throughout — it talks to the context surface, which talks to
`RespExecutorBase`. That is the seam that makes this a replacement rather than a rewrite.

---

## 8. Open questions

- **Where does the client-side cache probe sit?** It currently runs *above* the executor, so a cached
  command never reaches one. That ordering must survive; it is easy to lose when the send path is
  rewritten - and under §3d it is what a batch keeps even when it stops filling.
- **How much of `PhysicalBridge` survives?** The backlog, the write lock and the timeout sweep are
  independent of `Message`'s shape, but they are written in terms of it.
- **Inline parsing.** `IRespMessage.AllowInlineParsing` exists on the old branch; we have no equivalent,
  and it interacts with who owns the reply buffer.
- **What happens to `ResultProcessor`?** The typed-parse story on the new surface is `IRespHandler<T>`;
  the classic one is `ResultProcessor<T>`. They are two implementations of one idea and the core work is
  the moment to collapse them — or to decide deliberately not to.
