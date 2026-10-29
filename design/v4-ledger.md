# v4 ledger

The current list of what is pending on `v4` and what is waiting on a decision. **Keep it short:** when an
item closes, delete it here and let the commit message hold the story. History and reasoning live in
`v4-alpha-plan.md` (how the old core was retired) and `message-core-replacement.md` (why).

Last updated: 2026-10-13.

## Decisions waiting on Marc

Each has a default the work proceeds on until answered.

| # | question | default meanwhile |
|---|---|---|
| D3 | **Alpha cadence.** Latest is 4.0.127-alpha, cut 2026-10-12 (GitHub Release on `v4` runs `release.yml`). | cut when Marc says; CI green first |
| D4 | **Post the v4-port findings to PR #3251?** Drafted (spin-before-park +2.6% on `incr-conc64` in #3251's own reader; read-size hint is a hypothesis; drain-per-wake does not pay). Outward-facing. | not posted |
| D5 | **SER014 batch/transaction, unsettled parts:** conditions borrow the shipped `Condition` type (new spelling later?); whether `RespBatch`/`RespTransaction` stay structs. | as shipped in 2ca83a9d |

Closed on `v4` itself: **D1** - #3251's intent is ported and the default async reader (3cc99f62, then 41afb04b
and ff5df601), with `SingleReadLoop` / `SEREDIS_SINGLEREADLOOP` as the opt-out; **D2** - option (b), dedicated
connections connect and run TLS synchronously (dbb38f88).

Closed by the 3.4.0 merge (2026-10-09), because `main` decided them: **D6** - duplicate event ids renumbered on `main`
(#3266; 124-127 are now main's shipped ids, and v4's colliding v4-only events moved to 128-131); **D7** - RESP3
pub/sub gets a dedicated connection by default, sharing is opt-in via `SharedSubscriptionConnection` (#3264).

## Pending work (no decision needed)

- **Cache performance (2026-10-10) - shipped, and the list of ideas still to explore.** The design as built, with its reasons and rejections: `design/client-side-cache.md`. Shipped: c5370268
  (routing to answer a constant on every cached read: the ~30% RespFest regression), 04ef29d9 + 1bd841e6 (replies
  in GC-owned slabs: no refcount CAS per hit, no mid-life reply arrays; 1 hot key at 24 threads 11.5 -> 149 M/s),
  0023c9e6 (`CacheClock`), a5af2dec (eviction-queue leak; lazy in-flight completion), a7325526
  (`CacheOptions.Admission`, TinyLFU doorkeeper; all-miss 307k -> 593k ops/s, GC pause 29% -> 4%). Single-thread hit
  134 -> 114ns. Tools: `CacheHitSendBenchmarks` (`--inProcess`: the `.claude/worktrees` copies break BDN's
  out-of-process build), `cache-scaling [s] host:port` (hits), `cache-scaling miss 8 host:port [nocache|admit]`,
  `dotnet-trace` + the alloc-by-type summariser recipe in memory. **To explore, roughly in order:**
  - [x] Charge a per-entry overhead to `MaxBytes` (256 bytes + the request's length): before, a million tiny
    replies (counts) reported ~5 MB while holding ~350 MB of entry objects.
  - [x] Cache key bytes into the slab beside the reply: hits ~+10% at 100k keys / 24 threads (locality: the
    key a lookup finds sits beside its reply); misses neutral - one object fewer is lost in the remaining GC cost.
  - [x] Fold the single-dependency case into `Entry`: one dependent load fewer per hit (+3-5% at 100k keys / 24
    threads), and the fill's array now dies young (all-miss GC pause ~25% -> ~19%).
  - [x] A skewed (Zipf) workload in the harness (cc95aeef): `OnRepeatedMiss` wins outright - hit rate 72.4% ->
    76.6%, 1.06M -> 1.6M ops/s, CPU/op 9.8 -> 7.1us. **Decided (Marc, 2026-10-10): the default**, as
    `CacheAdmission.Default` resolving to it, documented.
  - [x] Propose a skewed mixed scenario for RespFest: done on their side (`cache-zipf-seq`/`-conc64`, efc2419).
  - [x] Doorkeeper sizing: from `MaxBytes / EntryOverheadBytes` rather than assuming ~1 KiB per entry.
  - [x] **The default behaved as `OnFirstMiss` in RespFest** (Zipf 72.1% vs 72.5%; all-miss identical). Not
    sizing: the byte hash (`RedisValue.GetHashCode(span)`, behind `RespRequest`, `RedisKey`, `RedisChannel`,
    `RespKeyTable`) gave 200,000 framed `GET rf:cbig:{i}` requests ~42,000 hashes, and the doorkeeper took its probe
    from the low bits of a bare multiply, landing them on ~4,500 bits: every first miss read as a repeat. The
    harness used zero-padded keys, which hashed fine - the lesson is to bench on more than one key shape. Fixed:
    a word-at-a-time multiply + MurmurHash3 finalizer (ideal distribution over 12 key shapes, same cost on a short
    request), and the doorkeeper mixes before probing. On RespFest's keys: Zipf 72.4% -> 75.1%, all-miss 324k ->
    610k ops/s. Hit path: the stub 1-key loop moves 72 -> 70-97ns between builds of ANY change to that function
    (a variant returning the old value measured the same), so code layout, not the hash; 100k keys +4-7ns.
  - [x] **After that fix, the default still matched `OnFirstMiss` at 64 callers** (72.0-72.7% vs 75.8% at 1 caller).
    RespFest's runners replay one 1M-draw key table, 3-30x over a cell; with a 16M table the gain returned, so they
    called it their fault (2026-10-11). It was half ours: the replay exposed a doorkeeper defect. It aged only by
    *first sightings*, and the table holds ~120,000 distinct keys against a 131,072 window - once each had missed
    once nothing new arrived, the filter never cleared, and every miss read as a repeat (modelled: 0 refusals). Any
    keyspace smaller than the window does the same. Fixed: every miss ages it, as TinyLFU ages by samples.
    `design/admitsim.py` (a model of doorkeeper + slabs + budget; imports RespFest's `harness.keyseq`): 1M table at
    64 callers 72.5% -> 76.1%; independent draws 75.3% -> 76.1% (a 64k window gives 76.7%, not taken: tuning).
    Measured, harness now drawing as it goes (it had replayed a 4M table): default 76.4%/76.0% at 1/64 callers vs
    72.4%/72.5% `OnFirstMiss`, 1.45M vs 1.07M ops/s at 64; all-miss 596k ops/s (was 593k). Dropped: coalescing
    refused misses, the reproduction-with-counters run, the slowed-64 and 8-caller cells.
  - [x] RespFest `--patch` run on 4.0.124, BOTH tables (2026-10-12): identical. Default 75.9-76.0% (conc64 2.65M /
    2.70M ops/s; lease 3.13M / 3.18M), `OnFirstMiss` 72.3-72.5% (2.10M / 2.29M).
    Unconfirmed, low priority: Their 64-caller sad-path 4-5% hits are expected to stay: a cyclic walk is a bounded keyspace, and the
    doorkeeper's lower churn lets a few entries live until their key returns (the harness's own all-miss cell, a
    cycle of 3,125 keys per caller: 12.5% at 64 callers, 0.0% at 1).
  - [ ] The gap to Lettuce's 79% (check which cache its runner uses; Caffeine's W-TinyLFU compares the candidate's
    frequency with the victim's). Model before code: second-chance eviction (below) and frequency-vs-victim
    admission (a sketch: a write per hit, against a hit path that writes nothing) in `admitsim.py`, on standard
    traces (ARC/LIRS, Caffeine's simulator) as well as Zipf - not fitted to one benchmark. Under ~1 point: close.
  - [ ] Second chance at slab eviction: a write-once "touched" flag per entry; touched entries copied forward rather
    than evicted with their slab. Only if the Zipf workload shows hot keys churning out.
  - [ ] The cached miss sends untyped and parses afterwards, where an uncached send parses typed on the reader.
  - [ ] `RespMultiplexerExecutor.CanWritePreamble` still resolves a route (server selection + the endpoint lock) on
    every paired send. Unlike `Transactional`/`Accumulates`/`CanCancel` (made constant in c5370268, 40ca69ee) the
    answer is route-dependent - a database view (`RespDatabaseExecutor`) does not override it, so says false, where
    an endpoint says true - though the executor is bound to one database, so it is probably fixed per instance.
    Check, then compute it once.
  - [ ] `RespKeyTable` nodes: check whether a key's node outlives every entry that depended on it (growth with the
    keyspace, not the cache).
  - [ ] Slab recycling via per-thread reader counters - only if large-object churn (gen2) shows up as a cost.
  - [ ] The interpolated builder: ~26ns over a 4ns floor, plus 10-15ns on every group method from inline-budget
    exhaustion (6548bffe). Draft parked as `design/parked/builder-inlining.patch` (`git apply` it) (forced inlining of
    `CommitBulk`/`CountArguments`, hot/cold `FoldSlot`); unmeasured.
  Rejected, with reasons in the commits: per-entry striped refcounts (~150 MB of padded counters), pinned-object heap
  for entries (no better), finalizer-driven slab recycling (interior pointers outlive the wrapper; finalizable
  objects are mid-life by construction).
- **Watch: `GetServerTestsCluster.GetServerByKeyMemoization` (RESP3) on Windows net481** - not seen in the 60 CI
  runs since 2026-10-09; drop it if that holds. Originally 2 of the first 3 `v4` runs, each after a cluster connect that waited the full 20s at the very start of the net481 run (three
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
- **The "~5s with nothing inbound" stall: root cause found and fixed** (the commit after 7aa7f818); CI since shows
  only the `RespCacheChurnTests` shape below.
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
  recurs, take a dump mid-stall (the old fix was found that way) rather than retrying. **Recurred:** 2026-10-10
  (6ab36833, Ubuntu, `"benchmark"` via context, 12s) and 2026-10-12 (4c206f39, Windows net10.0, `"scoped"`, 6s).
- **Watch: `BacklogTests.TotalOutstandingIncludesBacklogQueue` is intermittent, and was before today** (2026-10-09):
  three pings queued after `SimulateConnectionFailure` with `AllowConnect = false` sometimes show
  `PendingUnsentItems == 0`. Looked like a regression (2 of 3 alone at the head), but a bisect plus re-runs put the
  morning's 286170a8 at 1 of 8 as well, the same rate as the head - so pre-existing and environment-sensitive.
  Not investigated further; first suspect is the pings completing (or faulting) before the counters are read.
  Again on 5d4a4981 (2026-10-12, Ubuntu, same message).
- **Known flakes** (beyond the stall above): `SentinelTests.AbortOnConnectFailFalseRecoversOnceSentinelsAppear` (RESP2, Ubuntu: "did not recover once the sentinel became reachable", a5af2dec 2026-10-10 and ea92c293 2026-10-12; unrelated changes both times); `TouchIdleTime` (6381), `RespAggregateTiming`, `RespAggregateProtoTests.ADeferredWalkNeedsNoStorageAtAll` (net8, full suite only; per-thread allocation measure); `ClusterTopologyUnitTests.SlotLessNodesAreKnownButNotConnected` (net8 full suite once, 2026-10-09; 3/3 alone);
  `RedisBatchTests.AWatchConflictIsDistinctFromAFailedCondition` timed out once (fake transport) on a
  heavily loaded machine, 20/20 since.
  CI once each, not investigated (2026-10-10 to 10-12): `ConnectCustomConfigTests.HeartbeatConsistencyCheckPingsAsync`
  (net481), `RespSurfaceTests.EachPingMeasureGetsItsOwnClock` (net481),
  `DedicatedThreadsTests.WithTheFlag_PubSubStaysOnTheThreadPool(shared: true)` (net481),
  `RespTrackingTests.AThirdPartyWriteInvalidatesWhatWeCached` + `...TheKeyBytesTheServerSendsAreTheOnesWeRecorded`
  (net481, same run), `Resp3DowngradeTests.SubscribeQueuedBeforeDowngradeDoesNotPoisonInteractive` (net10.0, 11s),
  `ClusterShardedTests.SubscribeToWrongServerAsync` (Ubuntu). And one with a known cause, in the TEST:
  `CancellationTests.WithCancellation_CancelledToken_ThrowsOperationCanceledException` ("no exception thrown",
  Ubuntu, cc327de8) - `Task.WaitAsync(token)` returns an already-completed task as-is, and with inline sends a
  loopback SET can complete before `WaitAsync` looks; it should cancel through the API's own token instead.
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

- **Client-side cache across a geo/active-active failover** - closed 2026-10-12, except one run. Read-your-writes
  on failback: every member's cache is now stamped by our own writes (`RespClientCache.SetPeers`); flushing on
  each switch was rejected, since equal-weight groups switch on latency. A silent inactive member: already
  covered - group members force `HeartbeatConsistencyChecks`, so each endpoint is pinged every heartbeat and a
  half-open connection trips the dead-socket rule, flushing that member's cache. Design in
  `client-side-cache.md` §6. Tests: `GroupCacheTests` (in-process; fail without the fix).
  - [ ] **Run `ActiveActiveCacheScenarioTests`** against the Active-Active environment: written and compiled,
    never executed (no fault-injector environment here). It also asserts what the in-process servers cannot - that
    an inactive member's cache hears a write replicated from elsewhere.

- **Alternative client-side-cache invalidation sources, and opt-in/out.** Today invalidation comes only from
  `CLIENT TRACKING` (`CacheTrackingMode`: Default -> BCAST, or per-key tracking) and the cache is opt-in per multiplexer
  (`ConfigurationOptions.ClientCache` is null by default; `CacheOptions.Enabled = true` applies once one is given). Add policies for deployments where tracking is unavailable or unwanted
  (proxies, older servers, some managed offerings): **keyspace notifications** (`__keyspace@<db>__:*`, which
  needs `notify-keyspace-events` configured server-side and carries no RESP3 requirement) and **explicit
  pub/sub** (an application-defined invalidation channel the writer publishes to). Settle the rest of the opt-in/out
  story at the same time: how a caller opts out per multiplexer and per
  command, and what the cache does when the chosen source is not available (refuse loudly, as RESP2 + cache
  does today, versus degrade to no caching).

- **Client-side cache: public counters / metrics** - hits, misses, stored, refused (admission), invalidations,
  stale-served. They exist internally (`RespClientCache.Stored`, `Count`, ...; tests read them) but nothing public:
  pick a shape (`System.Diagnostics.Metrics` instruments, a snapshot struct, or both).

- **Non-nullable `AppendFormatted(long)` / `(double)` overloads** on `RespRequestBuilder`, beside today's
  `long?`/`double?` (asked for by the NRedisStack port). Needs an overload-resolution sweep across the interpolated
  call sites before adding.

- **.NET 11 TFM:** try runtime async; prototype caller-driven TLS (`TlsBufferSession`); run the suite and RespFest on
  11 early.
  **The RC2 thread-pool regression (dotnet/runtime#135855) lands mostly on our own hops** (2026-10-15, 64 callers,
  GET 1 KiB, local Redis, client on 12 CPUs, median of 2): v4 default 762k -> 532k ops/s (-30%) at 5us caller work,
  but `SEREDIS_DEDICATEDTHREADS=1` on RC2 gives 758k (.NET 10 dedicated: 798k), and 1.17M at no work (.NET 10
  default 963k). `SEREDIS_SINGLEREADLOOP=1` alone: -17% on RC2 - the filler->parser hop is a large share. The
  socket/reader hops are ~0.1 pool items per op but gate every caller on the connection. The Redis-free repro
  (`--io dedicated`, `--complete ownpool|inline`, scratch patch) splits its -36% about 1/3 IO hop, 2/3 caller
  continuations; a naive own pool for continuations is 25% slower than .NET 10's pool, inline is serial (82k).
  Next: dedicated reader (+writer) by default on net11+, with a cap for wide clusters; 1 caller costs -7% ops but
  less CPU per op.
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
