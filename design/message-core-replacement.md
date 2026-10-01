﻿# Replacing the message core — plan

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

**The decorators are not work outstanding; they are a measurement of the wrong layer.** `[AutoDatabase]`
generates 1,256 forwarding members across three live types — `MultiGroupDatabase` (628), `RetryDatabase`
(314), `RetryTransaction` (314) — and none of them will ever be hand-written, because in the target shape
the decorator *disappears*: it becomes a context wrapping an executor, surfaced through
`TransitionalDatabase`. Look at what the funnel actually does:

```csharp
// MultiGroupDatabase
private TResult Execute<TState, TResult>(in TState state, AutoDatabaseSyncOperation<TState, TResult> operation)
    => operation(in state, GetActiveDatabase());   // resolve the active member, forward the COMMAND
```

That is the group executor's job done one layer too high. Resolve per-*command* and every one of 628
signatures needs a generated capture struct to carry its arguments across the funnel; resolve per-*send*
and there is one funnel, and the arguments never leave the frame. **The member count is the cost of
decorating above the command surface instead of below it.**

Where each actually stands, which is less than the counts suggest:

| decorator | target | state |
|---|---|---|
| `RetryDatabase` | context + `RespRetryExecutor` | **built** — `GetContextCore()` already returns `_inner.Raw.WithExecutor(new RespRetryExecutor(...))`, with its own tests. The 314 generated members are the old path running in parallel with a working new one. |
| `MultiGroupDatabase` | context + active-node executor | **not built** — `GetContext()` throws "The context surface is not yet wired for multi-group". This is step 4 below. |
| `RetryTransaction` | context + retry executor + transaction | throws, and the message is the reason: *a transaction is replayed as a unit, which a per-frame retry executor cannot express*. |

And a piece of scheduling that falls out: `MultiGroupDatabase` hand-writes `IsConnected`,
`IdentifyEndpoint`, `IdentifyEndpointAsync`, `CreateBatch` and `CreateTransaction` as active-member
delegations — **the same five still on `TransitionalDatabase`'s fallback**. So the group executor and
those five members are one piece of work rather than two. `IsConnected` becoming a virtual on
`RespExecutorBase` is the shape the other four want.

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
mechanism. `SimpleResultBox`, `TaskResultBox` and `IResultBox` all go.

> **Correction, from building it (phase 1).** This section used to end "…one mechanism, **no monitor**".
> That was wrong, and worth recording rather than quietly editing. A synchronous wait means blocking a
> thread, and `ManualResetValueTaskSourceCore<T>` offers no way to do that — the alternatives are a
> `ManualResetEventSlim` per sync call, which is the allocation we were trying to remove, or a monitor.
> So the monitor survives; what actually goes is the **box**, which is the allocation and the indirection.
> It is also cheaper than it reads: an async consumer's `OnCompleted` sets `Flag_NoPulse`, so completing
> an awaited command never takes the lock at all. Only a thread genuinely parked in `Wait` pays for it.

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

1. ~~**Land the operation type in RESPite**, with pooling and cancellation, and unit tests for the token
   lifecycle — completion, double-completion, recycle-then-stale-complete, cancellation races. No
   consumers yet. This is the piece most worth getting right in isolation.~~ **Done** — `0097f414`,
   `src/RESPite/Operations/`, 24 tests. Internal rather than public: nothing commits API until something
   holds one. Three design changes came out of building it, in §7a below.
2. ~~**Port the diagnostics inventory** (§4) onto it, with a test that renders a timeout report from a
   synthetic operation. Do this *before* any consumer, so the shape is decided while it is cheap.~~
   **Done** — `RespOperationDiagnostics`, `RespCommandStatus`, 10 tests. The shape decision it forced is
   in §7b.
3. **One executor, one connection**: the server-endpoint executor over a real connection, behind
   `RespExecutorBase`. The context surface already talks to that abstraction, so this is swappable.
   **Done.** 3a `RespConnection` (a FIFO of operations over `DuplexTransport`), 3b
   `RespConnectionExecutor`, 3c `StreamDuplexTransport`, 3d `RespHandshake`. The whole path now runs
   against a real server with no `Message`, no `ResultProcessor` and no result box in it, and comes up
   properly: authenticated, protocol negotiated, named, on the right database. Outstanding before it is a
   *replacement* rather than a demonstration: reconnect, the backlog, and the profiling hooks.
4. ~~**Routing executors**: multiplexer (slot) and group (active node).~~ **Done** — all three of §3b now
   exist and stack: `RespEndpointExecutor` owns a connection's life, `RespMultiplexerExecutor` picks an
   endpoint by slot, `RespGroupExecutor` picks a member. `MultiGroupDatabase.GetContext()` no longer
   throws — it returns a context over a group executor, verified against a real group. `Publish` is the
   remaining fallback candidate here. (`IsConnected` and the endpoint-identity members came off earlier, as executor capabilities —
   they did not need routing, only somebody to ask.) **The server-endpoint executor is done**:
   `RespEndpointExecutor` owns a connection's whole life — connect, handshake, notice death, reconnect,
   and hold a backlog meanwhile. That also closes phase 3's outstanding reconnect and backlog items; what
   remains there is the profiling hooks.
5. **Batch, then transaction**, as decorators. **Batch done** — `RespOperationBatchExecutor`, and §3c's
   prediction held: see §7p. Transaction is the remaining half. `RespBatchExecutor` is replaced by the `BatchConnection`
   shape; `IRespRunExecutor` is deleted rather than fixed.
6. **Retire the shim**: `RespMessageExecutor`, `FrameMessage`, `FramePairMessage`, `FrameRunMessage`,
   `PayloadProcessor` and its copy-per-reply.

`TransitionalDatabase` is unaffected throughout — it talks to the context surface, which talks to
`RespExecutorBase`. That is the seam that makes this a replacement rather than a rewrite.


### 7a. What phase 1 changed about the plan

Three things the port did not inherit unaltered, each found by building rather than reading:

**Version and flags must share one word.** Claiming the outcome has to check "nobody else has completed
this" *and* "this caller is not holding a handle to a previous life" as one atomic step. With the two
kept separate — as they were — a stale completer can read a matching version, be pre-empted while the
instance completes and recycles, and then win the claim on somebody else's command. Packing the version
into the high half of the flag word makes a single CAS cover both. A mutation removing the version check
fails a test, so the guard is real rather than decorative.

**`IsRecyclable` is not `IsCompleted`.** §6's pooling policy was prose; it is now a parameter.
`TrySetException(token, ex, definite:)` distinguishes a server error (the command was answered — recycle)
from a connection fault (the write may still be in flight — do not). Timeouts are never definite. Without
this the natural implementation dooms *every* failure, which is safe but pools nothing, or recycles them
all, which is how a reply lands on somebody else's command.

**The parse capability outlives the life.** It describes the type, not the request, so `Reset` has to
re-apply it. The straightforward implementation clears the whole flag word — and then every command after
the first on a recycled instance silently returns `default`. This is precisely the §4 hazard ("a recycled
operation must not leak a previous life's state into a new one") pointing the other way: the danger is not
only *keeping* too much, it is *dropping* too much.

Two smaller repairs, both in the same family — something outliving the reset that created it:

- `Reset` cleared the request buffer fields outright, stranding a writer that still held a reservation:
  its `ReleaseRequest` then had nothing to hand back, so the rented array leaked instead of pooling. It
  now drops only its own reference, and whoever releases last clears the fields.
- `ConfigureAwait` rebuilt the handle by re-reading `message.Token`, which would silently rebind a stale
  handle to a later life instead of failing.

**Method to carry forward:** each of these was checked by deliberately re-introducing it and confirming a
test failed. Three mutations, two caught, one not — and the one that was not is why the writer-outlives-reset
test exists. Worth repeating on phases 2-6, because this is diagnostics-adjacent code where nothing fails
loudly when it is wrong.


### 7b. What phase 2 decided

**The inventory is one struct, not seven fields.** `Reset` has to be exhaustive, and §4 says why: a
recycled operation that leaks a previous life's timestamps produces a timeout report describing the wrong
command — which is worse than no report, because it is believed. Seven fields means seven lines and
forgetting one is invisible; one struct means `_diagnostics = default` and the compiler owns the
completeness. A mutation removing that line fails a test.

**The split with the host is "what the write path knows".** RESPite cannot see `ProfiledCommand`,
`PhysicalConnection` or `CommandStatus`, and should not model them. So the struct holds what the write
path itself produces — created stamps, the status ladder, byte counters at enqueue, the write tick, the
high-integrity token — plus two opaque `object?` slots: `EnqueuedTo` for the connection and `HostState`
for the host's per-command object. The host composes; it does not reach in and read fields.

**`RespCommandStatus` mirrors the shipped enum exactly, ordering quirk included** (`Sent` = 2 numerically
before `WaitingInBacklog` = 3). A public shipped enum cannot be reordered, so making the mapping an
identity beats making it a `switch` somebody has to keep correct. Pinned by a test.

**`IsKnownNotApplied` splits in the middle, and the split is principled.** The status half — never handed
to a socket, so the server cannot have applied it — is the write path's, and lives here. The error-kind
half — which server errors describe the server's own state rather than a script that failed part-way
through, having already written — is error taxonomy, and stays in the host's `FaultContext`. This is the
part §4 flags as *not merely diagnostic*: `RetryPolicy` reads it to bypass the side-effect cap, so losing
the ladder makes retry silently more conservative. A mutation dropping the `Sent` transition fails three
tests.

**Only the operation's half of the report is rendered here.** The text people recognise (`inst`, `qu`,
`qs`, `aw`, `bw`, …) is mostly multiplexer and connection counters. `Describe(StringBuilder)` emits what
the operation itself knows and nothing else, which is what keeps the boundary honest.


### 7c. The one that mutation testing caught, and nothing else would have

Worth recording as method rather than as a bug. `RespConnection` held `RespScanState` in a **field**,
with a comment asserting that this was what let a frame split across several reads resume instead of
restarting. Every test passed. Mutating it — resetting the state on every read — *also* passed, and that
is the signal: a mutation that changes nothing means the claim is untested, and here the claim was also
simply **false**.

Bytes are consumed only when a frame *completes*, so an incomplete frame leaves its bytes in the buffer.
Re-feeding those bytes on the next read to a scan state that has already counted them double-counts the
aggregate depth. A bulk string survives it — one length prefix and a payload — which is exactly why the
original split-frame test passed. An aggregate does not.

The replacement test splits a 3-element array at chunk sizes 2, 3, 5, 7 and 11: **all five fail** against
the field version and pass against a fresh per-attempt state. Carrying scan state is for a reader that
does not retain what it has scanned; this one retains.

**The general rule this suggests for the rest of the phases:** a mutation that fails nothing is a finding,
not a pass. Two of the nine mutations run so far landed there, and both times the code was wrong — once
leaking a request buffer, once double-counting a frame. Neither would have been found by reading, because
in both cases the comment explaining the code was the thing that was wrong.


### 7d. Two boundaries phase 3 settled

**Error classification belongs to whoever turns a frame into a result.** This surfaced as a real
inconsistency rather than a design question: a context over a raw executor throws RESPite's
`RespException` for an error reply, while the same context over the message pipeline throws
`RedisServerException`, because `ResultProcessor` converts before a payload is ever produced. Two
producers, two answers, same surface. `RespConnectionExecutor` converts, for parity with what it
replaces — a caller catching `RedisServerException` today keeps catching it — and that puts the rule at
the SE.Redis/RESPite boundary, which is the only place that knows the taxonomy.

**A layer must not invent a failure it cannot describe.** `RespConnection.Send` originally faulted a
refused operation itself, with a generic `InvalidOperationException`. That exception won the outcome
claim, so the executor's own — which knows the command, the flags, and how far it got — could never be
set. Send now returns `false` and the caller owns the failure; `Close()` still faults what it has already
queued, because by then nobody else can. The general form: **the outcome claim is single-winner, so the
layer that can describe the failure best must be the one that claims it.**

A corollary worth stating, because it answers the "do cached values pay for this?" question by
construction: the diagnostics live on the *operation*, and an operation exists only once something is
going to the pipe. A cache hit returns from `TryGet` before the executor is reached, so it stamps
nothing. That is a property of the layering, not a flag anyone has to remember to check.


### 7e. Measured: the premise holds, and what it costs

Run 2026-09-19, net10.0, server GC, local server; counter `INCR`, one key per worker (a shared counter
serialises on the *server*, which is the thing being controlled for), 3s per measurement. `toys/CoreBench`,
with `toys/CoreBench.Baseline` linking the **same harness** against the shipped package so the control
cannot drift from what it controls.

| arm | 1 | 4 | 16 | 64 | bytes/op |
|---|---|---|---|---|---|
| **3.3.0** (shipped) | 27,929 | 79,896 | 205,505 | 426,699 | 361–383 |
| **old** (this branch) | 26,942 | 82,188 | 204,441 | 425,607 | 361–375 |
| **new** (this branch) | 27,626 | 86,635 | 224,965 | **553,822** | 496.1 |
| **newcache** (cacheable `GET`) | 7,667,317 | — | 79,032,968 | 89,931,589 | **0.0** |

**3.3.0 and this branch's old path are identical within noise**, at every worker count and in
allocation. That is the control everything else rests on: new-vs-old here is a valid proxy for
new-vs-shipped, because nothing about the old path has moved.

**The contention prediction in §3b was right**: +2.5% at one worker, +5.4% at four, +10% at sixteen,
**+30% at sixty-four**. Close single-threaded, gap opening with concurrency — which is what should happen
if the difference is that the old core serialises every argument inside its write lock and the new core
does not.

#### Where the bytes go

| arm | bytes/op | what the step adds |
|---|---|---|
| `noop` | 0.0 | the harness is free, so every number below is real |
| `yield` | 96.0 | **the caller's own async state machine**, for an await that genuinely suspends |
| `render` | 48.0 | the per-request lease |
| `direct` | 249.1 | +105 connection and operation, including the thread-pool work item |
| `exec` | 321.1 | **+72** `RespPayload` + `RefCountedBuffer` |
| `new` | 496.9 | **+176** the context surface's own async state machine |
| `newcache` | 0.0 | the same surface, when nothing suspends |

Of the new core's ~497 bytes, **~300 is addressable and none of it is the core design**:

- **~96 is the caller's**, not ours. Any suspending `await` boxes its state machine; 3.3.0 pays it
  identically. Not recoverable, and not a difference.
- **~176 is the surface's async state machine** on the suspending path — the largest single item, and the
  most promising. `newcache` at 0.0 proves it is *only* the suspending path: the same handler, parse and
  `ValueTask` machinery allocates nothing when the executor completes inline. A pooled async method
  builder, or a shape that avoids the async frame on synchronous completion, is the lead.
- **~72 is `RespPayload.Create` copying the reply**, already documented in-source as scaffolding for
  sharing the receive buffer's lease.
- **~48 is the per-request lease**, poolable.
- **~105 is the connection and operation path**, including the thread-pool work item that exists
  *because* continuations are queued rather than run inline. Not a bug to fix — that is thread theft's
  price, and the existing core pays it too.

#### Update: the async state machine, pooled

The ~176-byte item above was the state machine of the send path's awaiting tail, boxed because the await
suspends. `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]` on those tails
(net6.0+; down-level keeps the ordinary builder) pools the box. Re-measured in one session, so the
comparison is not across machine states:

| arm | 1 | 4 | 16 | 64 | bytes/op |
|---|---|---|---|---|---|
| old | 27,891 | 83,072 | 205,099 | 447,213 | 361–375 |
| new | 28,084 | 84,566 | 225,999 | **549,626** | **296.1** |

**The new core now wins on both axes**: +23% throughput at 64 workers, and −21% allocation at every
worker count — flat, and still dying entirely in gen0 where the old core promotes (30 gen1 and a gen2 at
64 workers). Throughput improved slightly too, which is the opposite of what a pooling scheme usually
costs; the box was being allocated and collected on the hot path, and not doing that is cheaper than
doing it.

**The price, stated rather than buried:** the returned `ValueTask<T>` becomes single-consumption.
Awaiting twice now throws where a `Task`-backed one tolerated it. `ValueTask<T>` has always documented
that awaiting more than once is illegal, so this *enforces* the existing contract rather than narrowing
it — but it is a behaviour change for anyone who was relying on the forgiving implementation, and it is
the sort of thing that shows up as a bug report rather than a compile error. `AsTask()` still works,
once.

Remaining after this: ~96 the caller's own (not recoverable, and not a difference — 3.3.0 pays it too),
~72 the reply copy that `RespPayload.Create` already calls scaffolding, ~48 the per-request lease, and
~80 in the connection and operation path including the thread-pool work item that buys us out of thread
theft.

#### Creep, which is the other half of the question

The new core's allocation is **flat at 496.1 and dies in gen0** — zero gen1, zero gen2 at every worker
count. The old core's varies between 322 and 383 and **promotes**: 29 gen1 collections and a gen2 at 64
workers. So on volume the new core is currently worse and on *accumulation* it is already better, which
is the distinction that matters for a long-lived server process.

#### What this is not yet

The new arm has no handshake, no `SELECT`, no reconnect, no backlog and no profiling hooks. None of those
distort a steady-state loop on a healthy connection, but they are real work that the old arm does and the
new one does not, so this is not yet a fair fight in either direction.


### 7f. The handshake got shorter, and the reason generalises

`ServerEndPoint.HandshakeAsync` sends `AUTH` and `CLIENT SETNAME` **twice** — once folded into `HELLO`,
once standalone — and the in-source comment is explicit that this is deliberate. It writes
fire-and-forget with no flush, so it cannot see whether `HELLO` was understood, cannot know whether the
server needs auth, and cannot know whether `CLIENT` is even available. It hedges because it is writing
blind.

`RespHandshake` is four sequential awaits, because the new connection can *read the answer*:

1. `AUTH` if a password is configured — **first**, because an authenticated server answers `HELLO` with
   `NOAUTH`, and asking for RESP3 before authenticating declines the protocol for the wrong reason.
2. `HELLO 3`, reading `proto` out of the reply. The server can decline two ways — an error, or a
   perfectly successful reply saying `proto 2` — and only the reply distinguishes them.
3. `CLIENT SETNAME`, tolerating failure: `CLIENT` can be disabled or renamed, and failing a handshake
   over a diagnostic nicety is the wrong trade.
4. `SELECT`, last, because it is the one step about this connection's *use* rather than its identity.

**The general point, which applies well beyond the handshake:** a large amount of the old core's shape
exists because it writes without being able to wait. Multi-message expansion inside the write lock, the
`NoFlushFlag`, inferring the protocol from a tracer's reply, sending commands speculatively in case they
are needed — these are all consequences of the same constraint. As pieces move onto a core that can
await, expect several of them to collapse rather than port.

Proven against real servers, including the secure one: RESP3 negotiated, RESP2 when not asked for,
`SELECT` isolating a key from a second connection on database 0, and `AUTH` succeeding where a control
test confirms the server genuinely refuses an unauthenticated connection.


### 7g. What the endpoint executor will not do for you

`RespEndpointExecutor` reconnects lazily — a send finds no connection and starts one, and everybody
arriving during that attempt waits on the same attempt rather than dialling their own. There is no
background loop courting an endpoint nobody is using.

**The interesting decision is what happens to a command that was already on the wire.** It is *not*
replayed, and that is a deliberate refusal rather than an omission. The bytes reached a socket, so
whether the server applied them is unknown; re-sending an `INCR` behind the caller's back would double
it. Deciding to retry is the retry layer's job, which has the flags and the retry category and can tell
a read from an accumulating write. This layer's job is to be *honest about what happened* — which is what
`RespCommandStatus` and the definite/indefinite split exist for.

The real-server test makes the consequence explicit rather than hiding it: kill the socket abortively,
and a command issued in the window before the read loop notices **fails**. It is the command after that
which succeeds, on a freshly handshaked connection. The first version of that test asserted recovery on
the very next command and failed — correctly, and the fix was to the test, not the code.

**What a backlogged command gets that an in-flight one does not** is certainty. It never reached a
socket, so it is marked `WaitingInBacklog`, and `FaultContext.NotApplied` reads exactly that to bypass
retry's side-effect cap. The diagnostics built in phase 2 are what make the distinction expressible; this
is the first place that actually depends on them.

A failed *connect* fails everything waiting on it rather than holding it for a later attempt: a queue
that grows without bound while a server is down is a worse failure than a fast one, and the caller (or
the retry layer above) is better placed to decide how long to keep trying.


### 7h. Routing, and the state that is neither true nor false

#### The ordering contract, which bounds everything below

**Per-connection FIFO is the primitive, and per-slot ordering is all that can ever be built on it.** Not
"all we manage for now" — Marc's point, and it is the ceiling for any multiplexed client against a
distributed store. Commands for different slots go to different nodes and execute concurrently; there is
no global order to preserve and no mechanism that could preserve one. So the contract is:

> Commands issued on one connection are executed in the order issued. Commands for one slot are executed
> in the order issued **for as long as that slot's owner does not move**. Nothing is promised across
> slots, and nothing stronger is available.

Three things follow, and they explain design decisions that otherwise look unrelated:

- **A cross-slot command is refused rather than split.** There is no order in which to do the halves that
  means anything, so refusing is the honest answer — `RespMultiplexerExecutor` throws rather than guess.
- **Redirects cannot break a guarantee that was never made across slots.** They can only damage ordering
  *within* a slot, which is exactly why handling them in the IO loop matters and why the damage is
  confined to the transition boundary below.
- **A reshard is allowed to hurt.** It moves a slot's owner, which the contract explicitly does not cover.
  That is the same reasoning as a cluster/standalone reconfiguration being allowed to bump.

Phase 4's multiplexer executor makes one decision — which endpoint — and hands the whole command to that
endpoint's executor. **The non-cluster path costs a volatile read and a field read**, and not only in the
executor: the request never had a slot computed, because `RespRequestBuilder` consults the same topology
before hashing a key. Outside cluster the whole apparatus is one branch that is always false.

**The constraint that shaped it, from Marc:** a multiplexer can be constructed — and `GetDatabase()`
called, and a context built and *memoised* — while still disconnected, so nobody knows yet whether this
is a cluster. Anything that decided routing at that moment would be wrong for the life of the
multiplexer. So topology is a cell the executor reads per send, never captures.

**And it has three states, not two**, which is the part that is easy to get wrong. With a plain bool
defaulting to "not a cluster", a request rendered before the answer arrived carries no slot — so when the
answer arrives a moment later, that request cannot be routed. `Unknown` computes slots *speculatively*:
a hash per key during the brief window before the first connection reports, in exchange for there being
no window in which a request exists that cannot be routed.

The two questions are deliberately separate properties, because they have different answers while
unknown:

| | while `Unknown` | why |
|---|---|---|
| `NeedsSlots` — "might this matter?" | **true** | computing a slot nobody needs is merely wasted work |
| `RoutesBySlot` — "does this decide where it goes?" | **false** | acting on a guess is not wasted work, it is wrong |

**What is *not* designed for:** a deployment genuinely changing between cluster and standalone. Marc's
call, and the right one — that is a reconfiguration, reconfigurations are expected to be disruptive, and
paying on every command forever to make a never-event seamless is a bad trade. In-flight commands may be
routed on the old answer, redirected, or fail and be retried.

**The alternative worth recording**, also Marc's: skip the speculation entirely, route unknown-topology
commands anywhere, and let `-MOVED` correct them. Very likely the right end state — redirect handling is
needed *regardless*, since a reshard moves slots under a running client and nothing done at connection
time helps with that. But the new core has no redirect handling today, so that is a promise rather than a
mechanism: those commands would surface the redirect to the caller as an error. The speculative hash
makes the window correct *before* redirects exist, and `Unknown` is deletable once they do.

**The redirect cost is ordering — but only at the boundary.** The blunt claim ("a redirect loses
ordering") is wrong, and the correction is Marc's: if a redirect is handled *in the IO loop*, in reply
order, then commands redirected **together** keep their order. They were sent to the wrong node in order,
that node answers `-MOVED` to each in the same order, and re-enqueueing as the replies arrive puts them
on the new connection in the caller's original order. "In the IO loop" is load-bearing — resubmitting
from arbitrary threads would lose it even here.

What genuinely inverts is the **mixed** case, which is precisely the discovery boundary: one command
issued while the topology was unknown takes the slow path — wrong node, redirect, new connection — while
the next, issued a moment later with the answer in hand, goes straight to the owner. `INCR k` then
`GET k` across that boundary can have the `GET` complete while the `INCR` is still in flight, and read
the value from before its own write.

**What keeps this design out of that case is an invariant, and it deserves stating rather than being
relied on quietly:** `Unknown` means no connection has reported, which in practice means there is no
connection — so commands issued then are *backlogged, not sent*, and drain in arrival order once the
topology is known and their speculative slots become meaningful. No redirect, no inversion.

The invariant breaks if a connection is ever brought up and used while its server type is still unset.
So: **whoever owns an endpoint must set the topology as part of bringing a connection up, before draining
the backlog.** *Done* — `RespHandshake.PerformAsync` now takes the topology cell and sets it before it
returns, which is before the endpoint executor publishes the connection and drains anything. The rule is
structural rather than remembered.

It costs no extra round trip in the normal case: `HELLO`'s reply already carries `mode`
(`standalone`/`sentinel`/`cluster`) alongside `proto`, so one command answers both questions. A server
with no usable `HELLO` falls back to `CLUSTER INFO` and `cluster_enabled:1`, which is unambiguous and
works on RESP2 — and a server where `CLUSTER` is unavailable is, by that very fact, not a cluster.
Verified against a real cluster node as well as a standalone one, because only the cluster case proves
the `mode` parsing and the latch to `RoutesBySlot`.


### 7i. Three executors, and what stacking them showed

All three of §3b now exist, and the thing worth recording is how little the outer two do.

| executor | its entire job |
|---|---|
| `RespEndpointExecutor` | own one connection's life: connect, handshake, notice death, reconnect, backlog |
| `RespMultiplexerExecutor` | pick an endpoint — by slot when slots mean anything, otherwise the one |
| `RespGroupExecutor` | pick a member |

The outer two are a resolution and a delegation. Neither repeats anything about connecting, retrying, or
queueing, because the thing they resolve *to* already does it. That is the structural argument for the
topology being right, and it only becomes visible once they are stacked: a group over a multiplexer over
an endpoint is about fifteen lines of actual decision-making end to end.

**`MultiGroupDatabase.GetContext()` used to throw** *"not yet wired for multi-group"*. What it needed was
exactly one thing: an executor that resolves the active member **per send** rather than at construction —
because the context is built once and memoised, while the active member is the thing a group exists to
change. That is the same property the multiplexer executor needs for topology, arrived at from a
different direction, which is usually a sign the shape is right.

It now works against a real group, proven by running the old surface and the new one against the same
group and the same key. That is the seam through which `[AutoDatabase]`'s 628 generated forwarding
members for this type eventually leave: each exists to capture a command's arguments, resolve the active
member, and replay the call — and once the resolution is an executor *below* the command surface, there
is nothing left for them to do.

**One wrinkle, and the first version of it was wrong.** I had the context skip caching when no member was
active, on the grounds that the command map could only be learned from one. Marc: *the command map comes
from config, not any member* — each group member is configured separately, and the group reads the map
from that configuration, so it answers with nothing connected at all.

What genuinely *does* need a member is the **database index**, when it was defaulted: `-1` means "whatever
the active member defaults to". An explicit index needs nobody, so that context is fully determined and
cacheable while everything is down; a defaulted one is not, and throws — which is exactly what the
shipped `Database` property already does in that state, so the context surface invents no new failure
mode. Both halves are pinned by tests against a group pointed at a dead port.


### 7j. Where the client-side cache fits in a group

Marc's question, and the answer turns out to be forced rather than chosen.

**A cached reply is only sound while two things hold**: the connection that produced it is the one being
asked, and that connection's `CLIENT TRACKING` registration has been continuously live. A group breaks
the first one by design — the active member is the thing it exists to change.

So **the cache is the active member's, resolved per command.** A shared group cache would serve member
A's value while B is active, silently, and no amount of flushing makes that safe in general. The
alternatives fall out:

| option | verdict |
|---|---|
| one cache, shared | **wrong** — serves one member's values from another |
| one cache, nuked on switch | correct, but throws away a warm cache on a *healthy* switch (latency, explicit failover) |
| per-member, resolved per command | correct, and a switch is a no-op |

The third needs nothing new for invalidation, which is the pleasing part: the existing rule — *flush when
the tracking connection drops* — already covers the case that actually matters, because a failover
usually happens **because** the old member's connection died, and that flushes its cache on the way past.
A switch away from a healthy member leaves its cache warm for switching back.

**The gap this question exposed:** the group's context was built with no cache at all, so the new surface
over a group did no caching whatsoever. The member's own cache sits *below* the probe point — the probe
happens in the context surface, above the executor — so delegating the send to the member's executor
never reaches it. `WithCacheResolver` fixes it the same way topology was fixed: resolved per command, and
`null` for every other context, which costs one predictable branch on the hot path.


### 7k. Does knowing the replication topology change the cache rule?

Marc asked whether the answer differs if we positively knew we were in (1) active-active, (2)
geo-replicated with the secondary a replica of the primary, or (3) independent deployments. Nothing in
the codebase models this today, so it would be new configuration — worth knowing before adding it.

**The answer is no, and the reason is better than the one §7j gives.** §7j argues from "the data might
differ". That is true but incidental. The real rule:

> **A cache entry carries an implicit subscription, and subscriptions do not transfer.**

`CLIENT TRACKING` registration is per *connection*. Inheriting A's entries while talking to B means
inheriting **no registration on B** — so future invalidations for those keys never arrive, and the entry
is unfalsifiable rather than merely possibly-stale. That holds even if B's data is byte-identical to A's,
which is exactly the case the three scenarios are asking about.

What each scenario *does* change is elsewhere:

| | data relationship | what actually differs |
|---|---|---|
| **Active-active** | converges asynchronously | invalidation latency is bounded by *replication lag*, not network RTT — a write at B reaches A's tracking only once it replicates. Bounds safe `MaxCacheAge`. |
| **Geo-replicated** | secondary is a replica | reads from the secondary are already stale; the cache does not make that worse. The tempting error: A's cached values are *fresher* than B's, so serving them looks like an upgrade — but they carry no B subscription, and after a promotion with unreplicated loss they are from a diverged timeline. |
| **Independent** | unrelated | nothing shared, nothing to reason about. |

**And the counter-intuitive part:** per-member caching matters *more* when the topology is "related" than
when it is independent. With independent deployments, a shared cache gives obviously-wrong answers that
are found in the first hour. With active-active or geo-replication it gives **plausible** wrong answers —
right key, right shape, slightly wrong value — which is the kind that survives to production.

So if this is ever modelled, the useful knob is a `MaxCacheAge` ceiling derived from expected replication
lag. Not a change to what happens on a switch.


### 7l. Following a redirect

The half that makes cluster routing real rather than nominal. `RespRedirect` reads a `-MOVED`/`-ASK`
reply; `RespRedirectingConnection` offers it to a router before the operation is completed; the
multiplexer executor resolves the target and re-issues.

**It happens on the IO loop, which is a correctness requirement.** Commands redirected together were sent
to the wrong node in order and answered in that order, so re-issuing as the replies arrive puts them on
the new connection in the caller's original order. Handing the work to a thread pool loses exactly that,
and per-slot ordering is the strongest guarantee this client has (§7h).

Four decisions, each of which had a wrong-looking easy version:

**`MOVED` updates the slot map; `ASK` must not.** That is the entire difference between them. `ASK` says
*this key* is mid-migration; updating the map from it would point every subsequent key in the slot at a
node that only holds the ones already migrated.

**`ASKING` is written with the command as one write, not two sends.** The server applies it to the very
next command on that connection, so anything interleaved receives it instead. Two `Send` calls cannot
promise adjacency — another sender takes the write lock between them — hence `RespConnection.Send(first,
second)`.

**And `ASKING` is declined when there is no live connection, rather than backlogged.** A backlog drains
one operation at a time, so the pair would lose that adjacency on the way out. Declining lets the
redirect stand as the error it arrived as, which is also the more honest answer: the node we were told to
try is not reachable.

**Once is enough** — Marc, from the shipped logic, which sets `NoRedirect` when it re-issues. A second
redirect for the same command is pathological rather than routine: two nodes that disagree, or a topology
changing faster than commands complete. Short-circuiting means the caller sees the server's own error,
which says more than a redirect loop or a hang would. (Note the shipped version applies it to `ASK` as
well as `MOVED`, and additionally marks `MOVED` re-issues as internal calls.)

**A bug this nearly had, caught by remembering §4:** `HasFollowedRedirect` lives on a *pooled* operation,
so without an explicit clear on `Reset` it becomes the next command's starting position — silently
refusing to follow a legitimate redirect for whatever reused the instance. Exhaustive reset is not a
tidiness rule.

**And a leak it did have:** `ASKING` has a reply, and nothing was awaiting it — so its operation was
never consumed, never reset, never returned to the pool, and never released the pooled buffer holding its
request. It is now drained explicitly.


### 7m. The new core, in front of the existing suite

`RespNewCore` builds the whole new chain — operation, connection, transport, handshake, endpoint and
multiplexer executors, redirects — over a real `ConnectionMultiplexer`, and eight existing suites now run
against it: **888 tests, no `Message` anywhere in the send path.**

**What it borrows and what it replaces is the point.** Topology and configuration stay with
`ConnectionMultiplexer`: which endpoints exist, which node owns a slot, what the credentials are.
`ServerSelectionStrategy` already maintains the slot map from `CLUSTER NODES`, keeps it fresh across
reshards, and knows about unreachable nodes — a second map maintained by the spike would be a second
thing to get wrong and would not make the *send path* any more correct. Only sending is new.

This is the same argument `TransitionalSurfaceFixture` makes one layer up, and for the same reason:
assertions written for the shipped library know far more about what it must do than anything written
alongside a spike.

**It found a real gap immediately**, which is the whole return on the exercise:
`SortedSetRangeStoreFailForReplica` failed because **the new core ignored replica flags entirely**. A
write demanded on a replica must be *refused*, not routed — and the rule about which commands are
primary-only already exists and is shared with the interpolated writer (`Message.DemandPrimary`), so the
executor only had to ask rather than decide. That is now enforced where routing decides.

**Widening it to 24 suites found two more**, which is the loop working:

- **Replica preference was not honoured at all.** The resolvers took a slot and nothing else, so
  `PreferReplica` routed to the primary. Fixed by passing the *command and flags* to the resolver:
  choosing a node is not only a question of where the data is, and `ServerSelectionStrategy` already
  knows which endpoints are replicas and which are reachable. Passing a slot alone silently discarded
  half the question.
- **The server-feature probe was not wired**, and that is not a missing nicety. Several commands are
  *chosen* from what the server supports — an all-GET `BITFIELD` goes out as `BITFIELD_RO` when
  available, which is what lets a replica serve it. Without the probe the surface reports "unknown",
  picks the writable command, and a caller who demanded a replica is then refused *for a read*. The
  selection logic handles "unknown" gracefully; it simply answers a question nobody had asked properly.

**`BitTests` is deliberately not in the set**, and the reason is the honest one:
`BitFieldAllGetGoesOutAsReadOnlyAndReachesAReplica` asserts through a `ProfilingSession`, and the new
core feeds no profiling at all, so the session comes back empty. That is a real missing feature rather
than a quirk of the test — `performance (ProfiledCommand)` is on §4's inventory, and
`RespOperationDiagnostics` already reserves `HostState` for it. Bolting on just enough to make one
assertion pass would be building the feature backwards. **Adding that suite back is the check that
profiling has landed.**

Maintenance events and sentinel are likewise untouched; the suites that pass are the ones whose
assertions do not depend on any of it.


### 7n. One resolution, three questions — and why a hard pin would be a lie

Three questions were each walking the executor chain separately: *can you reach this key?*, *which
endpoint would take it?*, and *what can the server that answers it do?* Six near-identical overrides
apiece across seven executors; a third question would have made eighteen.

They are the same walk with a different question at the bottom, so there is now one primitive:

```csharp
internal virtual RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags);
```

A **decorator** (retry, batch) forwards, because it changes nothing about where a command goes. A
**router** (group, multiplexer) resolves one step and recurses. An **endpoint** is the answer, which is
why the default returns `this`. The questions then sit on the base and come free.

**Why the feature probe matters more than it sounds.** Several commands are *chosen* from the answer: an
all-GET `BITFIELD` goes out as `BITFIELD_RO` when the server has it, which is what lets a replica serve
it. Answer "no idea" and the writable spelling goes out — which works everywhere, except that a caller
who asked for a replica is then refused *for a read*. A guess here is not a missed optimisation; it is a
client-side refusal of a legal command.

The new core now answers from **its own handshake**: `HELLO` already reports `version` alongside `proto`
and `mode`, so the endpoint that will run the command reports what it observed on that very connection,
rather than borrowing a version from somebody else's topology.

#### The race, and why pinning does not fix it

The probe runs at *render* time; the send happens later. They can resolve to different servers. Marc:
*"I'll settle for 'we made a good decision based on the information we had'"* — and that is the right
call for a reason stronger than pragmatism:

**A hard pin would be a lie.** Capturing the chosen server at render time narrows the window without
closing it — a reshard or failover between capture and send makes the capture stale, and now it is
*confidently* stale. Worse, it fights the principle the routing is built on: topology is read per send
and never captured, precisely because the context is memoised for the life of the multiplexer. Pinning
reintroduces the staleness the tri-state topology (§7h) exists to avoid.

`-MOVED` is the proof that pinning was never the mechanism. The cluster tells us when we were wrong, and
following that correction — in reply order, on the IO loop (§7l) — is what actually makes a stale
decision recoverable. A client that pinned *and* followed redirects has two mechanisms where one would
do; a client that pinned *instead of* following them is broken by the first reshard.

So the contract is: decide well from what is known, be correctable, and never pretend the decision was
more certain than it was.


### 7o. Profiling, and how little RESPite needed

Phase 3's last outstanding item. `BitTests` is back in the new-core set, which was the agreed check:
`BitFieldAllGetGoesOutAsReadOnlyAndReachesAReplica` asserts *through a `ProfilingSession`* which command
reached which endpoint, so it fails flat against a core that feeds none. `ProfilingTests` runs against
the new core too.

**A second door on `ProfiledCommand`, not a second type.** Three of its properties reached through to a
`Message` for `Db`, `Command` and `Flags`; they now fall back to fields that `SetOperation` fills.
Everything a profiled command reports is already carried by the operation's diagnostics — what it lacked
was a `Message` to read them *from*. The timing methods needed no change at all: they were never about
`Message`.

**The record starts at the endpoint executor, not at render.** A profiled command reports which server
answered it, and until routing has resolved there is no honest answer to that.

**RESPite needed 27 lines, which is two empty virtuals and two call sites.** Marc was right to be
surprised, and the first attempt had three; the third was convenience and is gone. What remains:

| hook | why it is in RESPite |
|---|---|
| `OnSent` | it fires exactly where `Diagnostics.Status` becomes `Sent` — RESPite already records that moment for its own reasons, so observing it costs one call beside a line that was already there. Doing it from outside means re-deriving "when was it sent" at four call sites, less accurately and easy to miss when a fifth appears. |
| `OnFinished` | **the one that cannot be done from outside.** It must fire for every ending — reply, server error, cancellation, timeout, connection fault — *and* for fire-and-forget commands nobody consumes. `OnReset` comes closest but fires on consumption, which those never reach. |

The one that was dropped, `OnResponseReceived`, is now stamped in `RespRedirectingConnection.TryHandOff`:
every frame matched to an operation already passes through there, in SE.Redis, so RESPite never needed to
know.

The general test this suggests: **a hook belongs in RESPite when RESPite already knows the moment, or
when no layer above can see all the cases.** Convenience does not qualify.


### 7p. Batch, and the machinery that did not need writing

§3c predicted that once operations complete themselves, the batch API's hardest problem stops existing.
It did.

The earlier attempt needed a `TaskCompletionSource` per element, a `Span<ValueTask<RespPayload>>`
out-parameter, and a scatter-back into caller positions — all to answer *"how does element 3 of 5 get its
result out?"*. **409 lines.** The operation-based version is **190**, and most of the difference is that
the question does not arise: element 3 *is* an operation, whose completion is itself. There is nothing to
hand back, so there is no return channel to design.

What a batch actually needs from a connection turned out to be one method — write N operations with
nothing of anybody else's between them — which is the N-way form of the pair `ASKING` already needed.

Three decisions worth keeping:

- **Grouped by slot, not by server.** Grouping by server races a reshard: the map can change between
  grouping and writing, and the group is then split across nodes with no way to tell. A slot is a
  property of the keys themselves and cannot move underneath the grouping; *where* that slot lives is
  resolved once, at dispatch. Outside cluster everything is `NoSlot`, so it is one group and the cost is
  a walk comparing ints.
- **Declined rather than backlogged when disconnected**, for the same reason `ASKING` is: a backlog
  drains one operation at a time, which is exactly the adjacency a batch is asking for. A batch that
  cannot be written contiguously is not a batch, so saying no beats quietly issuing it as a pipeline.
- **A failed element disturbs nothing else.** A server error on element two leaves one and three alone,
  because they were never sharing a result channel to begin with.

Still to do for the *public* `IBatch`: the surface itself. `IBatch` is `IDatabaseAsync` plus `Execute`,
so the shape is a context whose executor accumulates — which is what this is — wrapped in something that
implements the interface. That is forwarding, and forwarding is what `[AutoDatabase]` is for.


### 7q. Cancellation: captured, tested, and currently unreachable

Marc asked whether the cancellation work from the sidelined v3 branch survived the port. **It did** — §2
listed it under *Take*, and it landed in phase 1:

- a `CancellationTokenRegistration` taken at `SetRequest`, against the operation itself;
- unregistered on **any** definite outcome, not just a cancellation, so a completed operation stops
  holding a registration on somebody's long-lived token;
- `TrySetCanceled`, which competes for the same single-winner outcome claim as a reply;
- cleared by `Reset`, so a recycled operation does not inherit the previous life's token.

Four tests cover it, including the two that matter for a pooled, racing design: a reply and a
cancellation arriving together (run 500 times — *exactly one* wins, never both, never neither), and a
token cancelled after the operation has been recycled, which must not touch whatever is using the
instance now.

**But it is not reachable from the surface**, and that is the part worth recording rather than
discovering later. `RespExecutor.SendAsync` still calls `DemandNoCancellation`, which throws
`NotImplementedException` for any cancellable token — a gate added when the only executor was the
`Message` shim, whose pipeline genuinely cannot cancel an in-flight request. The new core threads the
token all the way into the operation (`Attach(request, flags, cancellationToken)`), so the capability is
live and tested; the gate above it means no caller can ask for it.

**Done, 2026-09-20.** `DemandNoCancellation` is now `DemandCancellable`, which asks
`RespExecutorBase.CanCancel` instead of refusing everything. The default is **false** — the honest answer
for anything on the classic pipeline, which cannot withdraw a request that has reached the socket, and
cannot ignore the reply that is coming without desynchronising every reply after it. The connection and
endpoint executors answer true; routers and decorators forward, because whether a command can be
cancelled is a property of whatever finally sends it.

Cancelled-*before*-we-start is still honoured whatever the executor says, since refusing to begin costs
nothing — and it is checked first, a cancelled token being also a cancellable one.

Proven against a real server: a pre-cancelled token throws without sending, a live token is accepted and
the command completes, and cancelling afterwards changes nothing because the registration is released on
any definite outcome. The `Message` shim still refuses, and a test pins that it does — the capability is
per-executor, not a flag that got flipped globally.

### 7r. Transaction: the handshake collapses, and a lie about "no endpoint"

Batch (§7p) needed no return channel — each operation's own reply *is* its result. `MULTI` is the first
thing on this core where that stops being true: the server answers each queued command with `+QUEUED`
and holds the real results until `EXEC`, which returns them as one array. RESP itself multiplexes them,
so something has to distribute them back.

**Three pieces, and only one of them is new.**

`ExpectsQueuedReceipt` on `RespPayloadOperation` says "your first reply is a receipt, not your result".
`RespClientConnection`'s hand-off hook absorbs it and leaves the operation pending — the *same* hook that
absorbs `-MOVED`, because it is the same shape of question: this reply does not complete this operation.
RESPite needed nothing, which is now twice in a row (§7o, §7p) that a Redis-semantic feature landed
entirely in StackExchange.Redis.

`RespExecOperation` is the only genuinely new object: one operation whose `ParseFrame` walks the `EXEC`
array and hands element *i* to queued operation *i*. Distribution happens **once**, not as a completion
source per element. The elements are copied out rather than reserved against the reply's buffer —
retaining N slices of one frame would pin the whole array for as long as its longest-lived element, and
for the scalars a transaction usually returns the copy is much the cheaper end of that trade. Each
element is scanned with `RespScanState`, not sliced by hand: hand-slicing works for scalars and quietly
breaks on a queued `LRANGE`, whose element is an aggregate of its own.

`TrySendTransaction` writes `MULTI`, the queued commands and `EXEC` as **one contiguous run**. That is a
stronger requirement than a batch's: a batch wants its commands adjacent, whereas anything interleaved
between this `MULTI` and this `EXEC` would *join the transaction* rather than run beside it.

**The handshake §5 predicted would collapse, collapsed.** The old core's `TransactionMessage.GetMessages`
pauses for condition replies with `Monitor.Enter`/`Monitor.Exit` on result boxes, *inside the write lock*
— because an enumerator that blocks for a reply cannot hold that lock while the reader must make
progress. A core that can `await` does not need any of it: a pause is simply the boundary between two
contiguous runs, exactly the distinction §3c asked for. The same collapse as §7f, from the same cause.

**The bug worth recording.** The real-server test failed with "No endpoint is available to serve this
transaction" while connected to a working server. `TrySendTransaction` was a method on
`RespEndpointExecutor`; the test drove a plain `RespConnectionExecutor`, which inherited the base
implementation — and the base declines. Declining is reported as *no endpoint*, which was a lie: the
connection was right there and working.

Nothing about assembling the run is endpoint-specific. It needs a `RespConnection` and nothing else, so
it moved to `RespTransactionExecutor.TrySendOver`, and both executors call it. `TrySendBatch` had the
identical hole — a batch over a connection executor silently degraded to a pipeline — and got the same
fix. **A `virtual` returning `false` is a capability question that answers "no" by default, and every
type that could have said yes and did not is a silent downgrade.**

**So the rest of the base got audited.** `RespExecutorBase` has six such members. Four are safe *by
construction* rather than by anyone remembering: `TryGetLocalFeatures`, `TrySendBatch` and
`TrySendTransaction` are only ever reached through `ResolveFor`, which walks the routers down to a leaf
before the question is asked — the funnel §7m built for a different reason turns out to be what keeps
capability questions off decorators. `TryResend`/`TryResendAsking` bypass `ResolveFor` (the redirect path
resolves an `EndPoint`, not a key), but `RespNewCore.ForEndpoint` returns a `RespEndpointExecutor`
directly, so they land on a leaf too.

**`CanWritePreamble` is the one that is genuinely broken, and differently.** It is asked on the
*outermost* executor — `context.Executor` — not on a resolved leaf, because the pair has to be written by
whoever owns the write, and resolving first would answer a question about the wrong object. So every
router in the chain has to forward it; `RespRetryExecutor` does, `RespMultiplexerExecutor` and
`RespGroupExecutor` do not. But fixing the forwarding would change nothing today, because **no new-core
leaf implements it at all**: `IRespPreambleGate.IsNeeded` takes a `PhysicalConnection`, which the new
core does not have. RESPite has the primitive — `RespConnection.Send(first, second)`, already carrying
`ASKING` — and the gate is what cannot reach it.

The effect is bounded and not a correctness bug: `AwaitPair` falls back to sending the two in sequence,
which is semantically identical and one round trip worse. What it costs is that the gate is never
consulted, so an `EVALSHA` on the new core carries a `SCRIPT LOAD` it does not need *on every call*.
Tracked in the queue; the fix is to give the gate a connection identity both cores can supply, which is
a change to an interface the old core owns and so belongs with the deletion rather than before it.

`RespRedirectingConnection` was renamed to `RespClientConnection` in the same change, for the same
reason: it had grown a second job (receipts, not only redirects), and a bare `RespConnection` looks
interchangeable with it and silently is not — a transaction over one completes every queued command with
the string `"QUEUED"`.

**`WATCH` and conditions: two runs, and the connection has to be named.** A condition is evaluated
*before* `MULTI` and its answer decides whether the transaction is sent at all — the pause point of §3c,
and the first place two contiguous runs are genuinely required rather than one. Run 1 is the watches and
their checks, interleaved `WATCH k` / check / `WATCH k` / check, because a check that ran before its own
watch reads a value the watch is not yet guarding. Run 2 is `MULTI`…`EXEC`, sent only once every check
has answered.

**The thing that is new, and that the old core never had to face.** `WATCH` is *per-connection* state,
so run 2 has to happen on the same connection as run 1 — and between two awaits a reconnect is entirely
possible. Sending `MULTI`/`EXEC` on a fresh connection would run the transaction with its guard silently
gone, which is the one outcome a conditional transaction must not have. So the connection is **named**,
not merely used: `CurrentConnection` is captured before run 1 and compared before run 2, and a mismatch
fails the transaction rather than running it unguarded.

The old core never faced this because it never let go: `TransactionMessage.GetMessages` holds the write
lock across the whole thing and blocks for the condition replies with `Monitor` handshakes on result
boxes. Holding the lock *is* how it pins the connection. That is the trade — the old core bought
connection affinity with a blocked thread inside the write lock; this one buys it with an identity
comparison, and the reader never stops making progress. Same guarantee, and §7f's collapse again.

**Seven condition types carried over without a line changed.** `Condition.TryValidate(ref RespReader,
out bool)` already existed for `ConditionProcessor` and is exactly the shape `ParseFrame` wants. What had
to be added was rendering: `RenderCheck(RespContext)` and `WatchKey`, as a deliberate *twin* of
`CreateMessages` rather than a translation of it — a `Message` carries a database, flags and a result box
that a rendered frame has no use for, and going through one to get bytes back out would be a round trip
through the very type this work removes. When `Message` goes, `CreateMessages` goes with it and the twin
stays. Same story as `Message.GetPrimaryReplicaFlags` and `ServerSelectionStrategy`: the parts of the old
core that were about *Redis* rather than about `Message` survive the replacement.

A failed condition **cancels** the queued commands rather than faulting them — "a precondition did not
hold" means they did not run, which is a different outcome from failing — and `UNWATCH` is sent even when
nothing was queued, so a watch never leaks onto a pooled connection.

### 7s. `IBatch`/`ITransaction`: the gate opens, and where it stops

`CreateBatch`/`CreateTransaction` were the last two members on `TransitionalDatabase`'s fallback, and
they were the gate on everything: `RedisBatch : RedisDatabase`, so as long as a batch had to come from
the old surface, all ~504 of `RedisDatabase`'s members had to stay to serve it.

**`TransitionalBatch` is ~60 lines, and that is the whole argument.** It derives from
`TransitionalDatabase` over a context whose executor queues; every command member is inherited, and
nothing on the command path is told it is in a batch. That is the same `RedisBatch : RedisDatabase`
inheritance the new design exists to undo — and it is fine *here* for the reason it is not fine *there*:
`RedisDatabase` is ~6,000 lines of command implementations, so inheriting it pins every one of them,
where `TransitionalDatabase` implements nothing and is a funnel to a context. The inheritance buys ~504
members and costs nothing, because there is nothing behind it to be stuck with.

`TransitionalTransaction` adds three members. `AddCondition` hands back a `ConditionResult` *now* and
fills it in when the check is answered, which is what keeps "which condition failed?" answerable — a
single bool from `Execute` cannot say that, and by the time it is false the conditions are gone.
`WasWatchConflict` needed the core to distinguish a failed condition (the state was not what you wanted)
from an abort (it was, and somebody moved first); only the second is worth retrying, so `EXEC`'s null
reply now reports itself rather than being folded into the same `false`.

**Two things the generator and the surface gave up for free.** `[AutoDatabase]` matched interfaces by
*declared* name, so a class declaring `IBatch` got nothing generated — silently, since the members it had
not written were simply absent rather than throwing. Matching `AllInterfaces` fixes it: `IBatch` **is**
an `IDatabaseAsync`. And scanning inside a batch is now refused rather than offered broken: the cursor
for every page after the first is read out of the previous page's *reply*, which a batch has not sent.
The shipped surface inherits `RedisDatabase`'s implementation here and so offers a scan that cannot
advance.

**Where it stops, and this is the useful part.** Wrapping the existing `BatchTests`/`TransactionTests`
through `TransitionalSurfaceFixture` — the technique that made 27 other suites into proof — fails
wholesale, 436 of 442. The fixture builds over `RespMessageExecutor`, the shim onto the *old*
`PhysicalConnection` pipeline, and the new batch and transaction executors need a `RespConnection` to
write a contiguous run to. The shim has none, so both capabilities inherit the declining base.

That is a boundary, not a bug, and the distinction matters: for `MULTI`/`EXEC` **contiguity is the
semantics**, so falling back to sending the frames individually would let another caller interleave and
join the transaction — a correctness hole dressed as a degradation. Declining is right. What it means is
that these two suites become the proof only once `GetDatabase` is wired through `RespNewCore`, which is
now the next real step rather than a later tidy-up.

Both refusals also repeated §7r's lie — "No endpoint is available" when the endpoint is fine and the
connection is what is missing. Both messages now name the actual cause. That is twice this exact wording
has misled a diagnosis; a capability that declines should say which capability, not invent a topology
problem.

### 7t. The write slot, and three wrong guesses

§7s ended with "wire `GetDatabase` through `RespNewCore` and the batch and transaction suites become the
proof". They did: wrapping `BatchTests`/`TransactionTests` over the real core rather than the shim went
from 4/442 passing to **277**, and then to **437** once the failures were understood. What follows is
what the 163 were, because two of them were defects in the core rather than in the new code.

**Conditional transactions were not ordered against concurrent commands (161 failures).** Every one had
the same shape — `var exec = tran.ExecuteAsync();` not awaited, then `db.StringGet(key)` expecting to see
the transaction's effect. Unconditional transactions were fine, because they run to the write without
awaiting; with a condition there is a real pause for the checks, and anything written into it lands
*before* the `MULTI`. The shipped surface promises the opposite and keeps that promise by holding the
connection's write lock across the pause — which is precisely why `TransactionMessage` has to pause its
enumerator with `Monitor` handshakes on result boxes: the reader must still make progress to deliver the
replies being waited for.

The fix keeps the guarantee and drops the mechanism. An **ordered write slot** on the endpoint executor
blocks *writers* only: arrivals go to the backlog, which is the same queue a disconnected endpoint uses
and drains in arrival order, so per-connection FIFO comes from the mechanism that already guarantees it
rather than a second one. No thread blocks, the reader is never impeded, and the replies that release the
slot cannot be starved by it. The cost — other writes to that endpoint queue for one round trip — is
inherent to the guarantee, not to this design, and only conditional transactions pay it.

**Getting the slot right took three wrong guesses, and the shape of the error is worth keeping.** Each
time I reasoned from the code to a plausible cause, changed it, and re-ran to find the count unmoved:

1. *"A batch declines when the connection is not up yet."* True, and worth fixing — `TrySendBatch`
   returns a bool so it can only decline, and declining fails a caller whose batch merely arrived early.
   `PrepareRunAsync` now waits. Not the cause.
2. *"The connect publishes the connection before the backlog finishes draining, so a run overtakes."*
   Also true, also a real pre-existing window, also closed. Not the cause either.
3. *"The fixture mixes connections, so the test is unprovable."* Wrong — and the grep that suggested it
   was sloppy: `SetAddAsync` matched `VectorSetAddAsync`.

The actual cause only appeared when I stopped reasoning and dumped the bytes: `DEL, SET, SMEMBERS, DEL,
SADD`. The slot holder drained the *live* backlog when it became ready, so commands issued **after** it
took the slot were written in front of its run. Arrival order at the connection is not the rule; the rule
is that whoever holds the slot goes ahead of everything issued after they took it. The holder now
captures the backlog under the lock at the moment it acquires, and drains only that.

Three hypotheses, two of which were real bugs that were not *this* bug — which is exactly why they were
convincing. The instrument that settled it took two minutes and should have come first.

**What the remaining failures are, and none of them is a core defect.**

- **`AsyncState` is not propagated onto returned tasks** (2 tests). `ValueTask.AsTask()` over an
  `IValueTaskSource` cannot carry it; the old core allocates a `TaskCompletionSource` per command and
  gets it for free. Carrying it means allocating one *when `asyncState` is non-null* — which is the
  right trade, since the default `GetDatabase()` passes null and would pay nothing. ~270 call sites,
  mechanical; queued rather than done here.
- **`Assert.IsType<RedisTransaction>(tran)`** (2 tests) — a test pinning the shipped concrete type. The
  behaviour it then checks (nested transactions throw) is preserved.
- **`Assert.Equal(TaskStatus.Canceled, pending.Status)` read synchronously after the await** (2 tests).
  The operation *is* cancelled — the same assertion passes with a delay, and against a fake transport
  it passes outright. What changed is that this core sets `RunContinuationsAsynchronously` (§7b, and
  `docs/ThreadTheft.md`), so the wrapper task has not transitioned yet at the instant the test looks.
  Not a bug, but a **visible timing change**: code that reads `.Status` immediately rather than awaiting
  was correct-by-luck on the old core. Worth a release note, because it will not announce itself.

### 7u. `AsyncState`, and fire-and-forget at the right layer

**`AsyncState` cannot come from an `IValueTaskSource`, and that is the BCL rather than this design.**
`Task.AsyncState` is set when the task is constructed and is immutable afterwards; `ValueTask<T>.AsTask()`
exposes no state parameter, so an IVTS-backed task always reports null. Verified rather than assumed, and
the experiment turned up a second fact that matters more: for an already-completed value `AsTask()` can
hand back the **shared `Task.FromResult` cache singleton**. So the tempting optimisation - stamp
`m_stateObject` with `UnsafeAccessor`, which this library already does in `Delegates.cs` - is safe only
for a *pending* source, whose task is freshly allocated and unshared. For a completed one it would
corrupt an instance the whole process holds, and there is no reliable way to ask whether the task you
were handed is yours. Left as a documented option behind one helper rather than spread across the call
sites.

The helper sits beside `Wait`, for the same reason `Wait` does. A database with no async state - what
`GetDatabase()` gives you - takes the plain `AsTask()` path unchanged; only a caller who asked for state
allocates the `TaskCompletionSource`. The new `RespDatabaseContext` surface returns `ValueTask` and
allocates nothing at all; this exists for `IDatabaseAsync`, which returns `Task` and was already
allocating one object per command, so the delta is one more object on an opt-in path.

**No `RunContinuationsAsynchronously` on that TCS, deliberately.** The core's operations already complete
with asynchronous continuations (§7b, `docs/ThreadTheft.md`), so by the time the bridge resumes the hop
off the IO thread has happened. A second hop would add latency to buy a guarantee already held.

**Fire-and-forget: the first attempt was at the wrong layer, and two existing tests said so.** Putting
"return default immediately" in the shared send funnel looked obviously right and broke two things that
are deliberate: `FireAndForgetIsNeitherCachedNorServed` asserts the cache is still *consulted* (so the
refusal is counted rather than invisible), and `FireAndForgetIsNotRetried` asserts a write failure still
surfaces. Short-circuiting above the cache destroyed both.

The convention already existed one layer down - `RespBatchExecutor` and `RespMessageExecutor` each answer
fire-and-forget with a null payload that `Parse` turns into `default(T)` - and the only executor missing
it was the transaction one. Fixed there, with `DiscardReply` draining the operation so it still recycles.
**The lesson is the same one as §7t:** the funnel looked like the general place to put a general rule, and
the two tests encoded why it is not. Reading them first would have been quicker than reverting.

**Still open: fire-and-forget does not short-circuit at the endpoint executor**, so an ordinary
`db.StringIncrement(key, 1, FireAndForget)` on the new core still waits for a reply the caller said they
did not want. That is a latency gap rather than a correctness one, and it interacts with how write
failures surface, so it wants deciding rather than pattern-matching.

### 7v. Closing out the batch/transaction suites: two real bugs behind the "known" failures

The three remaining failures after §7u were labelled "characterised, none a core defect". Two of them
were core defects, and the label was wrong because I had stopped at the first plausible explanation.

**The nested-transaction test was the one genuine test-only fix.** `Assert.IsType<RedisTransaction>(tran)`
pinned the shipped concrete type, so it was the one assertion in the suite a second `ITransaction`
implementation could not satisfy. Asserting through `IDatabaseAsync` tests the contract instead. That
exposed the second half: `TransitionalBatch` declared its own public `CreateTransaction`, which the
interface never reached, because `IDatabaseAsync.CreateTransaction` is an **explicit** implementation on
the base. `RedisDatabase` had already solved this with a runtime `this is IBatch` check in one method
rather than two that can disagree — copied, because it is the better shape and not merely the shipped one.

**A conditional transaction was dialling a new connection for every command issued while it ran.** The
`.Status`-timing story from §7t turned out to be hiding this. Making cancellation inline (below) left one
failure that was deterministic rather than flaky, and it reported *"the connection was lost"* while both
the old and the new connection were open — which is not what "lost" looks like.

The cause: the backlog branch in `Dispatch`/`Enqueue` ends with `EnsureConnecting()`, because it was
written for "there is no connection". §7t reused that branch for "the write slot is held", where the
connection is perfectly good and merely busy. So every command arriving during a conditional transaction
started a fresh socket, which replaced `_connection`, and the transaction then found its **named**
connection changed and refused to send — the §7r safety check doing exactly its job, on a problem
manufactured one layer below it. Reusing a branch reuses its side effects, and this is the second time in
two sections that a branch has been shared on the strength of its *condition* while its *body* carried an
assumption that no longer held.

**A synchronous command that landed in the backlog could not be waited on at all.** This is the one worth
keeping: it was dismissed as scan flakiness twice, including once in a queue entry that said "do not
assume it is harmless". `RespMessageBase.Wait` guards on `Flag_Sent`, and the guard is right — awaiting a
command whose batch has not been executed would hang forever, because nothing is ever going to send it.
But a *backlogged* command is the opposite case: nothing has written it yet, and somebody has promised to.
The operation had no way to say so, so the sync path threw `"This command has not been sent"`.

Reachable long before any of this work: any synchronous command issued while the connection is still
coming up hits it. It presented as intermittent scan failures because a cursor loop is a long run of
synchronous sends and only needs to be unlucky once, and it got more reachable with the write slot.
`Flag_Queued`/`MarkQueued` names the state; `Wait` and `GetStatus` accept either.

**Inline cancellation, narrowly.** The queued commands of a transaction whose condition failed are
cancelled by a caller that never sent them - no read loop is involved - so `TrySetCanceledInline`
completes them on the calling thread, and the shipped guarantee that they have transitioned by the time
`ExecuteAsync`'s task completes holds again. The flag is restored before returning, because
`ManualResetValueTaskSourceCore.Reset` does not clear it and a pooled operation's next life would
otherwise complete inline *from the read loop* - the one thing this is not allowed to do.

`BatchTests` and `TransactionTests` now pass in full, on both protocols, against the new core: 926/926
across the wrapped and unwrapped forms, and three consecutive full-suite runs with the scan failures gone.

### 7w. `StringGetWithExpiry`, and a coverage claim that was not true

`StringGetWithExpiry` was picked as the next exemplar of "has not moved yet" on the reasoning that it
needs a composite result assembled from two replies - and then moved in the same sitting, because it
needs no mechanism at all. The old core expresses it as an `IMultiMessage` expanding to `[TTL, GET]`,
with the TTL's result box created *inside* the expansion and the `GET`'s processor reassembling the
pair, plus a `CanWriteWithoutExpansion = false` backstop because writing it unexpanded leaves the expiry
half unreadable. Here it is two sends and an addition. Same collapse as §7r, from the same cause.

Two details are load-bearing. Both commands are issued *before* either is awaited, so they travel
together and in order on one connection rather than costing two round trips - they address one key, so
they resolve to one server, and they were never atomic against other clients in the old core either.
And **both are awaited even when the first fails**: the operations are pooled and reset when their
result is consumed, so an abandoned one never resets, never returns to the pool, and never releases its
pooled request buffer. That is the same leak `DiscardReply` exists to prevent, reached by a different
road. SER352 is now **2**, both of them `Publish`.

**Then the coverage claim turned out to be partly false, and that is the real finding here.** The
technique this work leans on - subclass an existing suite, override `GetDatabase`, and every assertion
in it becomes an assertion about the new core - only works if the suite *calls* `GetDatabase(conn)`.
Many call `conn.GetDatabase()` directly, which silently bypasses the override and tests the shipped
database instead. A census of the wrapped suites found **246 such call sites across 22 files**, several
of them with *no* routed call at all: `StreamTests` (97), `GeoTests` (28), `BasicOpTests` (24),
`HashImportTests` (14), `ScanTests` (12), `KeyTests` (9). Those wrappers ran, passed, and proved
nothing.

Converting them all produced **164 failures** - not flakiness, and not the new core's connection stack
either, because the `Transitional*` wrappers fail identically and those run over the old pipeline. They
are command-level gaps in the new surface. Three are already identified: the command map is not
consulted where the shipped surface falls back (`MSETEX` → `MSET`), `Expiration` rejects inputs the old
API accepted ("a deadline is required; KEEPTTL and PERSIST are not expirations"), and a set of
value/prefix mismatches not yet diagnosed.

Eleven suites are kept converted, because they pass: `StreamTests`, `GeoTests`, `BasicOpTests`,
`SetTests`, `HyperLogLogTests`, `IncrexIntegrationTests`, `KeyIdleTests`, `KeyIdleAsyncTests`,
`LexTests`, `MultiAddTests`, `OverloadCompatTests`. That is real coverage that did not exist an hour
ago, at no cost in pass count. The other eleven are reverted rather than left red, so the full suite
stays usable as the regression instrument it has been all session - and their gap list is the queue's
top item rather than a discovery waiting to happen during deletion.

**The lesson generalises past this branch:** a test that inherits a suite to re-run it against a
different implementation proves nothing unless every route into the subject goes through the seam. Worth
an analyzer - "a wrapped suite calling `conn.GetDatabase()` directly" is mechanically detectable, and
this is the second time a silent no-op has been found by accident rather than by tooling (the first was
`[AutoDatabase]` matching interfaces by declared name, §7s).

### 7x. The new core was writing to the wrong database, silently

Converting `CopyTests` surfaced a cross-database `COPY` returning false. Chasing it found something much
larger than the test: **the new core ignores the database index entirely.**

`SELECT` is issued once, by the handshake, for the connection's configured database. `RespNewCore`'s
endpoint cache is keyed by endpoint alone, so `GetDatabase(5)` builds a context that reports database 5,
resolves to that same single connection, and reads and writes **database 0**. Verified directly rather
than inferred: writing `"five"` through a database-5 context and reading it back through the *shipped*
database-0 database returns `"five"`, and shipped's database 5 is empty.

No error, no warning, no failing test. It is the worst shape a defect can have - silent, and wrong in the
direction of corrupting somebody else's data.

**Why no test caught it** is the part worth keeping. Several suites do use a dedicated database, and their
new-core wrappers were passing: every operation went to database 0 *consistently*, so a test that writes
and reads through the same wrapper sees exactly what it expects. Only a test that crosses databases -
`CopyTests.CrossDB`, comparing against the shipped database - can see the difference. Self-consistent
wrongness is invisible to self-consistent tests.

**Refused first, then fixed.** The interim guard - `GetDatabase` throwing, the fixture skipping - earned
its place inside one run by turning eight *passing* locking tests into skips, their multi-database
coverage having been fake all along.

**The fix is a connection per (endpoint, database).** The endpoint cache is keyed by database first,
because that is the axis that decides which connection; `SELECT` then happens once per connection, at its
own handshake, and can never be stale. Verified against the shipped surface rather than itself: writing
`"zero"` to database 0 and `"other"` to another, both surfaces now agree about which is which.

**That is deliberately not what the shipped core does**, and the difference is worth stating. There, one
connection serves every database and a `SELECT` is injected immediately before any command for a
different one - cheaper in sockets, and the reason the shipped path needs a preamble mechanism at all.
Multiplexing databases over one connection means every such command must be written as a contiguous
pair, which is the capability §7s recorded as unreachable. A connection each is correct today and costs a
socket per database *actually used* - and databases are rare, discouraged in cluster, and usually one.

So the preamble stays queued, but as the optimisation it originally was rather than the correctness
blocker this briefly made it: `SELECT` no longer needs it, while `SCRIPT LOAD`-before-`EVALSHA` and the
hash-import prepare still do. The ordered write slot from §7t remains the primitive the pair-write will
want.

**The test that now pins it does something no wrapped suite could**: it compares *across* databases and
*against the shipped surface*. A suite that writes and reads through one wrapper cannot see this class of
bug however many times it is run.

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

### 8a. Questions raised while driving the engine failures down (2026-09-27)

Engine-flag failures went 133 -> 87 over one working session. These are what is left that is a
*decision* rather than a defect, recorded so they are not rediscovered.

- **What should `RespResult.RefCount` report?** `RespResultLeaseSharingTests` (7 failures) asserts an
  absolute count - 1 for a result alone, 2 once a lease is taken - and its class summary says that is
  the intent: "the underlying buffer should survive until the result and every lease have been
  disposed". Under the new core it measures **14**, because the reply shares one read buffer with
  every other reply in it. So either the property is per-result and the new core is exposing the
  buffer's count, or the semantics have legitimately changed and the assertions should be deltas. The
  test cannot be fixed without deciding which; nothing else depends on the answer.

- **Should `tran.Scripts.EvaluateAsync` be refused inside a transaction?**
  `MultiMessageInTransactionTests.TheFrameSurfacesComposedPairIsRefused` asserts a
  `NotSupportedException` mentioning "positional EXEC result array". Traced: the executor reports
  `Accumulates = true`, so `Scripts` takes the documented inline-body branch - one frame, no composed
  pair - queues normally, and the test hangs because it awaits without calling `Execute`. Inlining is
  what the shipped core does and what §7j defends. So either the test encodes a newer intent for the
  frame surface, or it is asserting against a behaviour that was deliberately chosen. Deliberately not
  changed.

- **Should `IsConnected(key)` mean "a socket exists now" or "a command would succeed"?** It is public
  API with a documented meaning and no internal caller - `KeyPrefixed`, `RetryDatabase` and
  `MultiGroupDatabase` only forward it - so it was left exactly as it was, and `RespConnectionState`
  was added alongside (internal) to answer what a bool cannot. Under lazy dialling a KEYED
  `IsConnected` still reports `false` for an endpoint that is merely undialled, which is the "not yet
  versus broken" ambiguity in its remaining form. Promoting the enum to public is trivial if wanted.

- **`MOVED` to the same endpoint needs a reconnect, and the naive fix deadlocks.** Diagnosed and
  reverted rather than left half-done; see §7l. The rule is right and the shipped core spells it
  `MarkNeedsReconnect`; retiring `_connection` directly fixes the four batch cases and hangs the four
  retry ones, with or without disposing, because it does not coordinate with `_writeSlotHeld` -
  exactly what `OnSendRefused`'s own comment warns about. It wants a flag acted on at a safe point,
  not a field nulled from a read loop.

- **The new core emits `SCRIPT` before every `EVAL`.** `ProfilingTests.Simple` expects
  `SET,EVAL,EVAL,GET,ECHO` and sees `SET,SCRIPT,EVAL,SCRIPT,EVAL,GET,ECHO`, so the script hash is not
  being retained between calls. Same family as the three pre-existing `ScriptLoadPairingTests`
  failures. Not yet investigated.

- **Two cores keep two sets of books while both are live.** `IReconnectRetryPolicy` sees the new core's
  per-executor counts interleaved with `PhysicalBridge`'s - traced as one extra consult - which is why
  two `ReconnectRetryPolicyUnitTests` cases still fail. Nothing to fix here; it resolves when the old
  core goes, and is noted so the failures are not chased again.

- **Several remaining failures are the two cores disagreeing, not the new core being wrong.**
  `BacklogTests` (4) calls `server.SimulateConnectionFailure`, which reaches
  `ServerEndPoint.interactive` - the OLD core's bridge. The new core's own connection is untouched, so
  the command that the test expects to fail succeeds. Same shape as the `IReconnectRetryPolicy`
  residual. These want either a new-core-aware simulation hook or to be left until the old core goes;
  chasing them as new-core bugs wastes the effort.

- **`AbortOnConnectFailTests` (4) is message parity, not behaviour.** The new core throws
  `RedisConnectionException` correctly; the text is its own literal ("The connection is not
  available.") where the tests want the shipped `ExceptionFactory.NoConnectionAvailable` wording ("No
  connection is active/available to service this operation: ..."). That factory needs a multiplexer, a
  `Message` and a `ServerEndPoint`, none of which `RespPayloadOperation.EnsureFaulted` has, so closing
  it is a plumbing decision rather than a one-line fix - the same shared-wording argument as
  `MultiSlotMessage`.

- **DECISION TAKEN UNDER UNCERTAINTY, worth reviewing: FailFast now dials once.** `BacklogPolicy.FailFast`
  was refusing a command when the executor had no connection - including when it had never dialled -
  so a FailFast multiplexer could never issue its FIRST command against a healthy server
  (`ClientKillTests`, 4 failures). The shipped core never had to decide this, because it dials every
  endpoint during connect; lazy dialling creates the question. The exemption is one ATTEMPT, not one
  success - counting successes would leave FailFast permanently disengaged against a server that is
  down - after which a disconnected send is refused as the policy says.
  `RespEndpointExecutorTests.WithoutQueueingADisconnectedSendFailsImmediately` asserted "and it did not
  even try" and has been changed to assert both halves. If the original intent was deliberate about the
  never-dialled case too, the alternative fix is to have the multiplexer warm the new core's executors
  during connect, and this should be reverted.

## 9. Ending the two-core era: the new core needs its own topology

> For the chain as it stands today - what happens between `db.StringGet(key)` and the socket, and which
> of `ServerEndPoint`/`PhysicalBridge`/`PhysicalConnection`'s jobs went where - see `command-flow.md`.
> This section is the plan for what is left; that one is the map of what exists.

### 9a. Why this is the priority, stated as measurement rather than opinion

The command surface is done; the topology half has not started. That asymmetry is what makes the
engine flag feel like a constant fight - and the fight is structural, not incidental.

The new core owns **sockets**. It does not own **which sockets, or what is on the end of them**:

```
ForSlot(slot)  -> _multiplexer.ServerSelectionStrategy.Select(...)  -> ServerEndPoint  (old core)
Any(...)       -> same
OnSlotMoved    -> _multiplexer.ReconfigureIfNeeded(...)             (old core re-discovers)
features       -> _multiplexer.SelectServer(...)
```

`RespTopology` is 147 lines holding a tri-state - cluster, not cluster, unknown. **No slot map, no
endpoint registry, no roles.** The map it routes on is `ServerSelectionStrategy.map`, a
`ServerEndPoint[16384]` filled by `ConnectionMultiplexer.UpdateClusterRange` from a `CLUSTER NODES`
that the OLD core issued during its own auto-configure. So the old core must connect for the new core
to route at all, and both cores are live for every command.

Every dual-core symptom met so far is one shape:

| symptom | cause |
|---|---|
| `IsConnected(default)` non-deterministic | selector reports the OLD core's connection state |
| `IReconnectRetryPolicy` sees `0,0,1` | both cores count against one policy instance |
| `BacklogTests` (4) | `SimulateConnectionFailure` reaches `ServerEndPoint.interactive` only |
| cluster detection overwrote a correct topology | two probes, two answers, last writer won |

What remains, by size:

| | lines | status |
|---|---:|---|
| `RedisDatabase`, `Message`, `ResultProcessor`, `RedisTransaction`, `RedisBatch` | ~13,200 | replaced; 2 engine-gated fallbacks left |
| `ServerEndPoint` (+`.Maintenance`), `ServerSelectionStrategy` | ~2,980 | **no new-core equivalent** |
| `PhysicalBridge`, `PhysicalConnection` (all parts) | ~3,860 | blocked behind the above |

### 9b. The plan, in the order it has to happen

**A. The new core owns the slot map.** DONE. `RespTopology` gains a `SlotOwners?[16384]`, filled by a
`CLUSTER SLOTS` the NEW core issues after its own handshake reports cluster - on that connection,
before the backlog drains, for the same ordering reason `OnServerType` is set there. `ForSlot`
consults it and falls back to the selector only while it is empty, so the change was safe before it
was complete. `CLUSTER SLOTS` rather than `CLUSTER NODES` deliberately: its reply is nested arrays
needing no text parsing, and `ClusterConfiguration` - the NODES parser - takes a
`ServerSelectionStrategy`, which is the coupling being removed. A `MOVED` teaches the map directly,
since the server naming the new owner is more current than any rediscovery.

Proved rather than assumed: `TheNewCoreFillsAndRoutesFromItsOwnSlotMap` checks the map against the
CLUSTER's own configuration, not against the shipped map, so it cannot pass by one copy equalling
another.

**B. Roles and selectability.** DONE. Primary/replica per endpoint, so `PreferReplica`/`DemandReplica`
resolve without `ServerEndPoint`.

Two records, because there are two questions and only one of them has a slot in it. Per range, the
same `CLUSTER SLOTS` entry that names the primary names its replicas, and that pairing is the part no
other source has - "this server is a replica" says nothing about WHICH slots it replicates, so a keyed
`PreferReplica` can only be answered from the range. Per endpoint, a flat role map answers the keyless
case and the standalone one, where there is no slot to look anything up by.

`INFO REPLICATION` supplies the standalone half, and it needs the endpoint passed in: the reply names
the role of whoever answered it and nothing in the reply says who that was. Asked only when more than
one endpoint is configured - with one there is nothing to prefer a replica OVER, so the round trip
would buy a fact routing cannot act on, on every connection, forever. A cluster pays nothing, having
read the same facts out of a reply it wanted anyway.

Choosing among replicas is round-robin from a rotating offset, preferring one already connected:
taking the first every time puts every replica read of a range on one node, which is most of what
asking for a replica is for, while always taking the next dials a fresh socket for a read an existing
connection could serve. An unsatisfiable *demand* returns null rather than the primary, so it falls
through to the selector and then fails - a caller who demanded a replica and got a primary has been
told something untrue about where their read ran.

`MOVED` records the new owner with NO replicas rather than carrying the old range's across: a `MOVED`
names one endpoint, and the previous replicas may no longer replicate that slot. They return with the
next `CLUSTER SLOTS`; until then a preference resolves to the primary, which is what "prefer" means.

**C. Features.** DONE. `MultiplexerFeatureProbe` read the version off the SHIPPED core's
`ServerEndPoint` - the third coupling, after routing and roles. It is now answered from what this
core's own handshake observed, recorded per endpoint before the connection is handed back.

Both protocols, which needed one addition: `HELLO` reports the version and RESP2 connections do not
send one, so an `INFO SERVER` fills that half. It matters more than it sounds - several commands are
CHOSEN from what the server supports (an all-GET `BITFIELD` goes out as `BITFIELD_RO` where that
exists, which is what lets a replica serve it), so a core that cannot answer "what can this server do?"
picks the writable spelling and a replica read is then refused.

Routing and the probe share one path (`RouteEndpoint`, over the same `EndpointForSlot`/`EndpointForAny`
the executors use), so "which server answers this?" cannot drift from where the command actually goes.
Splitting the endpoint choice out of executor creation is what makes that possible: the probe wants the
answer, not the machinery, and creating - let alone dialling - an executor to read a version would turn
a question into an action.

The selector remains the fallback, for the same reason it does in routing: while nothing has been
dialled there is nothing to have observed, and the other core's knowledge beats the configured default.
It goes with the rest of the fallbacks in D.

**D. Stop dialling twice.** With A-C the multiplexer need not create `ServerEndPoint`s or bridges when
the engine flag is on, and `PhysicalBridge`/`PhysicalConnection` become deletable.

D is two halves, and only the first is close.

**D1: the new core stops READING the shipped topology.** What is left in `RespNewCore` after C is a
short list, and it is worth writing down because it is shorter than it looks. `RawConfig`,
`GetEndPoints`, `ClientName`, `ClientCache`, `CurrentProfilingSession` and `SetAuthSuspect` are
configuration and services, not topology, and they stay. The topology reads are:
`ServerSelectionStrategy` (x4, all routing fallbacks), `SelectServer` (x1, the probe's fallback),
`GetSubscribedServer` (x1), `GetServerEndPoint` (x2, profiling and the script cache), and
`ReconfigureIfNeeded` (x2, telling the OTHER core what a redirect revealed - which is a notification,
not a dependency).

Everything needed to replace the routing fallbacks already exists: `CLUSTER SLOTS` names every node in
the deployment, the role map covers standalone, and the reachability preference reads `_endpoints`.
`DemandReplica` with no replica returning null even matches shipped semantics exactly - `AnyServer`
leaves `fallback` unset on that branch and returns null too.

**D1 was attempted and does not hold up yet; the reason is worth recording, because it is not the
reason that was expected.** The plan assumed the obstacle was the startup window - that between
`SeedTopology` reporting cluster and this core's own `CLUSTER SLOTS` landing there is nothing of ours
to route a keyed command on - and that the fix was one of: accept a `MOVED`, seed the map once at
construction, or re-resolve the route as the backlog drains. The third is genuinely cheap: the command
is backlogged behind the very connection whose handshake fills the map, so the answer arrives in time
and only the ORDER of the two steps makes it unusable, and the handoff a redirect already uses
(`TryResend`) is all the machinery it needs.

**But `IdentifyEndpoint` asks the question without sending anything**, and `ClusterTests.TestIdentity`
asserts it agrees with the cluster's own view on a multiplexer where nothing has been dialled. There is
no backlog to correct, so no drain-time trick reaches it: the map has to EXIST before the first
question, which means eager discovery, which is D2.

Roles fail on the same rock and more visibly. This core connects on demand, so a replica nothing has
had reason to dial has no role - and `DemandReplica` answered from that would refuse a perfectly good
replica for no reason other than laziness. Measured: removing the selector from keyless routing took
the engine suite from 72 failures to 98, including every `GetFromRightNodeBasedOnFlags` case and the
`BITFIELD`-reaches-a-replica tests.

**So the fallbacks are not tidiness debt - they are covering a real gap, and the gap is eager
discovery.** D2 is not "the next step after D1"; it is the prerequisite for it.

**Half of that gap has since closed, from the same observation that made A work.** Discovery does not
need a connection per endpoint - it needs ONE connection that describes the deployment. A cluster
already had this: `CLUSTER SLOTS` names every node and every role from whichever node answers. The
standalone equivalent is `ROLE`, whose value here is not the answering server's own role but the other
side of it: a primary lists its replicas, a replica names its primary. So one connection now yields
both roles of a pair, and a replica nothing has had reason to dial has a role anyway.

`ROLE` rather than `INFO REPLICATION`, which the first version used: the same facts as a structured
reply rather than a text section to scan, and it is what the shipped core parses too.

What remains is narrower than "eager discovery": the FIRST routing decision, before any connection
exists at all. Commands could be corrected at drain; `IdentifyEndpoint` cannot, because it asks without
sending. Closing that means the new core connecting before the first question rather than on it, which
is a deliberate change to the lazy design and is the real content of D2.

**Detection was reading the wrong evidence.** `CLUSTER INFO` is a diagnostic command, and an error from
it was being read as "standalone" - which is wrong for anything implementing the routing surface
without the diagnostic one: a proxy, an alternative implementation, and the in-process test server,
which serves `CLUSTER NODES` and `CLUSTER SLOTS` and has no `CLUSTER INFO` at all. Slot routing was
switched off entirely for such a deployment. `CLUSTER SLOTS` is now asked anyway when `CLUSTER INFO`
declines, and a reply carrying ranges settles it - better evidence in any case, being the very thing
routing uses. Invisible in the suite today because the selector fallback masks the consequence (the
shipped core detects clusters from `INFO`'s `cluster_enabled` and gets the right answer), so it has a
test of its own: `AServerThatServesSlotsWithoutClusterInfoIsStillACluster`.

**D2: the multiplexer stops DIALLING.** Much larger, and not gated on D1: endpoint discovery, the
connect handshake `ConnectAsync` waits on, `IsConnected`, `IServer`, sentinel, tiebreakers and
maintenance events all assume bridges exist. This is where `BacklogTests`, `ReconnectRetryPolicyUnit`
`Tests` and `AbortOnConnectFailTests` stop being dual-core artefacts, because there stops being a
second core.

A was the keystone: nothing else could move first, and it is what stops MOVED handling, routing and
connection state being three answers from two places.

Uncovered while measuring A and B and since fixed: the new core did not reclassify a redirect whose
target cannot be routed to (`?:port`, `:port`) as `UnknownRedirectTarget` - `ResultProcessor` does
that, and nothing on the new path did. The connection already recognised the case (it is why the
topology refresh was requested) but let the raw `MOVED` text stand as the error, so a caller was told
the slot moved somewhere when the server had actually said it does not know where the slot went. The
kind is read by retry policy, and the distinction is exactly the one that matters there: a command
redirected nowhere provably never ran.

Two details worth keeping: the check now precedes `NoRedirect` rather than following it, because it is
a statement about the reply rather than about what to do with it - which is also the shipped ordering;
and the message is cleared in `OnReset`, without which the pooled operation handed a stale redirect
diagnosis to whatever command reused it, which is how the first version turned a `CROSSSLOT` error
into a redirect message.

### 9b-ii. Preambles, and what profiling still disagrees about

A preamble - the `SCRIPT LOAD` in front of an `EVALSHA`, the `HIMPORT PREPARE` in front of a
`HIMPORT SET` - can be written as a pair when a connection is warm, and cannot when it is not. The
cold path fell back to sending it as an ordinary command, which was correct and wrong in two ways at
once.

It appeared in the caller's **profiling session**, as a `SCRIPT` they never issued and could not
reproduce, and it never told the **gate** it had succeeded - so the belief stayed unset in exactly the
case the fallback always covers, the first evaluation. Every later call then re-sent the preamble,
forever: correct, invisible to every test that only checks results, and precisely the round trip the
pairing exists to remove. `SendPreambleAsync` is now one path for both sequential cases, overridden by
whoever owns a connection and forwarded by the router and the per-database view.

Two profiling disagreements found alongside it, both fixed: a per-database view's commands were
reported against the CONNECTION's database rather than their own, so profiling a dedicated-database
workload named database 0 throughout; and commands that name no database - `PING`, `ECHO`, the
`CLIENT` family - were reported as running in database 0 rather than as db-free, which the shipped
core decides from `Message.RequiresDatabase` and this now shares.

The missing commands in `LowAllocationEnumerable` turned out to be an ordering bug one layer down, in
RESPite, and a general one rather than anything about profiling. `RespMessageBase` published the
outcome and *then* called `OnFinished`, which is what pushes a profiled command into its session.
Publishing runs an awaiting continuation inline, so a caller could be back from its await, have
finished waiting on every task, and be asking for the results before the last few records had been
pushed. Not dropped - late, by microseconds, which is why the count varied run to run (988-999 of
1,000) and why it only showed under a blocking `WaitAll`: an `await` adds a scheduling hop that hides
it. `OnFinished` now runs before publishing, which is the same ordering the shipped core has always
had - `Message.Complete` records the performance data before it activates continuations. Worth noting
this is the second instance of exactly the hazard `Mark`'s own remarks describe; the remedy was
applied there and not to its neighbour.

**What remains is a semantic question rather than a bug.** `ProfilingTests` asserts
`EnqueuedToSending > 0`, and on a warm connection this core has no queue between those two points -
the operation is stamped and written in the same breath, so the honest measurement is zero, and it
fails only under enough load to make it so. The shipped core always has a bridge hop there. Either the
stage is meaningless for this core and the assertion should be relaxed for it, or the stamps should be
placed at points that keep the shipped meaning.

### 9b-iii. Reference counts, and a configured pool that was accepted and ignored

`RespResultLeaseSharingTests` asserted absolute `RefCount` values, and they no longer mean what they
did. A result that SHARES the receive buffer shares it with whatever else landed in the same read, so
the number describes how replies happened to pack rather than anything about this result - measured at
2 for a 4KB blob and 5 for a two-byte one. The sharing contract is a delta, not a total: taking a lease
adds exactly one reference (a copy would add none), disposing it removes exactly one, disposing it
again removes nothing. Rewritten that way the assertions are stronger, not weaker - they no longer
depend on buffer packing - and they pass, which is also the evidence that nothing is over-retaining.

The last one in that file was a real defect rather than an expectation: `RespConnection` rented every
inbound buffer with a null pool, so `ConfigurationOptions.ResponseBufferPool` was accepted and silently
ignored by this core. That is not a staging detail - payloads are retained from those buffers, so a
caller who supplies a pool is asking to own the memory the replies live in. The shipped core has always
honoured it (`PhysicalConnection.ReaderBufferPool`); this one now does too.

Not done, and worth considering separately: `RespPayload.GetReader()` attaches no services, so a lease
that has to copy rents from the default pool rather than from the pool the data came from - which is
what the code there says it intends. The payload's own `RefCountedBuffer` is both an
`IPayloadReservationProvider` and an `IBufferPoolProvider`, so handing it over would fix that for free -
but it would also let readers on that path start SHARING where they currently copy, which is a lifetime
change rather than a pool change, and wants its own look.

### 9b-iv. Availability: the breaker saw nothing this core did

A circuit breaker counts outcomes, and every outcome it was counting belonged to the shipped core. Under
the engine flag the commands run on the new core's socket, so a breaker configured by the caller
observed only handshake and heartbeat traffic - which meant it never tripped, a group never rerouted
away from a failing member, and a retry policy with failover configured rode out a server answering
`LOADING` to everything.

Observed from `OnFinished`, which is the one hook that sees every ending - a reply, a server error, a
cancellation, a timeout, a connection fault - and is the same point the shipped core observes at, in
`Message.Complete`. The operation carries who is counting, set where it is created, because by
completion time the routing decision is gone and the answer has to be the endpoint the command actually
went to. Runs are set in `SendRun` rather than at creation, since a batch's and a transaction's
operations are built by the composing executor: without that a deployment whose `EXEC` always failed
was never judged unhealthy, because the breaker only ever saw single sends.

A trip hands the teardown to the pool rather than doing it inline, exactly as
`PhysicalConnection.ObserveMessageResult` does - failing a backlog and building a detailed exception is
not work to put on the thread that just finished somebody's `GET` - and the connection is disposed from
there rather than from the completion path, which is the self-join the MOVED work already found the
hard way. The announcement is an ordinary `ConnectionFailed`, which is what a trip is from everybody
else's point of view and specifically what a connection group listens for; the shipped core reaches the
same event by a longer road through `PhysicalBridge.RecordConnectionFailed`.

Fresh counters after actuating, so the next connection starts clean rather than inheriting the
judgement that condemned the last one - which is what the shipped core gets structurally, by building
an accumulator per `PhysicalConnection`.

Two in that family remain and are not breaker-related: `WithRetry_Transaction_BatchExecute_`
`DoesNotWaitForReplies` and `WithRetry_ForwardsProbes`.

Also confirmed as dual-core artefacts rather than bugs, and added to the 9a list:
`ServerExecuteDatabaseTests` (2). They assert that a connection's selected database moves with the
commands sent on it, and ask `IServer.Execute("CLIENT", "INFO")` for the answer - but `IServer` builds
its context over `RespMessageExecutor`, the Message shim, so the write goes out on this core's socket
and the question is put to the shipped core's. One core, one socket, and they agree again.

### 9b-v. The failure surface, and what the remaining engine failures actually are

Triaged from the messages rather than the names, the engine-flag failures fall into four groups, and
naming them is what makes the queue finite.

**Failure-surface parity** was the largest and is the one being worked. A dial that fails throws
whatever the platform throws - a `SocketException`, a TLS `AuthenticationException`, an `IOException` -
and those were reaching callers unwrapped, so code catching `RedisConnectionException` (which is every
caller that has ever handled this) caught nothing. Wrapped now, with the platform error as the inner.

Separately, a command that never left the backlog is told "no connection was available to service
this", not "the dial failed": it did not fail to connect, it failed to find anywhere to go. That is the
question `ExceptionFactory.NoConnectionAvailable` answers - naming every endpoint tried, what each last
failed with, and how far connecting had got - and callers have been reading it for years.

**Dual-core artefacts** - everything that drives failure through `SimulateConnectionFailure`, which
killed only the shipped bridge, so this core's socket stayed up and the command simply succeeded. That
is why the group reads as "no exception was thrown" rather than as a wrong exception.

**These have since been converted rather than waited out**: the simulation now reaches this core's
connections too, and the test-only `AllowConnect` gate is honoured by this core's dial for the same
reason - a client that reconnects on a socket the test believes it has forbidden is not testing what
the test says. Doing that turned silent successes into sharp failures, and two of them were real bugs.

**A FailFast endpoint never reconnected.** `BacklogPolicy.FailFast` refuses a command rather than
queueing it, and refusing without dialling is correct for the command - but nothing else was ever going
to dial, so an endpoint that dropped once stayed down forever and the deployment never recovered from a
blip. The recovery is armed where the connection is LOST rather than per command, which keeps the
refusal free and still gets the endpoint back.

**The retry backoff had no timer, so "not yet" meant "not ever".** This core dials on demand; when the
policy declined an attempt, nothing came back to ask again, and anything already in the backlog waited
for the operation backstop to time it out two minutes later. Measured exactly that: 2m29s per test. The
shipped core does not have this problem because its bridge heartbeat retries whether or not anybody
asks, and `EnsureConnecting` now arms the same kind of timer - a poll that defers to the policy rather
than a second opinion about when to retry.

What remains in that family is narrower and named: `BacklogTests` (3) want this core's backlog to appear
in `GetBridgeStatus`/`PendingUnsentItems`, which it does not report at all; the rest are recovery-timing
differences between the two cores that D2 settles by leaving only one.

**Known-hard**: `MovedUnitTests` (4), the MOVED-to-same-endpoint reconnect, already diagnosed and
reverted once - the naive retire deadlocks on the write slot.

**Test isolation rather than core behaviour**: the scan/randomkey/script family, which fails with keys
belonging to *other test classes* (`FlushFetchRandomKey` finding `...NewCoreScanTests-ScanTests-...`).
Parallel classes sharing a database; visible here only because the timing differs.

### 9d. D2, fully defined: the multiplexer stops dialling

**The statement.** Under the engine flag, `ConnectionMultiplexer` creates no `PhysicalBridge` and opens
no `PhysicalConnection`. Every socket to every server belongs to `RespNewCore`. `ServerEndPoint` may
survive for a while as a metadata record - it is where several beliefs currently live - but it stops
owning connections. When the flag becomes the only behaviour, `PhysicalBridge`,
`PhysicalConnection` (6,567 lines across its partials) and most of `ServerEndPoint` (1,701) delete.

All three types are `internal` and appear nowhere in `PublicAPI.Shipped.txt`, so the deletion is not a
binary break. What IS public, and therefore constrains the design rather than being a casualty of it:
`IServer` and everything on it, `IsConnected`/`IsConnecting`, `GetStatus`/`GetCounters`/`GetStormLog`,
the `ConnectionFailed`/`ConnectionRestored`/`ErrorMessage`/`InternalError` events,
`ClusterConfiguration`, and the sentinel surface.

#### D2.0 The one decision that gates everything: eager or lazy

This core dials on demand. The shipped core dials every configured endpoint during `ConnectAsync` and
does not return until it has an answer. That difference is the whole of D2's difficulty, and it has to
be settled before the rest is designed rather than discovered underneath it.

- **Lazy** keeps this core's present behaviour and is cheaper for a client that touches one node of a
  large cluster. But `ConnectAsync` then has nothing to wait for, `IsConnected` has nothing to report
  before the first command, and `IdentifyEndpoint` - which asks without sending - cannot be answered
  from a map nothing has filled. Section 9b measured that last one: it is what stopped D1.
- **Eager** means this core connects to the configured endpoints during `ConnectAsync`, exactly as the
  shipped one does. Every question above is then answerable at the same moment it is answerable today,
  and the behaviour a caller sees does not change with the flag. The cost is the thing the lazy design
  was avoiding.
- **Eager-once** is the answer: connect to ONE endpoint during `ConnectAsync`, then dial the rest on
  demand. `ConnectAsync` has something to wait for, discovery is complete before the first command, and
  a 100-node cluster still opens one socket.

**And it buys no discovery we lack, which is worth stating plainly because the first version of this
section implied otherwise.** The configured endpoints are the dial targets - nothing has to be
discovered to start - and `CLUSTER SLOTS`/`ROLE` already ride on the first handshake whenever that
happens. Eager-once changes only WHEN the first connection is made, not what it asks. There is no extra
round trip anywhere in it; the entire content of the decision is whether `ConnectAsync` blocks on that
first connection so that `IsConnected` and `IdentifyEndpoint` have an answer before the first command.

**Resolved as a setting rather than a verdict, which is better than either answer.** It is a genuine
trade, not a default with a workaround: a caller who places keys deliberately - hash tags, a shard per
tenant, a keyspace partitioned on purpose - touches a small and known part of a large deployment, and
opening a socket to every node of it is cost with no return. A caller treating the keyspace as opaque
reaches everything eventually, so opening everything up front costs them nothing and answers every
question immediately. Neither is the right default for the other, which is the definition of a setting.

`ConnectMode` therefore has all three: `Lazy` opens nothing, `Discover` opens one, `Eager` opens every
configured endpoint. It is `internal` for now and defaults to `Lazy`, both for the same reason: while
the shipped core still dials every endpoint of its own, anything opened here is a socket IN ADDITION to
that rather than instead of it, so a caller asking for `Lazy` today would still get every socket opened
by the other half - a promise the client cannot keep. It goes public, and the default moves, with D2.8.

**And the connection-counting tests are not the authority on this.** They encode "the shipped core
dialled N sockets", which is a fact about the implementation rather than about desired behaviour; when
the count changes for the right reason the expectations change with it.

Nothing below is blocked on this except D2.1 and D2.3, but those two are the spine.

#### The obligations to re-home, with where each lives today

1. **Connect-time discovery and the `ConnectAsync` gate.** `ReconfigureAsync` resolves DNS, walks the
   configured endpoints, connects, auto-configures, proactively dials discovered cluster nodes, and
   decides whether the connect succeeded at all (`AbortOnConnectFail`). This core does discovery
   already; what it has no notion of is "the connect is finished".
2. **The heartbeat.** `OnHeartbeat` sweeps the client cache, checks whether a topology refresh is due,
   and pulses every `ServerEndPoint` - which is where keep-alives, connect retries, timeout scanning
   and backlog nudging happen. This core now has a reconnect timer (9b-v) and the operation backstop,
   which is two of those; the rest have no counterpart.
3. **Connection state on the public surface.** `IsConnected` is `_serverSnapshot.Any(s => s.IsConnected)`
   and `s.IsConnected` is `interactive?.IsConnected == true` - a bridge property. `RespConnectionState`
   already exists for this core (Unroutable/Deferred/Connecting/Connected); it has to become what those
   properties read.
4. **Counters and status.** `GetStatus`, `GetCounters`, `GetBridgeStatus`, `PendingUnsentItems`,
   `BacklogMessagesPending`. Three `BacklogTests` currently fail purely because this core's backlog is
   invisible to them.
5. **Events.** `ConnectionFailed`/`ConnectionRestored` are raised from the bridge's failure handling.
   This core raises `ConnectionFailed` for a circuit-breaker trip (9b-iv) and nothing else.
6. **Topology maintenance.** `AutoConfigureAsync`, `SetClusterConfiguration`, `UpdateNodeRelations`,
   tiebreakers, `SetUnselectable`, `OnSeenInTopology`/`OnMissingFromTopology`. Phases A and B moved the
   ROUTING half of this; the admin half - what `ClusterConfiguration` reports and what
   `ServerSelectionStrategy` publishes - is still the shipped core's.
7. **Subscriptions.** `RedisSubscriber` reaches for bridges directly; RESP2 needs a second connection
   per endpoint, which this core models but does not yet own end to end.
8. **`IServer`.** `RedisServer` builds its context over `RespMessageExecutor` - the Message shim - so
   every admin command still goes through the shipped path. This is why `ServerExecuteDatabaseTests`
   reads as a dual-core artefact.
9. **Sentinel.** `ConnectionMultiplexer.Sentinel` manages its own connections and failover.
10. **Maintenance events.** `PhysicalConnection.Maintenance` parses the Azure/AMR push notifications
    that drive the relaxation windows; four `MaintenanceRelaxation*` tests hang off it.
11. **Per-server beliefs.** `IsScriptLoaded`/`AddScript`/`FlushScriptCache` and the `RunId` check that
    invalidates them; this core borrows these from `ServerEndPoint` deliberately (`connection.Server`).
12. **Profiling context.** `StartProfile` needs a `ServerEndPoint` to name the server in a
    `ProfiledCommand`.

#### Sequencing

Each step is independently shippable and leaves the tree green.

- **D2.1 - Own the connect.** Implement D2.0's answer. `ConnectAsync` waits on this core;
  `AbortOnConnectFail` is decided from its result. *Done when* a multiplexer under the flag reports
  `IsConnected` correctly before any command is issued, and `ClusterTests.TestIdentity` passes without
  the selector fallback.

  **Tried, measured, and deferred - it cannot land before D2.8.** The mechanism works and is in tree:
  `RespNewCore.ConnectEagerlyAsync` brings up one endpoint and its handshake maps a six-node cluster,
  which `EagerConnectMapsTheWholeClusterFromOneConnection` asserts. Wiring it into `ConnectAsync` cost
  18 further failures, and they are all the same complaint: while the shipped core still dials every
  endpoint of its own, connecting eagerly ADDS a socket rather than replacing one. Tests that count
  connections notice - `DefaultOptionsTests.VanillaResp2ConnectsWithSeparatePubSubConnection`,
  `MaintenanceNotificationTests` (expected 1, got 2), `ServerRetirementUnitTests`,
  `UnroutableRedirectUnitTests`. So D2.1 and D2.8 are one step, not two, and the ordering in this
  section was wrong: eager connect does not PRECEDE the shipped core standing down, it replaces it.
- **D2.2 - Own the reporting AND selectability.** Connection state, counters, status, backlog
  visibility, and the `ConnectionFailed`/`ConnectionRestored` events sourced from this core - plus the
  selectability rules D2.3 turns out to depend on: retirement, unselectable flags, maintenance state,
  server-type filtering. *Done when* the three `BacklogTests` and the `AbortOnConnectFail`/`AsyncTests`
  recovery-timing group pass, and routing can answer "may this server be used?" without asking
  `ServerSelectionStrategy`.

  **Selectability is done.** `RespTopology` holds which endpoints are barred from new work, and routing
  consults it: a slot's owner that may not be used is not an answer, so the choice falls through rather
  than sending work to a retiring server. `PickReplica` skips barred candidates the same way.

  **Pushed rather than discovered, and that is the design rather than an expedient.** Retiring,
  redundant, wrong-server-type and mid-maintenance are all decisions the CLIENT makes about a server
  during reconfiguration - there is nothing to read from a connection, so a handshake could never learn
  them. While both cores exist the decisions are still `ServerEndPoint`'s and arrive as they are made;
  when only this core remains they are made here, and nothing about the reading side changes. That is
  why this is not another `ServerSelectionStrategy` dependency in disguise: it is state held here, not a
  question asked per command.

  **The backlog is now visible too.** `GetCounters` and `GetBridgeStatus` fold in this core's queue per
  endpoint and connection type - added, not substituted, since both queues are real while both cores
  exist. That closes the three `BacklogTests`. A backlog exists to be seen, and a client waiting on one
  it is told is empty is the diagnostic failing exactly when it is needed.

  **And the rest of the reporting, which was the half left over.** The same fold now carries operation
  counts, socket counts and sent-awaiting-response per endpoint, and `GetBridgeStatus` carries this core's
  byte counters (`last-in`, `cur-in`) - so a client under the engine flag no longer reports itself idle
  while it is busy, and a timeout no longer describes an idle shipped bridge instead of the socket the
  command was on. `LockingTests.TestOpCountByVersionLocal_UpLevel` is the assertion that measures it.

  The `CountsMultiplexerOps` opt-out survives for one narrow reason, now recorded on it: the suites that
  exercise this surface WITHOUT the flag build a core of their own beside the multiplexer's, and nothing
  has told the multiplexer that core exists, so nothing can count it.

  What remains of the reporting half is connection state on the public surface, plus two parity gaps
  found by pushing the failure messages to match:

  - **A backlogged command that cannot be sent should time out IN THE BACKLOG.** DONE, and it was a
    real defect rather than wording: a failed connect abandoned the whole queue, unconditionally, which
    is `BacklogPolicy.FailFast`'s behaviour applied to callers who did not ask for it. Under the default
    policy a command waits, up to its own timeout, precisely so that a transient failure does not become
    the caller's problem - and one failed dial is the most transient thing there is. Killing the queue
    on it left the backlog unable to do the one job it has.

    Three parts, because the first alone made things worse. `AbortPendingOnConnectionFailure` is now
    honoured, so the queue survives; a sweep on the retry tick fails whatever has waited longer than the
    timeout, since otherwise the only bound was the two-minute operation backstop - measured at 2m29s
    per affected test; and a failed connect now arms the retry timer, which only a DECLINED one did, so
    after a genuine failure nothing came back to retry or to expire anything.

    The diagnosis names why there is no connection, which needed a connection that was CLOSED rather
    than broken - retired, breaker-tripped, simulated - to write down a reason, since it has no fault of
    its own. "No connection became available" followed by nothing is a diagnosis that does not
    diagnose.
  - **The failure names the command but not the key.** DONE, and for free: the key is read off the
    RENDERED FRAME at failure time rather than carried alongside it. The bytes are already there - they
    have to be, to be written - so nothing is paid until something goes wrong and needs to name it, and
    what is paid happens on the caller's own unwind, where the operation is not going to be sent.
    Carrying a `RedisKey` on every operation against the chance that one fails was the alternative, and
    is strictly worse.

    The FIRST argument is taken, which is the key for the overwhelming majority of commands and is what
    `Message.CommandAndKey` reports. An approximation for the few where the first argument is a
    subcommand or a script body - and a slightly wrong label on an error is a far better trade than a
    field on every operation ever issued.

  Note the ordering this leaves. Selectability was a prerequisite for D2.3 but not a sufficient one -
  the timing blocker is still there, and it is answered by `ConnectMode` defaulting to something other
  than `Lazy`, which waits on D2.8. So D2.3 now needs only a default change, not new mechanism.
- **D2.3 - Retire the routing fallbacks (this is D1).** With D2.1 done, the selector calls in
  `EndpointForSlot`/`EndpointForAny` and the probe's `SelectServer` come out. *Done when*
  `RespNewCore` names `ServerSelectionStrategy` nowhere.

  **Blocked on exactly one thing now, and it is measured.** Removing both fallbacks costs 33 failures as
  it stands; two of the three causes have since been fixed, and the third is the mode default:

  1. **Roles were only known for endpoints this core had dialled** - it learns them from its own
     handshakes, and it dials lazily, so in the ordinary standalone pair the replica is never dialled and a
     `DemandReplica` read had no replica to choose. Roles are pushed from `ServerEndPoint` now, the way
     selectability already was, and seeded at construction from the snapshot the multiplexer holds. FIXED.
  2. **Everything undialled was being published as UNSELECTABLE.** `UnselectableFlags.DidNotRespond` is set
     until the SHIPPED bridge has connected at least once, and under the engine flag those bridges largely
     do not connect at all - so every endpoint this core had not used was barred from ever being chosen.
     The flag is excluded from what is pushed now; connectivity is the one thing this core tracks for
     itself, and it can tell "nobody has dialled this" from "this is down", which that flag cannot. FIXED,
     and it was a live defect rather than only a D2.3 blocker.
  3. **The slot map is empty until this core has dialled something**, because `CLUSTER SLOTS` is read by
     its own handshake. That is the whole of what is left. Measured: with both fallbacks removed and (1)
     and (2) in, the 22 remaining failures are 19 cluster-routing cases plus the 3 already known - and
     setting `ConnectMode.Discover`, which opens ONE socket at connect and fills the map from it, takes
     every one of those 19 to zero.

  **What `Discover` costs today is 28 failures elsewhere, and they are all the dual-core artefact**:
  connection counts (`ClusterTests.ConnectUsesSingleSocket`, the Azure/vanilla pub-sub cases,
  `ExceptionFactoryTests`' message text), and the unit suites whose fake servers are set up before a
  connection is expected (`UnroutableRedirectUnitTests`, `MaintenanceNotificationTests`,
  `ConfigTests.BeforeSocketConnect`). One more socket than the test expects, because the shipped core is
  still dialling everything as well. **D2.8 removes that**, and then the mode default can move and the
  fallbacks come out together.

  Also kept, because it is what will replace the keyless fallback: `EndpointForAny` now makes a role-aware
  choice of its own - a replica when one is preferred and known, otherwise a selectable endpoint that is
  not a known replica, preferring one already dialled. The selector is consulted first and still decides
  when it can; the local choice is the answer for when it goes. The role filter is not optional: without
  it a write goes to whichever endpoint happened to be dialled, which on a primary/replica pair is the
  replica as soon as one replica read has been served - caught by the pre-send check as
  `Command cannot be issued to a replica: BITFIELD`.

- **D2.5 - Own subscriptions**, including the RESP2 second connection.

  **What is still on the old core, counted rather than guessed at.** Instrumenting
  `ConnectionMultiplexer.CheckMessage` - which every `Message` passes through - over one engine-flag suite
  run gives 22,542 messages, and the distribution decides the order to do this in:

  | count | caller |
  | ----: | ------ |
  | 15,820 | `RedisSubscriber.Publish`/`PublishAsync` |
  | 1,967 | `RedisDatabase.ScriptEvaluate*Async` - **not work to do**; see below |
  | 1,954 | `RedisDatabase.ExecuteRespAsync`, `RedisDatabase.StringSetAsync` - likewise |
  | 1,527 | `RedisServer.ExecuteSync`/`ExecuteAsync` - the `IServer` members still on `Message` |
  | 408 | `RedisSubscriber.Ping`/`PingAsync` |
  | ~600 | the subscription machinery: `EnsureSubscribedToServer*`, `UnsubscribeFromServerAsync`, `ResubscribeToServer`, `ClusterPubSub` |
  | 90 | `RedisSubscriber.IdentifyEndpointAsync` |

  **`Publish` is DONE**, and it was the right first slice for a reason the table makes obvious: sixty-nine
  per cent of the traffic, and the only pub/sub member that is simply a command. It routes itself -
  `PubSub.PublishAsync` asks the executor to resolve the channel, which prefers the server this client
  already holds a subscription on, which is the same rule the shipped path expresses by passing
  `GetSubscribedServer`, and the stronger rule for `SPUBLISH`.

  **The 3,921 `RedisDatabase` rows are not work at all**, which is worth knowing before anybody plans
  around them. Tracing the callers: 1,998 are `OldScriptEvaluateAsync_DoesNotCorruptUnderLoad` and 1,998
  are `ExecuteRespAndScriptEvaluateResp_DoNotLeakRentedBuffers` - two load tests that exercise the shipped
  `RedisDatabase` deliberately, one of them saying so in its name. They go when the old surface goes, not
  at D2.8.

  **What is left is therefore smaller than the table suggests**: the subscription machinery, and the
  `IServer` members that still build a `Message` (SENTINEL 462, HOTKEYS 212, CONFIG 210, SCAN 175,
  CLUSTER 132, ROLE 108, SCRIPT 75, INFO 53, CLIENT 32, COMMAND 30, KEYS 18, SLOWLOG 10, REPLICAOF 4,
  LATENCY 3, MEMORY 2). The second of those is a mechanical per-member move; the first is one piece.

  **One caveat on the inventory**: it counts `CheckMessage` callers, and the handshake and reconfiguration
  write through `WriteDirectOrQueueFireAndForgetAsync`, which does not pass through it. So the
  configuration machinery's own traffic is invisible here and has to be accounted for separately - it is
  the other thing keeping the bridges alive.

  **What remains of D2.5 is the part that is a different shape**: subscribing registers a handler and
  outlives the call, so `Subscription`, `EnsureSubscribedToServer`, the resubscribe-on-reconnect path and
  the RESP2 second connection all have to move together. `RespNewCore` already has the connection
  (`SubscriptionEndpoint`, `SubscriptionContext`) and nothing but its own tests uses it.

  **Attempted, and deliberately not landed - with the blocker identified.** The outbound half was
  written: `PubSub` gained internal `SubscribeAsync`/`UnsubscribeAsync`, the six spellings chosen by the
  channel's options, and `SingleNodeSubscription`'s send sites routed through this core under the flag.
  It got the pub/sub suites green (221 tests, twice) and then the rest of the suite took it apart. The
  routing is reverted; the group methods and the spelling mapping are kept and pinned in
  `RespSurfaceServerParityTests` against `Subscription.GetSubscriptionMessage`, so the next attempt starts
  from a settled mapping rather than re-deriving it.

  **The blocker, stated once so it is not rediscovered: a subscription must record WHICH CORE holds it.**
  Everything else followed from not having that. `Subscription.IsConnectedAny` asks
  `ServerEndPoint.IsSubscriberConnected`, which describes the shipped bridge's socket; make it ask this
  core instead and every subscription the SHIPPED path owns is then judged by the wrong core. While both
  cores can own subscriptions, that question has one right answer per subscription and no field to hold
  it. The observable failure was precise: kill the connection, downgrade to RESP2, and the
  re-subscription slips past the publish - the in-process server's transcript reads `PUBLISH => :0`
  followed by three late `SUBSCRIBE`s. So the next attempt wants the ownership field first, and probably
  wants to move ALL subscriptions at once rather than per-channel.

  **Four things it found that are worth keeping, and three of them are fixed.**

  - **`SubscriptionEndpoint` trusted an ASSUMPTION about RESP3.** It used the ordinary connection whenever
    `KnowOrAssumeResp3` said yes - including when "yes" came from configuration rather than a negotiated
    fact. Safe for every other reader of that question and not for this one, because subscribing is
    **sticky**: under RESP2 a connection that subscribes enters subscriber mode and refuses everything but
    (un)subscribe, `PING` and `QUIT`. Guessing wrong does not cost a socket, it poisons the ordinary
    connection for every command after it - 46 failures, reported in the server's own words as
    `ERR only [P|S][UN]SUBSCRIBE / PING / QUIT allowed in this context (got: 'PUBLISH')`. Now: an endpoint
    that already has a subscription socket keeps it; otherwise the ordinary connection is used only for
    **known** RESP3. **Fixed**, and worth having regardless of the port.
  - **This core did not refuse a command the map had disabled.** `RespEndpointExecutor.Validate` checked
    database, admin mode and primary-only, but not availability - the shipped pipeline refuses while
    rendering, where `MessageWriter` throws on an empty mapped name, and a core that renders its own
    frames had nowhere making that refusal. `ConfigTests.ConnectWithSubscribeDisabled` asks for it by
    name. **Fixed**, and it applies to every ported command, not just `SUBSCRIBE`.
  - **`Ping` really is inseparable**, as the notes above said. `PubSubTests.TestBasicPubSubFireAndForget`
    is the proof: subscribe fire-and-forget, ping the subscriber to flush, publish, expect the delivery.
    With the subscribe on this core's socket and the ping still on the bridge's, the ping flushed nothing
    and the publish overtook the subscribe at the server. A five-second wait cannot recover a message that
    was never going to arrive.
  - **"Simply known" is wrong wherever a redirect is possible.** A sharded or key-routed channel sent to
    the wrong node answers `-MOVED` and the subscription then lives on the node it was redirected TO, so
    the server chosen before the send is exactly the wrong answer -
    `ClusterShardedTests.SubscribeToWrongServerAsync` in one line: `Expected: 127.0.0.1:7000, Actual:
    127.0.0.1:7001`. Closing that needs the send to report where it FINISHED rather than where it was
    aimed, which is the same capability `IdentifyEndpointAsync` wants.

  **Landed on the third attempt**, and the two that failed first are why it is worth reading the pieces
  rather than just the diff. The single-node subscription now sends on this core, with five parts that
  each exist because a test proved they had to.

  1. **Ownership.** `Subscription._onNewCore`, set by whoever actually sent the (un)subscribe, with
     `IsConnectedAny`/`IsConnectedTo`/`RemoveDisconnectedEndpoints` asking the owning core through
     `IsLiveOn`, and the shipped processor clearing it again when a bridge establishes one - a
     subscription MOVES between cores when a downgrade re-homes it, so "who holds it" is re-recorded
     rather than assumed. Without this, liveness is answered by the wrong core: a subscription reads as
     live because a connection it is not on happens to be up, and then nothing ever re-subscribes it.

     **Sticky**, because whether this core would take a FRESH subscription changes over time - it depends
     on the negotiated protocol, which is unknown at first - so deciding per call let one core subscribe
     and the other unsubscribe, leaving the channel subscribed on a connection nobody was tracking.
     `Issue1101Tests.ExecuteWithUnsubscribe*` reads that as "expected 0 subscribers, found 1" after
     unsubscribing everything. So a subscription already placed decides where the next command for it
     goes, in either direction; only an unplaced one is free to be placed.
  2. **"We are carrying this" and "the server has confirmed it" are different questions**, and
     conflating them loses subscriptions. The ping gate wants the first: a caller who declines the outcome
     then pings the subscriber to flush it, and the ping has to travel on the socket the subscribe went
     out on - true from the moment it is written (`PubSubTests.TestBasicPubSubFireAndForget`).
     `IsConnectedAny` wants the second: answer it optimistically and a later
     `EnsureSubscribedToServer` skips as "already subscribed", so a subscribe that never landed is never
     retried - `Resp3HandshakeTests` reads that as a publish finding NO subscribers at all.

     So ownership and an in-flight endpoint (`_sendingVia`) are recorded at SEND time, for the ping to
     ask about, and the placed endpoint only on CONFIRMATION. An unsubscribe likewise forgets the
     endpoint only once confirmed: until then deliveries can still arrive. In-flight counts as placed
     too, or two concurrent calls race onto different cores.
  3. **Re-subscribe when THIS core's subscription socket establishes.** The shipped core re-subscribes
     when its own subscription bridge comes up; a socket this core brought back had no equivalent
     trigger, so the subscriptions stayed off until something unrelated happened to ask -
     `Resp3DowngradeTests` showed it as `PUBLISH => :0` with the `SUBSCRIBE` arriving afterwards.
  4. **Only a socket whose choice cannot change underneath it**, which is `WouldSubscribeOnItsOwnSocket`:
     this endpoint already has a subscription socket, or its protocol has been NEGOTIATED as RESP2. An
     unknown protocol says no. Sharing the ordinary connection is right under RESP3 and is also where
     issue #3154 lives - this core picks the socket when a send is COMPOSED, so a subscribe composed
     while RESP3 was expected and written after a reconnect negotiated RESP2 lands on the ordinary
     connection and puts it into subscriber mode. Treating unknown as "probably RESP2" accepts exactly
     those.
  5. **The ping goes where this client's subscriptions are**, not merely on a subscription-shaped socket
     (`ConnectionMultiplexer.NewCoreHoldsSubscriptionsOn`). The fallback probe for a server that will not
     answer `PING` in subscriber mode is an unsubscribe from something nobody subscribed to, which is a
     round trip only where a subscription exists.

  **Redirect-capable channels followed, and it did NOT need a "where did the send finish" channel.** A
  sharded or key-routed subscribe sent to the wrong node answers `-MOVED` and then lives on the node it
  was redirected TO, so the server chosen before the send is the wrong answer
  (`ClusterShardedTests.SubscribeToWrongServerAsync`: `Expected: 127.0.0.1:7000, Actual: 127.0.0.1:7001`).
  Two things close it, and neither is a new executor contract:

  - **The redirect has to be followed onto the SUBSCRIPTION socket.** `TryFollowRedirect` resolved every
    target through `_forEndpoint`, which is the ordinary connection - so following a redirected
    `SSUBSCRIBE` the obvious way would fix the routing and poison the connection, because under RESP2 a
    subscribe on the ordinary socket puts it into subscriber mode. There is now a second resolver for the
    six subscriber-mode commands, and under RESP3 the two resolve to the same executor so it costs
    nothing.
  - **Where it landed is re-resolved from the SLOT afterwards, not reported by the send.** A `-MOVED`
    both moves the subscription and teaches this core where the slot went, because `OnSlotMoved` updates
    the map on the way through - so the owner afterwards IS the answer.
    `RespNewCore.EndpointForChannel` asks it, and a channel with no slot answers null, meaning "where it
    was aimed".

  Also worth keeping: no server selected is "ours, nothing to do yet" rather than "not ours", because
  falling through there let the BRIDGE subscribe and then this core subscribe as well - two subscribers on
  one channel, every message delivered twice (`Resp3HandshakeTests`, as `PUBLISH => :2`).

  **Three fixes came out of it that stand on their own.** `SubscriptionEndpoint` trusted an ASSUMPTION
  about RESP3 and had to be made to require a negotiated one - subscribing is sticky, so guessing wrong
  poisons the ordinary connection rather than costing a socket (46 failures, reported as `ERR only
  [P|S][UN]SUBSCRIBE / PING / QUIT allowed in this context (got: 'PUBLISH')`). `RespEndpointExecutor`
  did not refuse a command the command-map had disabled at all, where the shipped pipeline refuses while
  rendering. And the in-process test server diverged from a real one: `RedisClient.Unsubscribe` returned
  early when the client held no subscriptions, so an `UNSUBSCRIBE` there was never confirmed and the
  degraded ping waited for ever - a real server answers with a count regardless.

  **`MultiNodeSubscription` followed immediately**, and needed nothing new: its four send sites route
  through the same `TrySendViaNewCore`, and its liveness questions through the same `IsLiveOn`. The
  sticky-ownership rule covers the multi-node case for free - `IsPlaced` is "any endpoint placed", so the
  first placement decides for the whole subscription rather than per node, which is what stops one
  channel from being half on each core across several servers.

  While in there: `RemoveDisconnectedEndpoints` on that shape read `if (server.Value.IsSubscriberConnected)`
  and then removed it - **inverted** against both the method's name and the single-node sibling, which
  removes when `false`. The effect was benign, because `GetSubscriptionChange` re-checks liveness rather
  than trusting the record, so the cost was redundant `SUBSCRIBE`s on reconfigure rather than a lost
  subscription. Corrected with the negation it was missing.

  **`IdentifyEndpointAsync` closed it, and did not want the reporting channel either.** The shipped
  version reads the endpoint off the connection its `PUBSUB NUMSUB` reply arrived on, which is whichever
  one routing chose - so choosing before the send and answering that is the same fact by a shorter route.
  Safe because `PUBSUB NUMSUB` is keyless and node-local and therefore cannot be redirected, which is
  exactly what made the SUBSCRIBE case hard and this one easy. The round trip still happens: it is what
  makes the answer an observation rather than a guess.

  **And it found a defect in the slot resolution from the previous step.**
  `EndpointForChannel` computed the slot from the channel's own bytes, where the server routes by the name
  it RECEIVES - the prefixed one. So with a channel prefix configured, a subscription landed on one node
  and was recorded against another; `ClusterTests.ClusterPubSub(withKeyPrefix: true)` is what that looks
  like, and it only showed up once `IdentifyEndpoint` moved and the two stopped agreeing by accident.
  It now asks `ServerSelectionStrategy.HashSlot(in RedisChannel)`, which already handles the prefix and
  `IgnoreChannelPrefix` - borrowed rather than re-derived, which is the rule that should have been
  followed the first time.

  **D2.5 is done**: publish, subscribe and unsubscribe for every channel shape, the ping, the
  re-subscribe, and the endpoint identity all travel on this core under the flag.

  **So the obvious next move is to stop CONSTRUCTING the subscription bridge - and that was tried, and
  measured, and is not a one-liner.** Not activating it under the flag, plus relaxing the
  "fully established" gate so a connect does not wait for a leg nobody dials, gives **63 failures**
  across the pub/sub, handshake, cluster and config families. The mechanism is a chain, and it is worth
  having written down:

  - The subscribe path has a DECLINE for a subscription that would share the ordinary connection, which
    exists because this core picks its socket when a send is composed and a later downgrade can move it
    (issue #3154). That decline was safe only because the shipped bridge was there to take those
    subscriptions.
  - Remove the bridge and the decline has nowhere to drain: the fall-through becomes a subscribe that goes
    nowhere at all, which `ClusterTests.ClusterPubSub` reports as an empty subscribed endpoint.
  - Remove the decline as well, so this core takes every subscription, and the shared-socket path becomes
    live for the first time - which is where the other 63 come from.

  The pieces to close it already exist and are the ones D2.5 built: `SubscriptionEndpoint` shares only on
  KNOWN RESP3, `IsSubscriptionConnected` stops answering yes for a shared socket once the negotiated
  protocol drops, and the establish-time `EnsureSubscriptions` then re-places the subscription on a
  dedicated one. What has not been done is making that chain actually hold for a subscription composed
  before a downgrade and written after it - the narrow window #3154 names - and 63 failures is the
  measurement of how much of the suite depends on getting it right rather than nearly right. **That is
  the next piece of work, and it is the gate on halving the socket count.**

- **D2.6 - Re-home the per-server beliefs** (script cache, `RunId`, profiling context) out of
  `ServerEndPoint`.
- **D2.7 - Maintenance and sentinel.**
- **D2.8 - Stop constructing bridges under the flag**, then delete `PhysicalBridge` and
  `PhysicalConnection` when the flag becomes the only behaviour.

  **This is the goal, and everything else is now waiting on it**: the `ReconnectRetryPolicy` pair (9b-ix),
  D2.3's mode default and the 28 connection-count failures `Discover` costs, and every remaining
  two-socket artefact. So it is worth stating what actually holds the bridges up, because it is not the
  thing the `Message` inventory counts.

  **Four dependencies, not one.**

  1. **Discovery.** `ConnectionMultiplexer.ReconfigureAsync` establishes every `ServerEndPoint`'s beliefs -
     server type, version, databases, replica-ness, protocol, run-id, connection-id - by handshaking the
     SHIPPED bridge and reading `ECHO`/`INFO`/`CLUSTER`/`CONFIG` through it. None of that traffic appears
     in the inventory, because it goes out through `WriteDirectOrQueueFireAndForgetAsync` rather than
     `CheckMessage`. This is the one that matters: until the client's beliefs can come from somewhere else,
     the shipped bridge must connect, and so must exist.

     *Started.* This core handshakes its own socket and learns the same facts; it now PUBLISHES them -
     version and server type - to the modelled `ServerEndPoint`, and `ServerEndPoint.Protocol` answers from
     this core when the bridge has no answer. Roles and selectability already flowed the other way, which
     is the shape: one fact, discovered once, told to whoever needs it.

     `databases` and `replica-read-only` followed, in `RespHandshake.DiscoverServerConfigAsync`: two
     `CONFIG GET` reads on an interactive dial, published to the modelled `ServerEndPoint`. Three things
     about it are deliberate.

     - **Only when nothing has described the server yet**, which `Databases == 0` says exactly - it is the
       value a `ServerEndPoint` starts at, and both settings are discovered together on the shipped path.
       Asking unconditionally would put two round trips on every dial, which on a large cluster is per node
       and is the cost the lazy design exists to avoid; and they are server-wide answers, so asking twice
       learns nothing. The effect is self-adjusting: while the shipped handshake still discovers, this does
       nothing; when it stops, this is what knows.
     - **Not subject to admin mode**, because it runs during the dial on a context over the bare
       connection and the admin check lives on `RespEndpointExecutor`. That is the same exemption the
       shipped handshake gets from `SetInternalCall`, and for the same reason: `CONFIG` is restricted so a
       CALLER cannot reconfigure a server by accident, not because the client may not know its shape.
     - **A declined `CONFIG` answers null rather than failing the dial.** It is restricted on plenty of
       managed deployments, and the client has a default for every one of these facts.

     `RespNewCoreDiscoveryTests` pins both halves - what is learned, and that a described server is not
     asked again - by clearing the beliefs first, because the shipped handshake has already run by the time
     a test can look and asserting on the values as found would pass whoever learned them.

     What is still missing is `run-id`, `connection-id`, and - the real work - `ReconfigureAsync` itself
     routing its probes through this core rather than through a bridge.

  2. **Subscriptions** (D2.5's remainder). The shipped SUBSCRIPTION bridge is a whole second socket per
     endpoint and nothing else uses it, so this is the clearest halving available: `Subscription`,
     `EnsureSubscribedToServer`, the resubscribe-on-reconnect path, `Ping` and `IdentifyEndpointAsync` all
     move together.

  3. **The `IServer` long tail.** `Message.Create` sites, each wanting a group method and a handler.
     Mechanical and wide rather than hard; `INFO` is the worked example, parse-sharing included.

     *Started.* 68 sites at the time of writing, **33** now. To `Diagnostics`, which already carried the
     two `DOCTOR`s, `MEMORY PURGE`, `SLOWLOG RESET`, `LASTSAVE`, `COMMAND COUNT`, `ECHO`, `TIME` and
     `INFO`: `LATENCY RESET`/`HISTORY`/`LATEST`, `MEMORY STATS`, `SLOWLOG GET`, `COMMAND GETKEYS`,
     `COMMAND LIST`, `CLIENT LIST`, `ROLE`, the `SAVE`/`BGSAVE`/`BGREWRITEAOF` family, and `SHUTDOWN`. To
     `Config`, which already carried `REWRITE` and `RESETSTAT`: `CONFIG GET`/`SET`. To `Keys`: the
     no-`SCAN` `KEYS` fallback. To `Strings`: the database-scoped `GET`. **26 of the 33 remaining are
     `SENTINEL`**, which stays - see below - so the real remainder is 7: `SCAN` (5 spellings of one
     cursor page), `CLIENT KILL` (its filter wants modelling rather than forwarding a token list), the
     `CLUSTER NODES`/`SLOTS` call sites (the builders themselves stay, because `AutoConfigureAsync` and
     the multiplexer use them), `REPLICAOF`/`SLAVEOF`, and the multiplexer's own plumbing - the
     tie-breaker `GET`/`DEL` and the reconfigure `PUBLISH`, which travel as part of `MakePrimaryAsync`'s
     ordered sequence rather than as `IServer` members.

     `GetMemoryPurgeMessage` is **deleted** rather than ported: nothing in `src/` used it any more, so it
     was production code kept alive for a test to look at. Its assertions moved onto the request the group
     method issues, which is where the other ported categories already are.

     Two handlers now live in `RespHandlers` rather than beside their callers - `KeyArray` and
     `StringArray` - because `KEYS`, `COMMAND GETKEYS`, `COMMAND LIST` and the `SCAN` pages all want one,
     and three copies of "array of scalars, nil reads as empty" is three chances for one to disagree. The
     checks they wrap are shipped ones too: `ResultProcessor.ScalarSays` is what decides whether a
     `BGSAVE` said it had started, shared with the processor that used to own it.

     **The ports cost sockets, and the tests absorb that rather than the ports waiting for it.** `CLIENT
     LIST` is the worked example: it is the first context-surface call three of the `DefaultOptionsTests`
     make, so routing it to this core makes this core dial its own socket, and a test asserting "this
     server has two clients" sees three. Holding the port was the wrong answer - the direction is
     removing the `Message` machinery, and an artefact that exists *only* because two paths exist is not
     a reason to keep one of them. So the tests say what is true of the process as it is actually
     running: `OtherCoreSockets` adds this core's connections to the expected count, and goes to zero on
     its own when there is no second core. The connection SHAPE - one interactive, plus a subscriber under
     RESP2, per core - is still what is asserted.

     The other shape this takes is **ordering**, and `ConfigTests.ClientLibraryName` is that one: the
     library-name retro-fix is fire-and-forget on the connection being renamed, and `CLIENT LIST` now
     reads on the other core's, so the read can overtake the write it is meant to observe. One socket
     orders them; two do not. The test polls instead of asserting an ordering nothing is offering, and
     collapses back to a single read when there is one socket again.

     Three judgements worth not re-deriving. The array-returning group methods are **internal**, as
     `Hashes.GetAllArray` is: an array of string pairs is the OLD spelling, and what the new surface
     should offer for `CONFIG GET` is a separate question from getting `IServer` off the `Message` path -
     answering it by accident during a port would be a public shape chosen for a porting convenience. The
     same goes for `CommandGetKeysArray`, `CommandListArray` and `ClientListArray`. `CLIENT LIST` is in
     the diagnostic group rather than a `Client` one, because that is what the question is - "what does
     this server see?" - whereas the other `CLIENT` verbs are different kinds of thing: `SETNAME`/
     `SETINFO` and `TRACKING` are the handshake telling the server about US, and `KILL` is
     administration. A group named for the command word would collect things with nothing in common. And
     `ConfigSet`'s follow-up read is **deliberately left on the shipped path**: the point of that read is
     not its reply but what `ResultProcessor.AutoConfigure` does with it - publish the setting to the
     `ServerEndPoint`, which is how `databases`, `timeout` and `replica-read-only` stay true after a
     caller changes them. There is no context-surface equivalent yet, and the split is harmless because
     `CONFIG` is server-global rather than connection state, so the other socket answers the same
     question.

     Two things worth copying into the next batch. The element parses are **shared, not rewritten**:
     `LatencyHistoryEntry.TryParseEntry` and its sibling are now internal statics called by both the
     shipped `ArrayResultProcessor` and the new handler, so a server's own account of its latency cannot
     depend on which core asked - the same argument as `Diagnostics.ParseInfo`. And the risk in a port of
     this shape is the **wire form**, not the result: a `LATENCY RESET` that dropped an event name would
     quietly reset the wrong set. So `RespSurfaceDiagnosticsParityTests` keeps the shipped
     `Message.Create` calls - driven for real through `MessageWriter` - as the expected bytes, which is
     what the stream and key parity suites already do and is the only assertion that cannot be satisfied
     by misreading the code being replaced. The **retry category** goes in the same place, for the same
     reason: `CommandRetryCategoryUnitTests` could only reach it through a `Message` builder, and
     `RespRequest.Flags` carries it, so a ported command's category is asserted on the request the group
     method issues. Keeping a `Message` builder alive for a test to look at would be the alternative, and
     a worse one.

  4. **Sentinel** (D2.7), which is 462 of the inventory's messages and has its own connection model.

  **How to sequence it.** (1) is the gate - nothing else removes a bridge on its own, because a bridge that
  exists for discovery is a bridge that connects. (2) is the biggest visible win and is independent of (1).
  (3) and (4) can proceed in parallel with either.

  **The gate measurement has been run, and (1) is closed for the probe burst.** `AutoConfigureAsync`
  early-returns; both flags set; full suite. Two runs: 4 failures and then **exactly 2** - the
  `ReconnectRetryPolicy` pair and nothing else, 11,179 passing, which is the baseline with the burst still
  running. The two extras in the first run (`BacklogTests.FailFast`, `ClusterTests.MovedProfiling`) are
  both known rotators.

  So none of it is load-bearing any more: `CONFIG GET` x3, `INFO replication`, `INFO server`, the keyed
  `SET` role probe, `CLUSTER SLOTS`, `CLUSTER NODES` and the tie-breaker `GET`. This core's handshake
  supplies version, mode, role and peers, slot ranges, `databases` and `replica-read-only`, and publishes
  them; the suite does not miss the rest.

  **Three caveats, because "the suite passes" is weaker evidence for a probe whose consumers the suite does
  not exercise.** Sentinel is skipped in this environment, so `SENTINEL MASTERS` is untested and D2.7 owns
  it. The tie-breaker `GET` only decides between MULTIPLE primaries, which the topology here never has.
  And `run-id`'s only job is to notice a restart and flush the script cache, which no test restarts a
  server to check. Those three should move deliberately rather than on the strength of a green run - which
  is why the burst is still in tree rather than skipped under the flag.

  **What the measurement moved the gate to.** It is no longer *beliefs*: it is the *connect wait*.
  `ReconfigureAsync` waits per endpoint on `ServerEndPoint.OnConnectedAsync`, which is completed only by a
  `PhysicalBridge` - `IsConnected` reads `interactive?.IsConnected`, and the pending monitors are completed
  from `OnFullyEstablished`. So a bridge is still constructed and still dialled for every endpoint, and
  that is now the whole of what holds it up.

  Satisfying that wait from this core is three small pieces - `IsConnected` counting this core's
  established connection, this core completing the pending monitors when it establishes one, and
  `Activate` asking this core to dial - but they **cannot land before the bridge stops dialling**, because
  until then both cores would dial every endpoint and the connection-count tests (the same 28 that D2.3's
  `Discover` default costs) would count twice. So that trio and "stop constructing bridges" are one step,
  and it is the next one.

  **And the bridge cannot stop being dialled while anything still sends a `Message` through it**, which is
  what items (3) and (4) are: 76 `Message.Create` sites on `IServer` and 462 on sentinel. Porting them
  one at a time is the obvious reading of the plan and it is probably the wrong one, because there is a
  lever.

  **A `Message` can already be rendered without a `PhysicalConnection.`** `MessageWriter` has a
  `(channelPrefix, map, IBufferWriter<byte>)` constructor and detects a `RespFrameWriter`, so
  `Message.WriteTo(in MessageWriter)` renders a frame this core can send. What is left coupled is the
  REPLY: `SetResultCore(PhysicalConnection connection, Message, ref RespReader)`.

  Measured, because the number decides the strategy: **92 `SetResultCore` overrides, of which 26 touch
  `connection` at all** - 19 in `ResultProcessor.cs` and the rest one apiece. And what those 26 want is a
  short, flat list of connection facts: `OnDetailLog`, `BridgeCouldBeNull` (for the `ServerEndPoint`),
  `RecordConnectionFailed`, `SetProtocol`, `ConnectionId`, `SubscriptionCount`, `Protocol`,
  `MultiDatabasesOverride`. This core's connection knows every one of them.

  So the lever is an interface over those eight members, implemented by `PhysicalConnection` and by
  `RespClientConnection`, with `SetResultCore` taking it instead of the concrete type. 66 overrides change
  only a parameter type; 26 need looking at. That is one wide mechanical change against 538 hand-ports,
  and it makes EVERY remaining `Message` site work through this core at once - which is what lets the
  interactive bridge stop being dialled without `IServer` and sentinel having to be rewritten first.

  **Weighed against porting the tail, and the tail wins - which is a correction to the paragraph above.**
  The lever has a cost that counting overrides does not show: `SetResult` runs for every reply on the
  shipped path, so making its `connection` an interface puts an indirection in the hot path of the library
  as it ships today, for the sake of scaffolding that is deleted at the end. And it is one big-bang diff
  across 92 overrides, with the risk concentrated in the reply dispatch that every command depends on.

  Porting is 68 `Message.Create` sites in `RedisServer.cs` across ~22 commands (`CLIENT`, `CLUSTER`,
  `CONFIG`, `LATENCY`, `MEMORY`, `SCRIPT`, `SLOWLOG`, `SHUTDOWN`, `REPLICAOF`/`SLAVEOF`, `SAVE`/`BGSAVE`,
  `ROLE`, `COMMAND`, `KEYS`/`SCAN`, ...). Each is independently verifiable, costs the shipped path nothing,
  and - the deciding point - **a group method is the destination rather than scaffolding**. The reply
  interface would be thrown away; these are kept.

  **Sentinel does not have to move for the interactive bridge to go.** A sentinel is its own
  `ServerEndPoint` with its own `ServerType` and connection model, so it can keep a bridge after ordinary
  servers stop having one. That takes item (4)'s 462 messages off the critical path entirely, and leaves
  the reply interface as the fallback lever for if sentinel ever does have to move with everything else.

  Revised sequence, then: (3) the `IServer` tail, (2) subscriptions, then the connect wait and "stop
  constructing bridges for non-sentinel servers" together. (4) sentinel keeps its bridge until last.

  **One amendment after porting most of it**: a port that is the first context call on a path some test
  counts connections on costs a socket while both cores exist, and an ordering guarantee that one socket
  provides and two do not (`CLIENT LIST` is the worked example - see item (3)). Those are artefacts of
  having two paths, so they are absorbed in the tests rather than allowed to hold a port back; the
  direction is removing the `Message` machinery, not protecting the transitional state. What it does say
  is that "stop constructing bridges" wants doing sooner than the original ordering implies, because
  every one of these artefacts disappears the moment it lands - and that (2) subscriptions is the better
  next large piece, being the biggest halving available and independent of all of this.

#### Where the engine flag stands

**Two stable failures, down from 33** - and **both flags together is the measurement**, which is worth
stating first because measuring the engine flag alone wastes a day.

- `ReconnectRetryPolicyUnitTests.RetryPolicyFailureCases` x2 - 9b-ix: not answerable while both cores
  consult one policy object. Assertable at D2.8.

11,179 pass, 219 skip. The default suite is 11,177/0 and RESPite 1,947/0 throughout.

**The engine flag on its own is a configuration nobody will ever ship, and its failures are artefacts of
that.** Measured: 9 stable failures with `SEREDIS_NEW_CORE_ENGINE` alone, and **all nine pass with
`SEREDIS_NEW_DATABASE_SURFACE` as well**. The mechanism is one thing in every case - the surface flag
decides what carries an `IDatabase` command and the engine flag decides what carries a context one, so
with only the engine flag an `IDatabase` write goes out on the shipped bridge's socket while `IServer` and
the caching surface go out on this core's. A test whose mover and whose oracle are then on different
sockets fails for a reason that has nothing to do with either core being wrong:

- `ServerExecuteDatabaseTests` x3 - moves the selection with `IDatabase` and reads it back with
  `IServer.Execute("CLIENT", "INFO")`. Different sockets, so `db=0` where the test wants `db=18`.
- `RespInProcTrackingTests` x3 and `RespCacheInvalidationTests` x2 - the invalidation arrives for the
  connection that registered interest, and the write that should invalidate is on the other one.
- `MultiMessageInTransactionTests.TheFrameSurfacesComposedPairIsRefused` - the transaction and the
  script are on different cores.

So the two-flag number is the one to drive to zero, and a failure seen under the engine flag alone should
be re-run with both before it is believed.

**Two of the things that used to rotate turned out not to be flaky at all**, which is worth recording
because "flaky" was the wrong diagnosis twice in a row:

- `RespResultLeaseSharingTests` asserts CHANGES to `RespResult.RefCount`, and that count belongs to the
  receive BUFFER rather than to the result - so on the shared fixture another test's payload being
  released between the two reads moves it. They read as load-dependent and were measuring a number
  somebody else was also changing. Private connections now.
- `BatchTests.TestBatchSent` was the write-slot ordering bug in 9b-xii.

What is left in the rotating column is `ClusterTests.MovedProfiling` (RESP3),
`RespResultTests.ScriptEvaluateReadOnlyResp_Works` and `RespAggregateTimingTests` - each seen once across
three runs. `ScriptEvaluateReadOnlyResp_Works` reads null from a key it has just written, which looks like
a wrong-database script; the obvious mechanism was checked and ruled out - the paired preamble path DOES
carry its `SELECT`, and `SelectInjectionTests.APairedPreambleCarriesTheSelectToo` now pins that so the
next reader does not have to re-derive it.

#### What would tell us it is going wrong

The honest risk is that D2.1 makes this core eager and the reason the lazy design existed reasserts
itself - a large cluster paying connect cost it did not pay before. The measurement to keep is
connections opened per multiplexer at steady state, which "eager-once" should leave at one plus
whatever traffic demands.

The second risk is scope: steps D2.4 through D2.7 are each a surface with its own tests, and none of
them is on the critical path to *routing* correctness. They can be sequenced by test count rather than
by architecture, and should be.

### 9b-vi. Fire-and-forget declines the outcome, and this core does not let it

**The gap.** `CommandFlags.FireAndForget` means the caller has declined the result - and that includes
the bad ones. This core returns the real result and, worse, the real exception: a command issued
fire-and-forget against a server that had just gone away throws at a caller who said they were not
going to look. `Issue2392Tests.Execute` and
`RespEndToEndTests.SynchronousFireAndForgetReturnsDefaultRatherThanThrowing` both say so, and the
design already assumes it is handled - `Parse`'s remarks state that "the caller has explicitly declined
it, so the pipeline never captures one and the executor hands back null". The executor simply never
did.

**Attempted in `RespEndpointExecutor.Send`/`SendAsync`, and that is the wrong layer.** Both target tests
passed; the engine suite went 38 -> 61 and the DEFAULT suite broke, which is the part worth recording.
The failures were counting mismatches rather than crashes - `ScriptingTests.MultiIncrWithoutReplies`
expecting 1 and getting 0, `SO10504853Tests.LoopLotsOfTrivialStuff` expecting 1 and getting 3 - because
that executor serves runs as well as single sends. A transaction of fire-and-forget commands still needs
each queued reply: `+QUEUED` receipts and then the positional `EXEC` array. Discarding them there
shifted every result.

**Narrowed by bisecting the two halves, and the answer was not the layer at all.** The executor IS the
right place - `RespClientCacheTests` asserts a null result for a fire-and-forget send, so the contract
is exactly the one `Parse` documents. Short-circuiting the SYNCHRONOUS `Send` is safe and fixes
`SynchronousFireAndForgetReturnsDefaultRatherThanThrowing`; it is only `SendAsync` that breaks things,
and the breakage is not about fire-and-forget.

**Two candidate causes have been tried and ruled out, which is the useful part of the record.**

*Disposal racing the write.* `SO10504853Tests.LoopLotsOfTrivialStuff` issues
`KeyDelete(key, FireAndForget)` and disposes the multiplexer immediately, inside the same `using`; the
delete was lost and the test saw 4 where it wanted 1. A bounded drain on close - wait for each
endpoint's backlog and pending queue to empty, honouring the `allowCommandsToComplete` that
`CloseAsync` already advertises - does fix that test. It is NOT kept, because it regressed two others
(`TheEndpointExecutorReconnectsAfterARealSocketDies` and `DisconnectAndReconnect...Sync`): a connection
that is dead with operations still pending never reaches zero, so close waits out the whole timeout.
A drain has to distinguish "still going" from "never going to finish" before it can land, which the
shipped `QuitAllServers` sidesteps by sending an actual `QUIT`.

*Pairing jumping the backlog.* The preamble pair writes straight into the connection's write lock,
walking past anything queued - so a script issued while earlier commands were still waiting could go out
ahead of them, which would explain a script running before the `DELETE` it was meant to follow. Making
the pair fall back to sequential whenever a backlog exists changed nothing, so that is not the
mechanism either.

*The scripting failures were a third thing entirely, and it is now fixed.* A minimal repro narrowed it
to the concurrency shape rather than to fire-and-forget: the script task and the reads that follow it
are all started before any of them is awaited. `SequentialAsync` - the no-connection fallback for a
preamble pair - **awaited the preamble's reply before sending the request**, so everything the caller
issued in between went out in between. The `EVALSHA` was written after the `GET`s that followed it at
the call site, and they read the state from before the script ran.

Fire-and-forget only exposed it: returning early leaves the connection cold, and a cold connection is
exactly what selects that path. Both frames are now queued in order and only then awaited, which is all
`EVALSHA` ever needed - the server processes them in order, so the script is loaded by the time the hash
is used - and it costs one less round trip than awaiting did.

*The drain is now in, and correct.* It waits only on work that can still finish - written commands while
their connection is alive, queued commands while there is a connection or one on the way - because the
naive version waited out the whole timeout for commands stranded behind a connection that was never
coming back, which is the ordinary state of a shutdown after a failure. `CloseAsync` advertises
`allowCommandsToComplete`; this core now honours it.

*The async half is now in, and what was actually blocking it was nothing about fire-and-forget.* Three
separate things had to be true first, and only the first was about this core at all.

**One: `CloseAsync` never drained this core** while the synchronous `Close` did. Every `await using` -
which is every test and most callers - therefore dropped whatever this core still owed, and a
fire-and-forget command issued immediately before disposal simply never happened. That is `SO10504853`
and `Issue2392`, and it was mistaken for a fire-and-forget problem because only fire-and-forget leaves
anything owing at the moment of close. Fixed independently.

**Two: D2.4.** With `IServer` on the shipped core's socket and the writes on this one, returning before
the write landed removed the only thing ordering the two against each other. `DatabaseTests.CountKeys` is
that race exactly. Fixed by D2.4, and 9b-xi records the family.

**Three, and this is the one that was misdiagnosed twice: a caller that does not wait for its own replies
no longer establishes that the server has EXECUTED them.** After D2.4 the short-circuit still cost twelve
`HotKeysTests`, which cannot be a two-socket race because there is one socket. The measurement that
settled it: with the short-circuit in, `HOTKEYS` reports **nothing at all** - `cpuKeys=0`, `netKeys=0` -
while the key itself reads `20`, so the increments ran. They ran, and `HOTKEYS STOP` had already closed
the window they were supposed to be measured in. One awaited round trip anywhere between the writes and
the STOP makes every one of those tests pass, and it has to be a command routed by the same key, so that
on a cluster it reaches the node the increments went to.

So the tests were relying on an accident: awaiting each fire-and-forget command was itself a round trip,
because this core completed such a command only when its reply arrived. That is precisely what
fire-and-forget is supposed not to do. The workload is now written once, in `IncrementTwenty`, ending in
a keyed read - which is the honest statement of what the assertions need.

Worth keeping as a general lesson, because it will recur: **anything that asks the server what it just
saw has to round-trip first.** Fire-and-forget promises the command is sent, not that it has happened.

*And the same short-circuit on `RespDatabaseExecutor`*, the per-database view, which had to be there:
otherwise `GetDatabase(3)` throws at a fire-and-forget caller where `GetDatabase(0)` does not, and the
behaviour depends on which database you happened to be on. It cost eight tests when first measured and
three by the time it landed - D2.4 and the `CountKeys` round trip had already taken the rest - and all
three were the same shape again, on a dedicated database this time: `ScanTests.KeysScan`,
`ScansIScanning` and `KeyTests.TestScan` write fire-and-forget and then ask the server to scan them. One
awaited round trip on the same database each.

`RespOperationBatch` and `RespTransaction` keep their own handling, which is the other half of the same
observation: the decision is per-composition, and each composition answers it where it queues.

### 9b-vii. MOVED to the endpoint we are already on

**Not a statement about the slot map.** A `MOVED` naming the endpoint the reply arrived on happens when
a name resolves to something that has changed underneath - DNS, a load balancer, a proxy - so the
address is still right and the CONNECTION is stale. Re-sending on the same socket gets the same answer
forever, and since a command may follow only one redirect, the second `MOVED` surfaced to the caller as
an error. That is what `MovedUnitTests` has been failing on.

The remedy is the shipped one: replace the connection, then send the command again on its replacement.
Two things had to exist first, and both now do - `DropConnection`, and the habit of handing teardown to
the pool rather than doing it on the read loop of the connection being dropped, which is the self-join
an earlier attempt at this found and was reverted for.

**And a third thing, which was the actual blocker.** After the drop, the resend sat in the backlog until
it timed out, because `EnsureConnecting` consulted the reconnect backoff - an interval meant for a
server that is refusing us, applied to a connection we had just deliberately closed. `DropConnection`
now takes `reconnectImmediately` for that case. Relaxing the backoff generally was tried first and is
wrong: `ReconnectRetryPolicyUnitTests` asserts the policy IS consulted, and three more of its cases
failed.

**The behaviour is complete; what was left is a lazy-connect artefact.** Instrumenting the fake server
settled it, and the first reading was wrong: the failing assertion is not `SetCmdCount` but
`TotalClientCount` one line below, and the two happened to disagree by the same numbers. The server
handles exactly two `SET`s - `PROBE Set #1 on client 2`, `PROBE Set #2 on client 3` - which is what the
test asks for.

What differs is the connection count. The baseline is taken after a `PING`, and with `ConnectMode.Lazy`
this core has not dialled by then, so `initialConnectionCount` is 1 (the shipped core's) where the
test's model expects the client to be connected. It then sees three: shipped, ours, and ours again
after the reconnect.

**Confirmed by flipping the default**: with `ConnectMode.Discover`, all 22 `MovedUnitTests` pass. So the
`MOVED`-to-self work is done, and these tests are gated on eager connect rather than on redirect
handling - which makes them evidence for D2.0 rather than a MOVED bug.

The default stays `Lazy` regardless, because flipping it is a package deal: measured across the whole
engine suite it fixes ten and costs twenty-one, all of them connection-counting expectations plus the
two `ConnectMode` tests that assert lazy preconditions. Those become correct rather than merely
different at D2.8, and that is when to pay for them.

### 9b-viii. Configured command timeouts, and the diagnostics that make one useful (fixed)

**The gap.** `SyncTimeout` and `AsyncTimeout` did not reach an operation that had been written and was
waiting for a reply. The synchronous wait is `Wait(token, TimeSpan.Zero)`, and zero is the monitor's *no
deadline* case - so a command against a server that had stopped answering blocked until
`OperationBackstop` cancelled it, two minutes later.

**What it was not.** Passing the configured value into `Wait` is the obvious fix and is wrong on its own:
it fixed nothing in that family and broke `MaintenanceRelaxationTests`, which then reported a connection
failure where it wanted a timeout. Adding the relaxation-aware value did not change that. Tried,
measured, reverted.

**What it needed** was what the shipped core does: a sweep over operations that have been WRITTEN, driven
by the multiplexer heartbeat. `RespConnection.ExpirePending` walks `_pending` and times out whatever has
waited too long, and `RespEndpointExecutor.OnHeartbeat` drives it with the relaxation-aware timeout.

Three things fell out of it that were not obvious in advance, and each was a real defect of its own:

- **A timed-out operation stays in the pending queue.** Replies are matched positionally, so removing it
  would mis-address every reply after it. That makes "queued" and "still going" different questions, and
  `UnfinishedCount` was asking the first while meaning the second - so closing waited out the full
  timeout for commands that had already given up. Hence `RespConnection.UnfinishedPendingCount`.
- **The window has to be consulted on every sweep**, the backlog as much as the written queue: a command
  waiting for a connection to a server that is mid-migration is precisely what a maintenance window
  exists to rescue.
- **`ExpireBacklog` was reporting the wrong exception type.** It always raised
  `RedisConnectionException`; the shipped rule is that a connection fault is reported only when
  connecting has actually been *failing*, because then the timeout is a symptom and the connect fault is
  the cause. A command behind a connect that is merely slow has timed out. Sweeping the backlog from the
  heartbeat is what first exercised the difference.

**And then the diagnostics, which are most of the value.** A timeout exception's worth is its tail -
`qs`, `qu`, `in`, `last-in`, `cur-in`, the thread-pool counters, `sync-ops`/`async-ops` - and this core
produced none of it, because `ExceptionFactory` required a `Message`. That dependency was incidental:
`IFaultSubject` is the subset a fault report actually needs (what the command was, which key, how far it
got, which connection type, whether anyone awaited it), implemented by both `Message` and
`RespPayloadOperation`. `ServerEndPoint.GetBridgeStatus` now merges this core's connection state into the
same `BridgeStatus` every diagnostic surface already reads, so the tail describes the socket the command
was actually on rather than an idle bridge reporting zeroes.

**What remains, and it is not this.** `AsyncTests.AsyncTimeoutIsNoticed` now satisfies every assertion in
its body - message wording, `last-in`, `cur-in`, `Redis-Last-Result-Bytes`, and `async timeouts: 1` - and
still does not pass, because under the engine flag the test's teardown finds an ambient connection
failure that the shipped-only run does not have. It is pre-existing and independent: the shipped core's
own connections are dropped during the test's `CLIENT PAUSE 4000` (measured at two such failures with
the sweep disabled, one with it enabled), and they are dropped because they are there at all. That is
D2.8's business - stop constructing bridges - not this section's.

### 9b-ix. The reconnect retry policy is consulted by both cores, and the sequence cannot be right

**Two remaining `ReconnectRetryPolicyUnitTests.RetryPolicyFailureCases` cases, and the cause is the
arrangement rather than either core.** `ConfigurationOptions.ReconnectRetryPolicy` is one object, and
under the engine flag both `PhysicalBridge` and `RespEndpointExecutor` ask it. The test asserts the exact
sequence of counts the policy was handed - `"0,1"` or `"0,1,2"` - so any interleaving of two independent
askers produces something it rejects.

**Measured rather than inferred.** Instrumenting both call sites for `FailureMode.SlowNonConnect` gives,
in order: `NEWCORE 0`, `BRIDGE 0`, `NEWCORE 1` - which is the `"0,0,1"` the test reports. The other cases
show only `NEWCORE`, and they pass. So it is two askers, each counting correctly from its own zero, and
neither is wrong: with two cores there really are two sets of connections backing off. What cannot be
right until there is one is the *sequence a single policy object observes*.

**What was tried, and what it cost.** The new core's gate exempted only the never-connected case, so a
socket the SERVER dropped - nothing failed, nothing to back off from - still consulted the policy. That
is wrong on its own terms, and fixing it (exempt any attempt with no failure behind it, and number the
retries from zero as `PhysicalBridge` does by asking before it increments) makes `FailureMode.Success`
pass, where the policy should be consulted not at all.

It also breaks the three cases that were passing, from `"0,1,2"` to `"0"`, and the reason is worth the
record: with the exemption in place the new core asks **once per failure run**, and a run ends whenever
a connect succeeds - which it does, repeatedly, because the injected failures are shared with the other
core's reconnects. The old gate produced a rising sequence only by asking on attempts that were not
retries at all.

So the trade is one test for three, and it was reverted. The correct version of the gate is the one
described above; it becomes assertable at **D2.8**, when there is one asker.

### 9b-x. Client-side caching has to be negotiated on the connection that reads

**`CLIENT TRACKING` is per-connection, and in per-key mode the server registers what THAT connection
read.** It was being asked for in `ServerEndPoint.EnableClientTrackingAsync`, on the shipped core's
socket, while under the engine flag every read happens on this core's. Broadcast mode survived that -
the server pushes on the tracking connection whoever did the reading - and per-key did not: nothing was
ever registered, so nothing was ever invalidated, and the cache served stale data indefinitely. That is
the failure shape the refusal-on-RESP2 rule exists to prevent, arrived at from the other direction.

`RespHandshake` now asks, on the interactive connection it is bringing up, and the shipped handshake
stops asking when the other core owns the reads. The REFUSAL stays where it was, and deliberately: it is
a statement about the configuration, and it has to be made while somebody is still connecting, where the
caller sees it. Two tracking clients per endpoint would also mean duplicate invalidation pushes in
broadcast mode - `RespInProcTrackingTests` asserts `Single` on the tracking client and caught exactly
that when both cores asked.

### 9b-xi. The rotating "flake" family was one artefact, and D2.4 closed it

**Nine to twelve of the remaining engine-flag failures rotate identity between runs, and they are not
flaky tests.** Every one has the same shape: an `IServer` call on the SHIPPED socket, fire-and-forget
writes on THIS core's socket, and then a read that has to see them. They pass in isolation and under the
default suite because the race is narrow, and they pass or fail per run according to scheduling.

    server.FlushDatabase(dbId, FireAndForget);                     // shipped socket
    for (...) db.StringSet(prefix + i, ..., FireAndForget);        // this core's socket
    var count = server.Keys(dbId, prefix + "*").Count();           // shipped socket  -> 999 of 1000

`KeyTests.TestScan`, `KeyTests.FlushFetchRandomKey` (flush on one socket, `RANDOMKEY` on the other) and
`SortedSetTests.SortedSetRangeViaScript` are the recurring members, each with its `NewCore*` and
`Transitional*` twin. `HotKeysTests` is the same thing made deterministic by the async fire-and-forget
short-circuit, which is why 9b-vi and this section resolve together.

Under one core these are all FIFO on one connection. **D2.4 closed the whole group at once** - engine-flag
failures went 23 to 12 on that one change, and every member of this family went with it. The prediction
held; the same reasoning applied to `HotKeysTests` did not, and 9b-vi records that correction.

### 9b-xii. A run WAITING for the write slot did not keep its place in the order (fixed)

**The symptom was `BatchTests.TestBatchSent` failing `WRONGTYPE`, rarely, on both cores** - and passing in
isolation every time, which is what kept it looking like noise. The shape:

    _ = db.KeyDeleteAsync(key);
    _ = db.StringSetAsync(key, "batch-sent");     // key is now a STRING
    var batch = db.CreateBatch();                 // DEL, then SADD x3
    batch.Execute();
    var result = db.SetMembersAsync(key);         // must run AFTER the batch

`WRONGTYPE` means the `SMEMBERS` reached the server while the key was still that string: it overtook the
batch that was issued before it.

**Owning the write slot has always meant "ahead of everything issued since I took it". WAITING for it
meant nothing at all**, and that was the hole. Two halves, both now closed:

- `PrepareRunAsync` captured the backlog as its *earlier* set when it WON the slot. Everything queued
  while it waited was therefore counted as earlier and written IN FRONT of the run - the opposite of the
  truth, since those commands were issued after it. It captures when it ASKS now.
- `ReleaseWrites` drained the backlog before handing the slot to a waiter, so the OUTGOING holder wrote
  those commands, before the waiting run existed on the wire at all. It hands on first when somebody is
  waiting; the waiter drains what accumulated, after its own run, when it releases.

Needs a concurrent slot holder to show at all, which is why it was load-dependent: a batch issued while
another run held the slot, with one more command issued in between.

`RespOperationBatchTests.ARunWaitingForTheSlotStillPrecedesWhatFollowsIt` pins the ORDER rather than the
symptom, and was checked both ways - it fails deterministically with either half of the fix removed, and
passes fourteen consecutive runs with both in.

### 9b-xiii. OPEN: a shared RESP3 connection wedges, rarely, and everything queues behind it

**Seen twice, both times as a cascade rather than a failure.** Once as eleven failures with 143-second
waits (9b-vi's `EnforcesTimeouts` half explained why the cascade was so wide, and fixing that did not
address the stall itself), and once as thirty-three - every one of them a `RedisTimeoutException` on a
RESP3 connection reporting **`qs: 6094`**: six thousand commands sent and awaiting a reply. Neither run
reproduced; the same build gave 4 and 3 failures on the next two attempts, and the default suite is
unaffected in both cases.

`qs` that size means replies stopped being matched to the queue at all - a stalled read loop, or a
desynchronised one. What is known:

- It is the SHARED fixture connection, and RESP3 in both sightings.
- It is not specific to the change in flight when it appeared. The second sighting followed the `INFO`
  move; `INFO` was then verified separately (`ConfigTests.GetInfo` asserts the section grouping and the
  `CPU` key under both protocols, and passes), and two further full runs of the same build were clean.
- The visible failures are always *other* tests - whatever was queued behind it - so the test that
  wedged it is not identifiable from the output.

**One piece of that instrumentation is now permanent**, and it was a parity gap anyway: a timeout from
this core reports what MOVED on the connection while the command waited -
`outbound=0KiB, inbound=0KiB, 1954ms elapsed, timeout is 1000ms`, worded as the shipped core words it.
That single pair of numbers separates the two candidate faults: `inbound=0KiB` says the socket delivered
nothing, and a large `inbound` says bytes arrived and none of them were matched - a stalled read against
a desynchronised one. The counters come from the stamps the connection puts on the operation when it
takes it, which is what `OnEnqueued` exists for, so nothing is paid until something times out.

`MaintenanceRelaxationEvidenceTests.ATimeoutInsideAWindowSaysWhichEventCausedIt` pins it, and was
checked both ways.

Still wanted for a third sighting: the connection's own state at the moment `PendingCount` starts growing
without bound, and whether `Drain` has stopped being called or has stopped consuming.

### 9c. What this buys beyond tidiness

A real share of the remaining engine failures are dual-core artefacts that would evaporate rather
than need fixing - `BacklogTests` (4) and two `ReconnectRetryPolicyUnitTests` are positively
identified, and the connection-lifecycle groups are suspected. Fixing those individually is partly
wasted work; the cache, cluster-detection, cross-slot and transaction-abort bugs found alongside them
were genuine and worth having regardless.

