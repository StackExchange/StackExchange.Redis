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
5. **Batch, then transaction**, as decorators. `RespBatchExecutor` is replaced by the `BatchConnection`
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
