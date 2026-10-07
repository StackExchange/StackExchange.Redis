# Where a command goes

**Status: describes what is built, as of 2026-09-29.** Unlike `message-core-replacement.md`, which is a
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
RespDatabaseContext            the surface: groups of extension methods
        |
        |  render              interpolated string -> RespRequestBuilder -> RespRequestFrame
        v
RespExecutor.SendAsync         cache probe, then hand the frame to the executor
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
RespPayloadOperation           one command in flight; pooled
        |
        v
RespClientConnection           : RespConnection (RESPite) - pending queue, write lock, read loop
        |
        v
DuplexTransport                socket, TLS, tunnel
```

The two differences that follow from this:

**Rendering happens at the call site, not at write time.** `$"{RedisCommand.GET}{key}"` is an
interpolated string handler that writes RESP bytes as it is evaluated, so by the time anything is
routed the command is already a buffer. There is no object that "knows how to write itself later", which
is why the operation carries a `Slot`, a `Database` and a `Command` - the request it came from is bytes,
and the caller's frame is released.

**Parsing happens in a handler over a reader, not in a processor bound to a message.**
`IRespHandler<T>.Parse(ref RespReader)` is handed the reply frame and produces the result. The handler
is chosen at the call site and knows nothing about the command it is parsing for.

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

4. **Detach.** The frame is detached - ownership of the buffer moves to the send - and becomes a
   `RespRequest`.

5. **Route.** `RespMultiplexerExecutor.Route` reads the request's slot. Not a cluster, or no key: any
   acceptable endpoint. A slot: `RespTopology`'s own map names the owner, roles decide primary versus
   replica, and selectability decides whether that endpoint may be used at all. The answer is another
   `RespExecutorBase`.

6. **Database.** If the command's database is not the one the connection selected, a
   `RespDatabaseExecutor` view wraps the endpoint executor and a `SELECT` is injected as a preamble at
   write time - inside the connection's write lock, so nothing can slip between them.

7. **Operation.** `RespEndpointExecutor.Dispatch` rents a `RespPayloadOperation` from a pool, attaches
   the request bytes, and stamps what will be needed after the bytes are gone: slot, database, command,
   the profiling record, and who observes the outcome.

8. **Write, or wait.** If there is a live connection and no one holds the write slot, the operation goes
   straight to `RespConnection.Send`: under the write lock it reserves the request, enqueues itself on
   the pending queue, and the bytes go to the transport. Otherwise it joins the **backlog** and the
   executor starts connecting.

9. **Reply.** The transport hands received bytes to `RespConnection.OnReceived`, which appends to a
   reference-counted inbound buffer and `Drain`s it: `RespScanState` finds one complete frame at a time.
   Out-of-band frames (pushes) are offered to the push dispatcher; otherwise the frame is matched to the
   head of the pending queue - **strictly in order, which is what makes the pending queue a queue**.

10. **Hand-off or complete.** The frame is offered to the redirect handler first, because a `-MOVED` is
    an instruction rather than an answer. If nobody takes it, `TrySetResult` calls
    `RespPayloadOperation.ParseFrame`, which turns an error reply into a `RedisServerException` and
    otherwise *retains* the receive buffer rather than copying out of it.

11. **Parse.** The awaiting `SendAsync` gets a `RespPayload`, runs the handler over it, releases the
    payload, and returns the typed result.

## Where the old responsibilities went

| Old | New | Notes |
| --- | --- | --- |
| `Message` (command + render) | interpolated render, `RespRequestFrame` | rendered at the call site, not at write time |
| `ResultProcessor<T>` | `IRespHandler<T>` | handed a reader over the reply frame |
| `ServerSelectionStrategy` | `RespTopology` + `RespMultiplexerExecutor` | own slot map, roles, selectability |
| `ServerEndPoint` | `RespTopology`, `RespEndpointExecutor` | partly; script beliefs and `IServer` still live on the old type |
| `PhysicalBridge` | `RespEndpointExecutor` | backlog, write slot, reconnect, circuit breaker |
| `PhysicalConnection` | `RespClientConnection` / `RespConnection` | pending queue, read loop |
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
- **Timeouts.** Nothing arrives to tell you a reply is late, so this is the one thing that happens on a
  clock rather than in response to something. The multiplexer heartbeat calls
  `RespConnectionManager.OnHeartbeat`, which asks each endpoint to expire its backlog and then sweeps the written
  queue (`RespConnection.ExpirePending`), against the configured timeout raised by any maintenance
  window. A swept operation is completed but **stays in the pending queue**, because replies are matched
  to that queue positionally. The fault itself is built by `ExceptionFactory`, the same place the shipped
  core builds one, via `IFaultSubject` - which is why a timeout from this core carries the same `qs`,
  `in`, `last-in` and thread-pool tail that callers have been reading for years.

## What is still borrowed

Under the engine flag both cores exist, and the new one still asks the old for a few things per command
or per decision: keyless routing (`ServerSelectionStrategy`), the feature probe's fallback, the
subscription registry, and `ServerEndPoint` for script beliefs and profiling context. Each of those is
tracked in `message-core-replacement.md` section 9d, which is also where the plan for removing them
lives. `IServer` still builds its context over the Message shim entirely.
