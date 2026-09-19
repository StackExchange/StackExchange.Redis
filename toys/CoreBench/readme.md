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

**The contention hypothesis holds.** The gap is small single-threaded and opens with worker count, which
is the predicted shape — the old core serialises every argument of every command inside its write lock
(`message.WriteTo` runs there), while the new core renders into the caller's own buffer before the
executor is reached, leaving only stamp/enqueue/memcpy in the critical section.

**It also found a bug that no test could have.** Pooling was designed in phase 1 and never engaged:
`IsRecyclable` was false at every single `GetResult`, so an executor's pool took **0 hits in 56,642
sends**. The cause was ordering — `_asyncCore.SetResult` runs the awaiting continuation *inline*, so the
awaiter consumed the result and reset the instance before the completing code had recorded how the life
ended. Results were correct throughout; the only symptom was allocation. Fixing it took `exec` from
528.8 to 320.8 bytes/op.

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
