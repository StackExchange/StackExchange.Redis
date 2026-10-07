# Where a command goes

**Status: describes what is built, as of 2026-10-07.** Unlike `message-core-replacement.md`, which is a
plan, this is a map of the code as it stands - so where the two disagree, this one is the one that is
wrong, and should be fixed.

A map of the execution chain on the RESP context surface: what happens between `db.StringGet(key)` and
the bytes on the socket, and back. Written because the old chain was three types deep and easy to hold
in your head, and this one is a composition - which is more flexible and much less obvious.

## The old chain, for contrast

```
IDatabase -> Message -+-> ConnectionMultiplexer.SelectServer -> ServerEndPoint
                      |                                            |
                      +-> ResultProcessor                     PhysicalBridge   (queue, backlog, write lock)
                                                                   |
                                                            PhysicalConnection (socket, framing, read loop)
```

Two things about it matter for the comparison. **A `Message` is a command that has not been written
yet** - it carries the command, the key, the flags, and knows how to render itself when a connection
finally asks; formatting therefore happens *in* the connection, at write time. And **a
`ResultProcessor<T>` is bound to that message**, so parsing happens there too, on the way back.
`ServerEndPoint`/`PhysicalBridge`/`PhysicalConnection` are one fixed chain per server: routing is a
lookup that picks a server, and everything after that is the same three objects every time.

## The new chain

```
IDatabase / RedisDatabase      the shipped surface: a shim over the context - see "The IDatabase shim"
        |
RespDatabaseContext            the surface: groups of extension methods
        |
        |  render              interpolated string -> RespRequestBuilder -> RespRequestFrame
        v
RespExecutor.SendAsync         cache probe, then hand the frame (borrowed or owned) to the executor
        |
        v
RespExecutorBase               a COMPOSITION, not a fixed chain:
  RespMultiplexerExecutor        routes - slot, role, selectability
  RespDatabaseExecutor           names a database over a shared connection
  RespEndpointExecutor           owns one endpoint's connection: backlog, write slot, reconnect
  (RespTransactionExecutor, RespOperationBatchExecutor, RespGroupExecutor, RespBatchExecutor,
   RespConnectionExecutor wrap or replace parts of this)
        |
        v
RespPayloadOperation<T>        one command in flight, AND the awaitable for its parsed result; pooled
        |
        v
RespClientConnection           : RespConnection (RESPite) - pending queue, write lock, close notice
        |
        v
DuplexTransport                socket, TLS, tunnel; the read loop (a filler and a parser by default)
```

The two differences that follow from this:

**Rendering happens at the call site, not at write time.** `$"{RedisCommand.GET}{key}"` is an
interpolated string handler that writes RESP bytes as it is evaluated, so by the time anything is
routed the command is already a buffer. There is no object that "knows how to write itself later", which
is why the operation carries a `Slot`, a `Database` and a `Command` - the request it came from is bytes,
and the caller's frame is released (for a small request, before the send even returns - see step 4).

**Parsing happens in a handler over a reader, not in a processor bound to a message.**
`IRespHandler<T>.Parse(ref RespReader)` is handed the reply frame and produces the result. The handler
is chosen at the call site and knows nothing about the command it is parsing for. It runs when the
caller *takes* the result - `RespPayloadOperation<T>.GetResult` - not on the read loop.

## A worked trace: `await db.StringGetAsync(key)`

1. **Surface.** `Strings.Methods.GetAsync` - an extension method on the `RespStrings` group struct -
   calls `strings.Context.SendAsync<RedisValue>($"{RedisCommand.GET}{key}", flags, ...)`.

2. **Render.** The interpolated string is a `RespRequestBuilder`: each hole writes itself into a pooled
   buffer. Keys are prefixed (keyspace isolation), folded into the cluster slot, and marked for cache
   invalidation as they go. `Complete()` yields a `RespRequestFrame` - the bytes, plus the slot and the
   command.

3. **Cache.** `RespExecutor.SendAsync` probes the context's `RespClientCache` if there is one: a hit
   returns without sending; an in-flight identical request is awaited rather than duplicated; otherwise
   a fill is opened so the reply populates the cache on the way back.

4. **Borrow or detach.** An executor that copies the bytes during the call (`CopiesRequestOnSend` - the
   endpoint executor, and the database view over it) is handed a *borrowed* view of the frame
   (`AsLookupKey`), and the frame's array goes back to the pool when the send returns, on the thread that
   rented it. Anything else - a batch, a transaction, the raw-payload path - gets the frame *detached*:
   ownership of the buffer moves to the send, as a leased `RespRequest`. A wrapper that cannot copy
   synchronously turns a borrowed request into an owned one (`ToOwned`) before it holds on to it.

5. **Route.** `RespMultiplexerExecutor.Route` reads the request's slot. Not a cluster, or no key: any
   acceptable endpoint. A slot: `RespTopology`'s own map names the owner, roles decide primary versus
   replica, and selectability decides whether that endpoint may be used at all. The answer is another
   `RespExecutorBase`.

6. **Database.** If the command's database is not the one the connection selected, a
   `RespDatabaseExecutor` view wraps the endpoint executor and a `SELECT` is injected as a preamble at
   write time - inside the connection's write lock, so nothing can slip between them.

7. **Operation.** `RespEndpointExecutor.SendTypedAsync` rents a `RespPayloadOperation<T>` from that result
   type's pool, carrying the handler. `Attach` copies a request of up to 512 bytes into a buffer the
   operation keeps across its pooled lives (so a typical command allocates nothing for its bytes, and
   nothing of the caller's is retained); a larger one is shared, or copied into a rented array if it was
   borrowed. The operation is stamped with what will be needed after the bytes are gone: slot, database,
   command, the profiling record, and who observes the outcome. The task handed back is
   `new ValueTask<T>(operation, token)` - **one object per send**, where an `async` parse-after-await would
   have boxed a state machine as well. A command whose only result is success (`SendVoidAsync`) uses the
   same operation through its non-generic `IValueTaskSource`.

8. **Write, or wait.** If there is a live connection and no one holds the write slot, the operation goes
   straight to `RespConnection.Send`: under the write lock it enqueues itself on the pending queue and
   copies its bytes into the transport's buffered writer (`DuplexTransport.Write`); the flush is lock-free
   when there is nothing to do. Otherwise it joins the **backlog** and the executor starts connecting.
   Under the experimental `CombineWrites` flag a send that finds the write lock busy leaves its operation
   in an inbox for the lock holder to write, instead of waiting on the lock.

9. **Reply.** By default the transport reads with two loops: a *filler* that reads the socket into a small
   ring of buffers, and a *parser* that hands each filled buffer to `RespConnection.OnReceived` - so the
   next read is already under way while the last is parsed. They hand buffers back and forth through two
   `SingleWaiterSemaphore`s, which wait without allocating (`SingleReadLoop` restores the single loop).
   `OnReceived` appends to a reference-counted inbound buffer and `Drain`s it: `RespScanState` finds one complete frame at a time.
   Out-of-band frames (pushes) are offered to the push dispatcher; otherwise the frame is matched to the
   head of the pending queue - **strictly in order, which is what makes the pending queue a queue**.

10. **Hand-off or complete.** The frame is offered to the redirect handler first, because a `-MOVED` is
    an instruction rather than an answer. If nobody takes it, `TrySetResult` calls
    `RespPayloadOperation.ParseFrame`, which turns an error reply into a `RedisServerException` and
    otherwise *retains* the receive buffer rather than copying out of it. The operation completes with
    `RunContinuationsAsynchronously`, so the caller's continuation goes to the thread pool and never runs
    on the parser - the exception is a blocked synchronous caller that has claimed it (`SyncPump`), whose
    continuation runs on that caller's own, otherwise idle, thread.

11. **Parse.** The caller's `await` takes the result: `RespPayloadOperation<T>.GetResult` reads the
    handler, takes the payload (which recycles the operation into its pool), runs the handler over it,
    releases the payload, and translates an authentication fault into the multiplexer's own. Parsing on
    the caller's thread rather than the parser's keeps the read loop doing only framing.

## Where the old responsibilities went

| Old | New | Notes |
| --- | --- | --- |
| `Message` (command + render) | interpolated render, `RespRequestFrame` | rendered at the call site, not at write time |
| `ResultProcessor<T>` | `IRespHandler<T>` | handed a reader over the reply frame |
| `ServerSelectionStrategy` | `RespTopology` + `RespMultiplexerExecutor` | own slot map, roles, selectability |
| `ServerEndPoint` | `RespTopology`, `RespEndpointExecutor` | partly; script beliefs and `IServer` still live on the old type |
| `PhysicalBridge` | `RespEndpointExecutor` | backlog, write slot, reconnect, circuit breaker |
| `PhysicalConnection` | `RespClientConnection` / `RespConnection` | pending queue, write lock, close notice |
| `TaskResultBox` / `ResultBox<T>` | `RespPayloadOperation<T>` | pooled; the operation is the awaitable |
| framing/parsing in the connection | `RespFrameWriter`, `RespScanState`, `RespReader` | RESPite owns the wire |

## The paths that are not the straight line

- **Preambles.** A `SELECT` or a `SCRIPT LOAD` that must be adjacent to its command is written as a
  *pair* inside one write lock, with a gate consulted at write time to decide whether it is still
  needed. When there is no connection to pair on, they are sent in sequence instead - see
  `SendPreambleAsync`, which keeps the preamble out of profiling and still tells the gate.
- **Batches and transactions.** `RespOperationBatchExecutor` and `RespTransactionExecutor` accumulate
  operations and write them as one run, taking the write slot first so nothing interleaves. A
  transaction additionally expects `+QUEUED` receipts and distributes the positional `EXEC` array.
- **Redirects.** Handled on the IO loop, in reply order, so commands redirected together keep their
  order; the map is updated for `MOVED` and not for `ASK`.
- **Backlog.** Commands wait here when there is no connection. They survive a failed connect (subject to
  `BacklogPolicy`), are swept against their timeout, and drain in arrival order.
- **Pub/sub.** A subscription connection is a second `RespEndpointExecutor` per endpoint, needed because
  RESP2 delivers messages as ordinary arrays.
- **A connection the server closes.** The transport's end-of-stream closes the `RespConnection`, which
  faults what it had pending (`RedisConnectionException`, `SocketClosed`) and raises `Closed`. The
  endpoint executor subscribes before publishing the connection - holding itself weakly, so the delegate
  never keeps a multiplexer alive - and its `OnConnectionClosed` drops the connection and starts the
  reconnect, raising `ConnectionFailed` and then `ConnectionRestored` as v3 did; Active-Active failover
  listens to exactly those. The heartbeat also notices a closed connection that is still published, as a
  net under the notice.
- **Timeouts.** Nothing arrives to tell you a reply is late, so this is the one thing that happens on a
  clock rather than in response to something. The multiplexer heartbeat calls
  `RespConnectionManager.OnHeartbeat`, which asks each endpoint to expire its backlog and then sweeps the written
  queue (`RespConnection.ExpirePending`), against the configured timeout raised by any maintenance
  window. A swept operation is completed but **stays in the pending queue**, because replies are matched
  to that queue positionally. The fault itself is built by `ExceptionFactory`, the same place the shipped
  core builds one, via `IFaultSubject` - which is why a timeout from this core carries the same `qs`,
  `in`, `last-in` and thread-pool tail that callers have been reading for years. The heartbeat also
  declares a connection dead when commands time out and nothing at all has arrived for four timeouts.
  RESPite's own `OperationBackstop` - a cancellation clock on every operation - is **off by default**:
  the heartbeat is the timeout, as it was in v3, and the backstop's per-operation registration was a
  measurable cost. The test suite turns it on, as a net for a bug that leaves an operation unanswered.

## The IDatabase shim

`IDatabase`, `IBatch` and `ITransaction` are not a second implementation: `RedisDatabase` (split across
`Database/RedisDatabase.*.cs`) holds a `RespDatabaseContext` and forwards every method to it.

- **Asynchronous.** `StringGetAsync(key, flags)` is `_inner.Strings.GetAsync(key, flags).AsTask(AsyncState, flags)`.
  `TaskBridge.AsTask` turns the context's `ValueTask<T>` into the `Task<T>` the interface promises, and is
  the only place that cost lives:
  - Completed already (a client-side cache hit, say): `Task.FromResult`, or a completion source born with
    the async state.
  - Pending, on our own operation, without async state (the common case): the operation's own promise task.
    The endpoint marks the operation it just handed back (`NoteDispatched`, a thread-static), and
    `TryTakeDispatched` recovers it - checked by `ValueTask` equality, so a stale note simply fails to match.
    `RespPayloadOperation<T>.AsTask` then makes a bare promise task with `AsyncTaskMethodBuilder<T>` (one
    object, no completion source) and completes it from a static callback on the operation: one `Task`
    plus the thread-pool work item, per command.
  - Pending, with async state: `SourceBridge` - a `TaskCompletionSource<T>` constructed with the state,
    registered on the operation the same way. `Task.AsyncState` can only be set at construction, which is
    why state costs an object.
  - Pending, anything else (a decorator's task, a composite command): `Bridge`, the same over the
    `ValueTask`'s awaiter.

  Every bridge marks a fault observed as it sets it, so a dropped task never raises
  `TaskScheduler.UnobservedTaskException` - v3's behaviour, which `ValueTask.AsTask()` would not keep - and
  maps an `OperationCanceledException` to a cancelled task, which is how a transaction whose condition failed
  completes its queued commands. Fire-and-forget never carries state.
- **Synchronous.** `StringGet(key, flags)` is `Wait(SyncCall.Begin(), _inner.Strings.GetAsync(key, flags))`.
  The `SyncPump` captures the operations the call rents, and the wait has their continuations run on the
  blocked thread rather than the thread pool, so a saturated pool cannot strand a reply that has arrived.
- **Batches and transactions** are `RespOperationBatchExecutor` and `RespTransactionExecutor` contexts behind
  the same shim; `Execute` sends what was queued, and an `IBatch`/`ITransaction` can be executed again.

The context surface returns `ValueTask` and needs none of the bridge; measured against it, the shim costs
roughly a hundred bytes per command (the `Task` and the work item) and is the main reason `IDatabase` trails
the context API on small commands.

## What is still borrowed

The old core - `Message`, `ResultProcessor`, `PhysicalBridge`, `PhysicalConnection` - is gone; the "old
chain" above is history, kept for the comparison. A few of its neighbours remain: `ServerSelectionStrategy`
still answers keyless routing for `RespConnectionManager`, and `ServerEndPoint` still holds per-server state
the new core reads (script beliefs, maintenance, profiling context). `IServer` (`RedisServer`) is built over
the context surface, like `IDatabase`.
