# v4 ledger

The current list of what is pending on `v4` and what is waiting on a decision. **Keep it short:** when an
item closes, delete it here and let the commit message hold the story. History and reasoning live in
`v4-alpha-plan.md` (how the old core was retired) and `message-core-replacement.md` (why).

Last updated: 2026-10-06.

## Decisions waiting on Marc

Each has a default the work proceeds on until answered.

| # | question | default meanwhile |
|---|---|---|
| D1 | **#3251's intent (split socket fill from parsing) on v4?** v4 still parses inline on the read loop, async and sync. The throwaway port (`marc/v4-filler-experiment`) measured +39% `get-1k-conc64`, and with a 200-spin park + 64 KiB buffers led every concurrent scenario; cost ~5% on `incr-conc64` and 3-5% sequential. Options: port before the alpha / port as opt-in / after the alpha. | not ported |
| D2 | **Linux: the `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS` question.** Once a socket has done any async op (connect, TLS), the runtime routes readiness through the thread pool - even for a DedicatedThreads reader. The env var removes it (measured: 500 sync calls, 0 pool items), but it is process-wide and read at runtime start, so the library cannot set it for the caller. Options: (a) document only; (b) connect + TLS synchronously for dedicated connections, so the socket never goes async; (c) both. v3 behaved the same. | (a), documented in `docs/SyncOverAsync.md` |
| D3 | **When to cut the first alpha**, and who presses the button: a GitHub Release tagged `v4.0.N-alpha` on `v4` runs `release.yml`. The release dry run (`gh workflow run release.yml --ref v4`) has not been run - it needs your permission. | wait for green `v4` CI, then ask |
| D4 | **Post the v4-port findings to PR #3251?** Drafted (spin-before-park +2.6% on `incr-conc64` in #3251's own reader; read-size hint is a hypothesis; drain-per-wake does not pay). Outward-facing. | not posted |
| D5 | **SER014 batch/transaction, unsettled parts:** conditions borrow the shipped `Condition` type (new spelling later?); whether `RespBatch`/`RespTransaction` stay structs. | as shipped in 2ca83a9d |
| D6 | **Duplicate event ids, shipped in 3.x:** 116 (`RegisteringSlotMapNode` and `RequestingMaintenanceNotifications`) and 117 (`ActivatingUndialledServer` and `MaintenanceNotificationsAccepted`) each name two events. Renumbering one of each pair changes an id someone may filter on; leaving them leaves the ambiguity. | left as shipped |
| D7 | **RESP3 subscriptions on the interactive connection.** Under RESP3, v4 subscribes (at minimum the config channel) on the interactive connection. Redis classes ANY connection with a subscription as pubsub (`flags=P`, whatever the RESP version) and gives it the pubsub output-buffer limit - default `32mb 8mb 60` - instead of normal's unlimited. So a deep pipeline or slow consumer gets the main connection killed: reproduced with 50 callers x 1000 `LRANGE 0 99` (10k of 50k, then closed; with `ConfigurationChannel = ""`, 50k/50k in 0.3 s). The limit is server-side only (`CONFIG SET`, global, usually blocked on managed services). Options: (a) subscriptions always on their own connection, as RESP2 - gives up the "halves the connections" RESP3 pitch in `docs/Resp3.md`; (b) keep sharing, document, and default the config channel off; (c) share only when the user opts in.  **History, and why it is urgent for v3 too:** shipped 3.3.1 never subscribed the config channel under RESP3 at all (the subscribe ran only on the separate subscription socket, which RESP3 does not open) - broadcasts silently missed, but the interactive connection stays `flags=N` (measured: 50k/50k at a 34 MB output-buffer peak). `64e2d150` on `main` (#3254/#3255, 2026-10-01, unreleased) fixed the broadcast by subscribing on the interactive connection, which brings this hazard; v4 has it by absorbing `main`. The next 3.x release would ship it to every default RESP3 user. | benchmark sets `ConfigurationChannel = ""` meanwhile |

## Pending work (no decision needed)

- **CI: `GetServerTestsCluster.GetServerByKeyMemoization` (RESP3) fails on Windows net481** - 2 of the first 3
  `v4` runs, each after a cluster connect that waited the full 20s at the very start of the net481 run (three
  cluster connects stalled together). Suspected lost wake-up, fixed speculatively in d45eadf7 (announce
  "connected" only after the connection is published); not reproduced locally. Tests now print the connection
  log, with thread-pool stats, for any connect that uses half its timeout - read it if it recurs.
- **A server-side close is silent.** When Redis closed the interactive connection for the output-buffer limit
  (D7), nothing was logged and no `ConnectionFailed` fired; every pending command failed "The connection is
  closed." Check what the transport reports on a peer close, that it raises the event and the reconnect
  path, and that the failure says WHY (the server's reason is in its log, not on the wire).
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
- **Known flakes** (beyond the stall above): `TouchIdleTime` (6381), `RespAggregateTiming`;
  `RedisBatchTests.AWatchConflictIsDistinctFromAFailedCondition` timed out once (fake transport) on a
  heavily loaded machine, 20/20 since.
  (`RespHashImportProbeTests.AConnectionLocalPreambleIsTheScriptSeam...` was a startup race in the TEST, now
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

- **Batch/transaction buffer packing**: write a batch's commands adjacently into one shared buffer, rather
  than one rented frame per command, and hand the transport one contiguous run. The abandoned v3-era RESPite
  spike implemented this fully (PR #2959, `marc/respite`, "WIP : RESPite overhaul" - still open but idle since 2026-08: `src/RESPite/RespBatch.cs`,
  `Connections/Internal/{Buffering,Merging}BatchConnection.cs`, `SynchronizedBlockBufferSerializer`; v4's
  `BlockBufferSerializer` remarks are the surviving trace).
  **Check first** whether the interpolated writer's reserved prologue - the `*N` count is back-filled into
  padding reserved ahead of each frame - still lets frames sit adjacently, or leaves gaps that cost a copy.
- **Retry: the two `WithRetry` overloads default differently.** `IDatabaseAsync.WithRetry()` resolves the
  connection's configured policy (`MultiGroupOptions`/`ConfigurationOptions.RetryPolicy`); the context
  overload (`DatabaseExtensions.cs`, `RespDatabaseContext.WithRetry`) falls straight to `RetryPolicy.Default`.
- **Retry: stale comments.** `RetryDatabase.ExecuteAsync` says the policy "will live here in due course; for
  now it is a straight pass-through" (it retries); `RespRetryExecutor`'s remarks say "Failover is not wired
  up yet" (it is, via `GetFailoverSource()`).
- **Prose still says "the new core".** The rename pass (a1673a96) renamed types and members only; comments
  and docs still say "the new core" / "old core" where there is now one. A wording pass to "the connection
  manager" (or just "the core") - noisy, so its own commit.
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

- `v4` branched 2026-10-06 from `marc/v4-core-operation` (now superseded); packages compute as
  `4.0.N-alpha`, assembly version `4.0.0.0`. `main` drift: 0 (merged at c176699f).
- Latest fully green `v4` CI: 220d480c (Ubuntu; Windows net10.0 and net481), after the stall, stale-slot, pair-SELECT and
  stale-pump-signal fixes. First fully green: 37428740387 at cacab8cf.
