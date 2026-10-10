# v4 ledger

The current list of what is pending on `v4` and what is waiting on a decision. **Keep it short:** when an
item closes, delete it here and let the commit message hold the story. History and reasoning live in
`v4-alpha-plan.md` (how the old core was retired) and `message-core-replacement.md` (why).

Last updated: 2026-10-09.

## Decisions waiting on Marc

Each has a default the work proceeds on until answered.

| # | question | default meanwhile |
|---|---|---|
| D1 | **#3251's intent (split socket fill from parsing) on v4?** v4 still parses inline on the read loop, async and sync. The throwaway port (`marc/v4-filler-experiment`) measured +39% `get-1k-conc64`, and with a 200-spin park + 64 KiB buffers led every concurrent scenario; cost ~5% on `incr-conc64` and 3-5% sequential. Options: port before the alpha / port as opt-in / after the alpha. | not ported |
| D2 | **Linux: the `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS` question.** Once a socket has done any async op (connect, TLS), the runtime routes readiness through the thread pool - even for a DedicatedThreads reader. The env var removes it (measured: 500 sync calls, 0 pool items), but it is process-wide and read at runtime start, so the library cannot set it for the caller. Options: (a) document only; (b) connect + TLS synchronously for dedicated connections, so the socket never goes async; (c) both. v3 behaved the same. | **(b), 2026-10-09, approved and pushed (dbb38f88)**: dedicated connections connect and run TLS synchronously (`RespTransportFactory.RunBlocking`), so the socket never registers; the env var is no longer needed. Docs updated. |
| D3 | **Alpha cadence.** 4.0.69-alpha was cut 2026-10-08 (GitHub Release on `v4` runs `release.yml`). Next candidate is the NRedisStack-port build: `Blocking()`, single group accessors, IDatabase sync on Blocking, DedicatedThreads sync connect. | cut when Marc says; CI green first |
| D4 | **Post the v4-port findings to PR #3251?** Drafted (spin-before-park +2.6% on `incr-conc64` in #3251's own reader; read-size hint is a hypothesis; drain-per-wake does not pay). Outward-facing. | not posted |
| D5 | **SER014 batch/transaction, unsettled parts:** conditions borrow the shipped `Condition` type (new spelling later?); whether `RespBatch`/`RespTransaction` stay structs. | as shipped in 2ca83a9d |

Closed by the 3.4.0 merge (2026-10-09), because `main` decided them: **D6** - duplicate event ids renumbered on `main`
(#3266; 124-127 are now main's shipped ids, and v4's colliding v4-only events moved to 128-131); **D7** - RESP3
pub/sub gets a dedicated connection by default, sharing is opt-in via `SharedSubscriptionConnection` (#3264).

## Pending work (no decision needed)

- **PAUSED 2026-10-10: cache hit-path performance - waiting on Marc's RespFest re-run against 4.0.104.** Shipped
  today: c5370268 (the multiplexer executor answered `Transactional`/`Accumulates` by routing - server selection
  plus the endpoint lock - on every cached read and blocking send; the ~30% RespFest regression, and the lock made
  throughput FALL past 6 threads), 04ef29d9 (cache entries own GC arrays: no refcount CAS per hit; 1 hot key at
  24 threads 11.5 -> 140 M/s), 0023c9e6 (`CacheClock`: `TickCount64` for entry ages, `Stopwatch` kept for the
  grace window). Single-thread hit 134 -> 114ns. **Next, once the league confirms:** the interpolated builder -
  ~26ns over a 4ns hand-written floor, plus 10-15ns on every group method because one level deeper the JIT's
  inline budget runs out and `RespRequestBuilder`'s key path (`CommitBulk`, `CountArguments`, `FoldSlot`) stays
  as calls (6548bffe). Ideas: a small always-inlined fast path for command + one key; hot/cold split of the key
  append. Tools: `CacheHitSendBenchmarks` (run with `--inProcess` - the `.claude/worktrees` copies break BDN's
  out-of-process build), `-- cache-scaling 3 127.0.0.1:6379` (real multiplexer rows), `DOTNET_JitDisasm`.
  Fallback if fill-time copies hurt the churn league: global striped-epoch reclamation instead of GC arrays.

- **CI: `GetServerTestsCluster.GetServerByKeyMemoization` (RESP3) fails on Windows net481** - 2 of the first 3
  `v4` runs, each after a cluster connect that waited the full 20s at the very start of the net481 run (three
  cluster connects stalled together). Suspected lost wake-up, fixed speculatively in d45eadf7 (announce
  "connected" only after the connection is published); not reproduced locally. Tests now print the connection
  log, with thread-pool stats, for any connect that uses half its timeout - read it if it recurs.
- **Absorb `main` by merging**, little and often; resolve each conflict by re-expressing the change in v4
  terms and say how in the merge message. Drift check: `git rev-list --count v4..origin/main`. The final
  landing on `main` must be a real merge, never a squash.
- **Two-core leftovers:**
  - `Subscription._subscribed` / `IsSubscribed` / `AnySubscribed` now mean only "something
    subscribed this"; collapsing them changes the never-sent case - look, don't delete.
  - ~30 `ConnectionsIfCreated?.` null-tolerances; `ServerEndPoint.IsConnecting => false`.
  - Topology flows both ways between `RespTopology` and `ServerSelectionStrategy` ("phase D").
- **Subscription re-aim waits for its in-flight send to fail** (instant on Linux, ~2s on Windows, a connect
  timeout for a dropped SYN). Fixing it means de-duplicating a subscription that lands on two nodes. Not urgent.
- **The "~5s with nothing inbound" stall: root cause found and fixed** (the commit after 7aa7f818); CI to confirm.
  A full dump of a stuck run showed the connection's read loop mid-execution on a thread running TEST code: the
  reader completed an operation INLINE, the continuation ran up into a test's `await`, and the test then made a
  synchronous call whose reply only that reader could deliver. Inline completion leaked from the two deliberate
  inline paths (the sync pump's sink, `TrySetCanceledInline`) through `ManualResetValueTaskSourceCore`'s
  `RunContinuationsAsynchronously`, which survives a reset: across lives (the inline continuation recycled the
  instance before the `finally` restored it) and within one (TrySetCanceledInline flipped it before claiming).
  12/12 local full runs clean afterwards, against ~1 in 5-7 stalling before; no 5s timeouts at all.
- **Fixed 2026-10-09:** `ReadOnlyLeaseTests.PrimitiveElementsAreNotClearedOnReturn` (Windows CI twice) asserted
  on whatever the process-shared `ArrayPool` handed back; it now asserts only when it is the same array.
- **Watch: one "nothing inbound" timeout on CI after the stall fix** (2026-10-09, Ubuntu, PR run of 804da1c9; the
  push run of the same commit passed): `RespCacheChurnTests.ChurnDoesNotDisableTheCache("scoped", 16 callers)`,
  `Timeout awaiting response (outbound=12KiB, inbound=0KiB, 5915ms)`, 16 SETs in flight. Purely async test, so not
  the old sync-on-reader shape. Locally: 25 runs of the class and 5 net10 full suites, all clean, no timeouts. If it
  recurs, take a dump mid-stall (the old fix was found that way) rather than retrying.
- **Watch: `BacklogTests.TotalOutstandingIncludesBacklogQueue` is intermittent, and was before today** (2026-10-09):
  three pings queued after `SimulateConnectionFailure` with `AllowConnect = false` sometimes show
  `PendingUnsentItems == 0`. Looked like a regression (2 of 3 alone at the head), but a bisect plus re-runs put the
  morning's 286170a8 at 1 of 8 as well, the same rate as the head - so pre-existing and environment-sensitive.
  Not investigated further; first suspect is the pings completing (or faulting) before the counters are read.
- **Known flakes** (beyond the stall above): `TouchIdleTime` (6381), `RespAggregateTiming`, `RespAggregateProtoTests.ADeferredWalkNeedsNoStorageAtAll` (net8, full suite only; per-thread allocation measure); `ClusterTopologyUnitTests.SlotLessNodesAreKnownButNotConnected` (net8 full suite once, 2026-10-09; 3/3 alone);
  `RedisBatchTests.AWatchConflictIsDistinctFromAFailedCondition` timed out once (fake transport) on a
  heavily loaded machine, 20/20 since.
  Pub/sub under full-suite load, each once and each clean in isolation: `MultiGroupTests...PubSubOrderedRouted`
  and `...PubSubRouted` (RESP3), `PubSubKeyNotificationTestsCluster.KeyNotification_CanObserveSimple_ViaQueue`.
  `PubSubRouted` and `KeyNotification_CanObserveSimple_ViaQueue` have each now failed in two full runs (RESP2
  and RESP3), always clean in isolation and in the run after; the leading suspect if a pattern holds.
  (`RespHashImportProbeTests.AConnectionLocalPreambleIsTheScriptSeam...` kept recurring after that fix - CI
  once, local net8 twice - and the second cause was isolation: the probe ran on the SHARED multiplexer, so any
  concurrent test's batch or transaction holding the write slot sent the next pair sequential. Now
  `Create(shared: false)`, 2026-10-09. The first cause was a startup race in the TEST, also
  fixed: a pair sent while the post-connect drain holds the write slot goes sequential, which sends the
  PREPARE without asking the gate; the connection-local gate rightly does not claim on being told, so the
  next pair prepared again. Product behaviour is by design: at worst one redundant, idempotent PREPARE per
  field-set per connection, only in that window.)

## Backlog (after the alpha)

- **Client-side cache across a geo/active-active failover.** Today there is **no flush on switch, by design**
  (`MultiGroupDatabase` remarks): each member multiplexer has its own cache, resolved per command
  (`WithCacheResolver(() => TryGetActive()?.ClientCache)`), so a switch serves from the new member's cache and
  leaves the old one warm for switching back; and any member whose connection fails flushes its own cache
  (`ConnectionMultiplexer.OnConnectionFailed` -> `ClientCache.OnFlush`), which covers the usual failover cause.
  Covered only by a unit test (`RespGroupExecutorTests.TheCacheFollowsTheActiveMemberRatherThanBeingShared`);
  the fault-injector tier has no cache scenario. Gaps to settle:
  - **Read-your-writes across a failback.** While B is active, writes land on B; A's cached entries are only
    invalidated once replication carries those writes to A and A's server pushes the invalidation to A's
    tracking connection - so on switching back, A can serve pre-switch values for up to the replication lag.
    Options: flush the returning member's cache on a switch (the "nuke"), keep it warm under a max-age bound,
    or make it a policy (keep-warm vs flush-on-switch).
  - **A silent inactive member.** An inactive member's tracking connection that goes half-open never raises a
    failure, so nothing flushes; check the heartbeat covers inactive members, or flush on becoming active.
  - **A real test:** a fault-injector scenario with the cache on - write via B during failover, fail back,
    assert no stale read.

- **Alternative client-side-cache invalidation sources, and opt-in/out.** Today invalidation comes only from
  `CLIENT TRACKING` (`CacheTrackingMode`: Default -> BCAST, or per-key tracking) and the cache is on by default
  (`CacheOptions.Enabled = true`). Add policies for deployments where tracking is unavailable or unwanted
  (proxies, older servers, some managed offerings): **keyspace notifications** (`__keyspace@<db>__:*`, which
  needs `notify-keyspace-events` configured server-side and carries no RESP3 requirement) and **explicit
  pub/sub** (an application-defined invalidation channel the writer publishes to). Settle the opt-in/out story
  at the same time: whether on-by-default is right for 4.0, how a caller opts out per multiplexer and per
  command, and what the cache does when the chosen source is not available (refuse loudly, as RESP2 + cache
  does today, versus degrade to no caching).

- **Unobserved faults on the new surface** (the transitional `Task` surface was fixed in the commit that removed
  this item's first half: it now bridges through its own task and marks a fault observed). A `ValueTask` over the
  pooled operation has no `Task` and no finalizer, but a non-pooled `async ValueTask` that completes asynchronously
  is `Task`-backed - every one down-level, and 10 of 13 on net6+ - so a faulted one that the caller drops can still
  raise `UnobservedTaskException`. Dropping an un-awaited `ValueTask` is already misuse; low priority.

- **Batch-mode profile (2026-10-07), and two things it ruled out.** SADD, 50 callers x 100 per batch, perf with
  full stacks: v4 is round-trip bound (40-50% of samples are idle pool workers and the read loop's spin-before-park),
  command assembly into the transport (`RespConnection.Send`) is 0.9-1.5% - so **buffer packing at construction
  cannot pay** - and socket sends are ~2x v3's (v4 ~130-145 commands per send, v3 ~280; v4 reads in far larger
  chunks). Tried and reverted:
  - *Gathering several committed pages into one send* (copy up to 32 KB into a scratch buffer): sends did not fall
    (12,950 -> 16,365 per 2M ops). Pages are not the split; each send is ~one caller's flush, because the writer
    wakes and sends per flush. Only holding sends back would merge callers, which the coalescing experiment found
    does not raise throughput (round-trip bound).
  - *Batch/transaction attaching through the inline-copying `Attach(in RespRequest)`* instead of the span overload
    (which rents an ArrayPool array per command - a 100-deep batch overflows the pool's per-thread cache onto its
    locked per-core stacks, ~4% of samples): allocation ROSE ~80 B/op, because at depth most operations are new
    (pool of 128 vs ~5,000 in flight) and each new one allocates its own inline buffer. Throughput within noise.
  The real lever both point at is **operation pool size at depth**: 128 slots against thousands in flight means
  most operations (266 B, plus request bytes) are allocated fresh. A larger or depth-adaptive pool (watch the 8-probe
  `TryTake`) would let the inline buffer stick and make the ArrayPool change pay.

- **`MaintenanceOptInServerTests.SequenceIdsAdvanceAndCanBeRepeated` fails when its class runs alone** (5 of 5,
  with and without the 2026-10-07 pool change; usually passes in the full suite): `SendShardNotification` reaches 0
  connections where 1 opted in - the opt-in has not landed at the fake server when the notification is sent. A
  test-ordering dependence; look at what `OptInAsync` awaits.

- **Watch: net8.0 cluster tests timing out at 10 s under the full suite** (`DisabledCommandsStillConnectCluster`,
  `PubSubKeyNotificationTestsCluster.SubKeySpaceEvent_HandlesNewlineInField`,
  `ARetiringServerIsNotSelectableEvenThoughItStillOwnsItsSlots`). Seen in 2 of 7 full net8.0 runs after the per-thread
  operation cache (03bbb05a), 0 of 3 before it; 0 of 16 when the cluster tests run on their own, either build. Not
  attributed - but if it recurs, compare with and without that commit first.

- **Watch: `QueuedResultTests.RetryTransactionFireAndForgetSharesOneCompletedTask` failed once** (net8.0, full
  suite, 44ms, 2026-10-08, after the main merge; message not captured), 0 of 6 in isolation. It asserts that a
  retry transaction's per-command tasks are settled when `ExecuteAsync` returns, which `RetryTransaction`
  guarantees by awaiting them; if it recurs, capture the assertion before anything else.

- **`DedicatedThreads` is not pool-independent on Linux** (found 2026-10-08 by the starvation harness). .NET's
  "blocking" socket `Receive` is non-blocking underneath and is woken through the thread pool, so with the pool
  starved the dedicated reader thread never wakes for data already in the kernel buffer (seen: Recv-Q 7 bytes,
  one reply, unread; reader parked in `SocketAsyncContext.ReceiveFrom`). Affects the pump and the sync context
  alike, and probably v3. `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1` removes it entirely. Options: document
  that alongside `DedicatedThreads`, or have the dedicated reader (and writer) wait with `Socket.Poll` - a direct
  syscall - before each blocking call, so it never needs the pool.
  **2026-10-09, fixed (D2 option b).** Probed first (scratchpad `sockprobe`, PING round trips over
  loopback): async connect 57.6us CPU + 1 pool item per round trip; `Poll` before `Receive` 50.9us and STILL 1 pool
  item - the cost is the epoll registration itself, which queues a pool item per readiness event whether or not
  anyone waits; `Socket.Blocking = true` changes nothing; a socket that was only ever used synchronously 7.6us and 0.
  Dispose wakes a blocked `Receive` and a blocked `Poll` alike. After connect, a dedicated connection already did
  only synchronous IO (the Sync writer never transitions, inline sends defer to it), so the fix is the connect:
  `Socket.Connect` and `SslStream.AuthenticateAsClient` on a LongRunning thread (the connect timeout races the
  attempt), with cancellation disposing the socket; not with a tunnel. Results, dedicated, no env var:
  `IDatabase` sync seq 25.6k ops/s at 16.1us CPU (was 20.4k at 78us); TLS 23.7us vs 105us default, 0 pool items;
  starved pool, every sync path runs. `SyncCompletionTests` now asserts the absolute (500 -> 0 pool items).
  **.NET 11 (RC1, measured with the same probe):** dotnet/runtime#124200 flips a socket back to blocking after
  `ConnectAsync`, so async-connect + sync IO drops to 17.8us CPU per round trip - but still 1 pool item (the epoll
  registration stays), and TLS's async handshake still flips it for good. Sync connect is 7.7us and 0 on both
  runtimes, so the fix stands on .NET 11 too.
  **Side finding, not fixed:** in DEFAULT mode an abandoned connect (timeout) keeps its socket in SYN-SENT until
  the OS gives up - `ConnectAsync` is called without the token. 5 blackholed attempts left 5 sockets; dedicated
  mode leaves none.

- **Batch/transaction buffer packing**: write a batch's commands adjacently into one shared buffer, rather
  than one rented frame per command, and hand the transport one contiguous run. The abandoned v3-era RESPite
  spike implemented this fully (PR #2959, `marc/respite`, "WIP : RESPite overhaul" - still open but idle since 2026-08: `src/RESPite/RespBatch.cs`,
  `Connections/Internal/{Buffering,Merging}BatchConnection.cs`, `SynchronizedBlockBufferSerializer`; v4's
  `BlockBufferSerializer` remarks are the surviving trace).
  **Check first** whether the interpolated writer's reserved prologue - the `*N` count is back-filled into
  padding reserved ahead of each frame - still lets frames sit adjacently, or leaves gaps that cost a copy.
- **Retry: stale comments.** `RetryDatabase.ExecuteAsync` says the policy "will live here in due course; for
  now it is a straight pass-through" (it retries); `RespRetryExecutor`'s remarks say "Failover is not wired
  up yet" (it is, via `GetFailoverSource()`).
- **Prose still says "the new core".** The rename pass (a1673a96) renamed types and members only; comments
  and docs still say "the new core" / "old core" where there is now one. A wording pass to "the connection
  manager" (or just "the core") - noisy, so its own commit.
- **Performance, remaining ideas** (after the 2026-10-07 work that took v4 from ~55% of v3 to parity or better;
  each now promises single-digit percent, so parked): `RespPayload` as a struct (public API, ~32 B/op); keep small
  request bytes inline in the operation (removes the last per-command array when operations are new at depth, at
  a fixed cost to every operation); `IDatabase`'s remaining ~100 B/op over the context API is the `Task` and the
  thread-pool work item that runs its completion (the completion source went 2026-10-07: an `AsyncTaskMethodBuilder`
  promise held by the operation) - the work item could go if the operation queued itself (`IThreadPoolWorkItem`,
  net6+) rather than through the source's continuation; slim the operation further (266 B: the token + registration are 24, the
  request memory/owner/refcount 28); a growable SPSC ring for the pending queue (`ConcurrentQueue` segments are ~5%
  of allocation at extreme depth, but only on growth - risky, three threads touch it). Not worth it: stackalloc
  request rendering (that path is ~1% of CPU now), delaying the writer to coalesce (small writes follow
  throughput, they do not limit it), a dedicated small-buffer pool (slower than ArrayPool's thread cache).
  **Warm-up** is JIT tiering, as for v3 (which ramps as much): `TieredCompilation=0` runs ~1.4-1.5M INCR/s from
  the first iteration where default tiering is ~1.0M rising to ~1.25M by the sixth, hot methods lingering in the
  PGO instrumented tier. Options are blunt (`AggressiveOptimization` forfeits PGO; ReadyToRun in the package is a
  packaging change) - left to applications, and benchmarks should warm up before measuring.
- **Replace the method-replaying decorators with executor decorators.** Eager frames mean the "what to replay"
  problem is solved below the API: `RedisDatabase` is a thin `IDatabase` over a `RespDatabaseContext`
  (every member is `_inner.<Group>.XAsync(...)`), so `new RedisDatabase(ctx.WithExecutor(retry))` retries
  the whole surface. Candidates: `RetryDatabase` + `RetryTransaction` (+ the `AutoDatabaseGenerator` /
  `CapturedArgs` capture machinery), `KeyPrefixed*` (~2.6k lines; `RespContext` already prefixes at write),
  and `MultiGroupDatabase` (same generator; `RespGroupExecutor` exists). What has to be settled first:
  - **Batches/transactions through a retrying executor.** `RespRetryExecutor` forwards `CanWriteRuns`/
    `CanWriteTransactions` but not `TrySendBatch`/`TrySendTransaction`, so a run is not retried as a unit.
    Retrying one needs fresh pooled operations per attempt behind durable per-command tasks - what
    `RetryTransaction` does today by replaying calls - and conditions re-evaluated per attempt.
  - **Sync.** `RedisDatabase` sync members are `Wait(...Async)`, so they would *retry* (blocking through the
    delays) rather than hit the executor's throwing `Send`. A decision, not a blocker: allow it, or keep
    `WithRetry` async-only by refusing sync on the returned object.
  - **Inners that are not context-backed.** `WithRetry`/`WithKeyPrefix` accept any `IDatabaseAsync`
    (mocks, user decorators). Keep the old wrapper as the fallback, or require `IRespTarget`.
  - **Unit of retry moves from method to frame.** Multi-send members (cluster fan-out splits, scan pages,
    script load paths, `HashImport` chunks) retry per send rather than restarting the method; usually
    better, but audit them. Scans gain retry they do not have today (`RetryDatabase` forwards them).
  - **Prefix-specific:** stripping the prefix from keys in *replies*, `KeyRandom` refusal, script keys
    (`RedisDatabase.Scripts` passes `withKeyPrefix: null`), and `WithKeyPrefix`'s own
    `is KeyPrefixedDatabase` prefix-merge.
  - **Feature reporting:** `GetFeatures` must still report `Retry`/prefix so nesting and batch rejection hold.
  - Gains beyond code size: `asyncState` would work on a retrying database (the bridge task spans attempts).
- **Trusted-callback completion mode** - test whether Respire's speed comes from completing callers inline
  on the reader with a watchdog; if so, offer it opt-in, default off. **Mechanism confirmed** (2026-10-07,
  Respire 0.7.14 @ ee842696, `Networking/CompletionScheduler.cs`): completion cores are
  `RunContinuationsAsynchronously = false`; each receive drain's replies (up to 256) go to ONE runner that
  completes them serially, in wire order, inline. Busy socket: the runner is a pool work item. Idle socket:
  the receive loop posts its next read and then runs the batch on its own thread (`RunWhileAwaiting`). A
  watcher hands the undelivered tail to a fresh runner after a hard-coded 500 ms with no progress
  (`StalledDeliveryThreshold`, internal), logging a warning; ordering is lost from that point. So: no
  thread theft from the socket read, but head-of-line blocking of every caller on the connection behind any
  continuation, up to 500 ms. Connection model: multiplexed, one connection by default
  (`RespireOptions.Connections`, round-robin when raised), plus a small dedicated pool for blocking commands.
  **Harm measured** (RespFest work league, `results/work-league-20261007`, RespFest f341e8e local): callers
  spin N us of CPU after each reply. Respire 1.12M / 38k / 9.4k ops/s at 0 / 20 / 100 us - pinned to the
  serial 1/N ceiling, p50 6.7 ms at 100 us; v3 394k / 303k / 91k and v4 935k / 327k / 89k scale with cores.
  So inline completion is out as a default; the only variant still worth a trial is batching pool wakes
  per parse pass with continuations kept asynchronous.

## Status

- **2026-10-09: one group accessor instead of two (was a backlog item).** `RespDatabaseContext` is now an
  `IRespKeyspaceTarget` and `RespServerContext` an `IRespServerTarget` (each its own context), so the single
  constrained-generic accessor per group serves targets and contexts alike; the non-generic ones are gone, from
  the extension properties and the down-level shims. Machine code: identical to the old direct accessor at tier 1
  (4 bytes for `ctx.Strings`, 1,935 for a prefixed context, 40 via `IDatabase`); only without tiering does the JIT
  leave a direct call to the trivial getter rather than inlining it. Side effect, a fix: `Config`, `Diagnostics`,
  `Scripts` and `PubSub` had no target accessor, so `server.Config` did not compile on an `IServer`; it does now,
  and so does `db.PubSub`.

- **2026-10-08: `Blocking()` contexts (named `Synchronous()` until 2026-10-09) - a supported sync path for command groups, ours and libraries'.**
  `ctx.Blocking()` (on `RespContext`, `RespDatabaseContext`, `RespServerContext`) makes the shared send path
  call the executor's blocking `Send`, so every `...Async` group method returns an already-completed task and
  `.GetAwaiter().GetResult()` is correct (on an ordinary context it is not: a pending `ValueTask` throws on
  `GetResult`). Starvation harness (2 pool workers, both blocked; caller on its own thread): healthy pool, sync
  context 25.6k ops/s vs `IDatabase` sync (pump) 23.9k; starved with `DedicatedThreads` and inline socket
  completions, both ~22k and never stalling, naive `AsTask().GetResult()` stalling every time.
  **2026-10-09, done:** paired sends (EVALSHA behind SCRIPT LOAD, HIMPORT PREPARE + row) block too, via a
  blocking pair `Send` on the executors (the endpoint waits the body on the calling thread and tells the gate
  there); `RedisDatabase`'s sync members all send through a cached blocking context, helpers included
  (scripts, locks, `StringGetWithExpiry` - now two round trips rather than one pipelined pair); `SyncCall`,
  `SyncPump` and RESPite's `IContinuationSink`/interposition machinery are deleted. The endpoint's blocking
  wait got the backstop `SyncWait` had (2x sync timeout + 5s when heartbeat-driven), from an explicit
  `SyncTimeoutMilliseconds` rather than via the modelled server, which can be null. Executors that cannot
  block (`CanSendBlocking`: batch, transaction) send asynchronously and `SyncWait` blocks on the task.
  Retry blocks since 2026-10-09: `RespRetryExecutor.Send` (single and pair) runs the loop over the inner
  synchronous send, pausing with `RetryController.FailoverOrDelay` (`Thread.Sleep`, or the failover token's
  wait handle) - prompted by the NRedisStack port, which proxies through the retry wrapper.

- **2026-10-08: InlineSends, ON by default since 10 clean full-suite runs (opt out: `SEREDIS_INLINESENDS=0`).** A caller whose request is
  alone in flight on its connection sends its own bytes instead of waking the writer loop; bounded to what was
  staged when it claimed the writer, with the rest (and any send that would block) handed to the loop. Why: each
  thread-pool hand-off wakes a worker that then spins idle, and that was ~85us of every sequential request's CPU
  in v3 and v4 alike (118 -> 30us per INCR with the pool's spin disabled). RespFest (`inline3-*`, 4.0.67):
  work-100-seq 265.8 -> 203.0us CPU/op (v3 207.3), incr-seq 69.2 -> 66.5, get-1k-seq 70.3 -> 67.7 (+2% ops/s);
  concurrent within noise. Made the default after 10 full-suite runs with it on (net8.0 and net10.0
  alternating): 9 clean, 1 with only the known `RespHashImportProbeTests` flake. An earlier version had shown two
  `ScanTests.*ScanLarge` failures and a 10s timeout in 6 runs; none recurred - keep an eye out.

- **2026-10-08: queued operations act through the token they were queued with.** Batches and transactions
  queue `QueuedOperation` (operation + token) and runs are written as `RespRunEntry`, so a batch member that was
  cancelled, consumed and re-rented before `Execute` is skipped rather than writing - or faulting - the stranger
  now on its instance. Test: `RespConnectionTests.ARunWritesTheLifeThatWasQueuedNotTheInstancesCurrentOne`
  (fails on the old behaviour by writing the stranger's request).

- **2026-10-07: a lost batch completion, found and fixed.** One `IBatch` command in ~40 benchmark runs never
  completed: the operation had been pooled while a writer still held a reservation on its request (the reply landed
  and the result was taken between the writer staging the bytes and releasing them), re-rented for a batch, and the
  writer's late release took the NEW life's request count to zero - so the batch write skipped it as "already
  completed". Now the last request reference recycles (`RespMessageBase.Recycle`). Also: a stale or premature
  `GetResult` reset whichever life was current in a `finally`; it now throws without touching it. Both have
  deterministic tests in `RespConnectionTests`. Benchmarks taken before this are void.

- `v4` branched 2026-10-06 from `marc/v4-core-operation` (now superseded); packages compute as
  `4.0.N-alpha`, assembly version `4.0.0.0`. `main` drift: 0 (merged at c176699f).
- Latest fully green `v4` CI: 220d480c (Ubuntu; Windows net10.0 and net481), after the stall, stale-slot, pair-SELECT and
  stale-pump-signal fixes. First fully green: 37428740387 at cacab8cf.
