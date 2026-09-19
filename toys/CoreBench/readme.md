# CoreBench

The old core against the new one, on a real server. Not a general benchmarking tool — `OpBench` is that.
This exists to answer two specific questions from the queue, and to keep answering them as the new core
changes.

```bash
dotnet run -c Release --project toys/CoreBench -- --work incr --seconds 3 --workers 1,4,16,64
```

`REDIS_HOST` / `REDIS_PORT` override the target (default `127.0.0.1:6379`).

## Arms

| arm | what it is |
|---|---|
| `old` | `ConnectionMultiplexer` → `IDatabase` → `StringIncrementAsync`. The shipped path. |
| `new` | context surface → `RespConnectionExecutor` → `RespConnection` → socket. No `Message`. |
| `newcache` | as `new`, plus `RespClientCache`, on a cacheable `GET`. |
| `exec` | subtractive: the executor alone, request rendered up front, reply released by hand. |
| `direct` | subtractive: an operation parsing straight to `long`, no `RespPayload` at all. |
| `render` | subtractive: the interpolated writer alone, no IO. |
| `noop` | the harness floor — an async lambda awaiting nothing. Measured at **0 bytes/op**, so every other number is real. |

One key per worker, deliberately: a shared counter serialises on the *server*, and the question here is
what the *client* does under concurrency.

## What it found

### The four-way comparison

Counter `INCR`, one key per worker, 3s per measurement, net10.0, server GC, local server.

| arm | 1 | 4 | 16 | 64 | bytes/op |
|---|---|---|---|---|---|
| **3.3.0** (shipped package) | 27,929 | 79,896 | 205,505 | 426,699 | 361-383 |
| **old** (this branch) | 26,942 | 82,188 | 204,441 | 425,607 | 361-375 |
| **new** (this branch) | 27,626 | 86,635 | 224,965 | **553,822** | 496.1 |
| **newcache** (cacheable `GET`) | 7,667,317 | — | 79,032,968 | 89,931,589 | **0.0** |

**3.3.0 and this branch's old path are identical within noise**, at every worker count and in
allocation. That is the control the whole comparison rests on: the new-vs-old numbers below are a valid
proxy for new-vs-shipped, because nothing about the old path has moved.

**The contention hypothesis holds**: +2.5% at one worker, +5.4% at four, +10% at sixteen, **+30% at
sixty-four**. The predicted shape, from the mechanism written down in advance - the old core serialises
every argument of every command inside its write lock (`message.WriteTo` runs there), while the new core
renders into the caller's own buffer before the executor is reached, leaving stamp/enqueue/memcpy in the
critical section.

### Where the bytes go

Subtractive, one worker, each row a complete operation:

| arm | bytes/op | what the step adds |
|---|---|---|
| `noop` | 0.0 | the harness is free; every number below is real |
| `yield` | 96.0 | **the caller's own async state machine**, for an await that genuinely suspends |
| `render` | 48.0 | the per-request lease |
| `direct` | 249.1 | +105 connection and operation, including the thread-pool work item |
| `exec` | 321.1 | **+72** `RespPayload` + `RefCountedBuffer` |
| `new` | 496.9 | **+176** the context surface's own async state machine |
| `newcache` | 0.0 | the same surface, when nothing suspends |

So of the new core's ~497 bytes:

- **~96 is the caller's**, not ours - any suspending `await` boxes its state machine, and the old core
  pays it too.
- **~176 is the surface's async state machine** on the suspending path. `newcache` at 0.0 proves this
  is *only* the suspending path: the same handler, parse and `ValueTask` machinery allocates nothing
  when the executor completes synchronously. This is the largest single item and the most promising:
  a pooled async method builder, or a shape that avoids an async frame when the executor completes
  inline.
- **~72 is `RespPayload.Create` copying the reply**, which that method's own comment already calls
  scaffolding - the real answer is sharing the receive buffer's lease rather than copying out of it.
- **~48 is the per-request lease**, which is poolable.
- **~105 is the connection and operation path**, which includes the thread-pool work item that exists
  *because* continuations are queued rather than run inline. That one is not a bug to fix; see below.

Creep, as opposed to volume, already favours the new core: its allocation is flat at 496.1 and dies in
gen0 (0 gen1, 0 gen2 at every worker count), where the old core varies between 322 and 383 and promotes
- 29 gen1 and a gen2 at 64 workers.

## Reading the numbers honestly

**`newcache` is not a round trip.** It is a cache hit, so it measures the probe and nothing else — worth
having (it shows the surface, handler and `ValueTask` path are genuinely allocation-free, at 0.0
bytes/op) but it is not comparable to the other arms.

**Do not "optimise" by running continuations inline.** Turning off `RunContinuationsAsynchronously` makes
this benchmark ~32% faster at 64 workers. That is [thread theft](../../docs/ThreadTheft.md): the reader
loop is hijacked to run application logic, and every other reply on that connection waits behind it. This
benchmark's continuation is a loop counter, so the theft is free here and ruinous in production. The
existing core has used `RunContinuationsAsynchronously` for exactly this reason since long before any of
this; the new core matches it.

**The new arm still allocates more per operation than the old**, and that is not yet explained away.
Known contributors: the per-request lease (~48 B/op, see the `render` arm), and `RespPayload.Create`
copying the reply (~96 B/op, see `direct` vs `exec`) — which that method's own comment already calls
scaffolding, since the real answer is to share the receive buffer's lease rather than copy out of it.
What the new core does win on is **creep**: its allocation is flat and dies in gen0, where the old core's
varies and promotes to gen1/gen2.

**Not yet in the new arm**, and all of it has to be there before this is a fair fight: handshake
(`HELLO`/`AUTH`/`SELECT`), reconnect, the backlog, and the profiling hooks.
