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
| D6 | **Event 71** (handshake reply dump at Information) is not ported. | stays unported |

## Pending work (no decision needed)

- **CI: `GetServerTestsCluster.GetServerByKeyMemoization` (RESP3) fails on Windows net481** - 2 of the first 3
  `v4` runs, each after a cluster connect that waited the full 20s at the very start of the net481 run (three
  cluster connects stalled together). Suspected lost wake-up, fixed speculatively in d45eadf7 (announce
  "connected" only after the connection is published); not reproduced locally. Tests now print the connection
  log, with thread-pool stats, for any connect that uses half its timeout - read it if it recurs.
- **Absorb `main` by merging**, little and often; resolve each conflict by re-expressing the change in v4
  terms and say how in the merge message. Drift check: `git rev-list --count v4..origin/main`. The final
  landing on `main` must be a real merge, never a squash.
- **Two-core leftovers:**
  - `Subscription._onNewCore` / `IsOwnedByNewCore` / `NewCoreOwnsAnySubscription` now mean only "something
    subscribed this"; collapsing them changes the never-sent case - look, don't delete.
  - ~30 `NewCoreIfCreated?.` null-tolerances; `ServerEndPoint.IsConnecting => false`.
  - Topology flows both ways between `RespTopology` and `ServerSelectionStrategy` ("phase D").
- **Subscription re-aim waits for its in-flight send to fail** (instant on Linux, ~2s on Windows, a connect
  timeout for a dropped SYN). Fixing it means de-duplicating a subscription that lands on two nodes. Not urgent.
- **Retire the `RespTaskBatch` spike**: port `RespBatchExecutorTests`' still-useful cases (forgotten commands,
  run-capable executors, per-slot split) onto `RespBatch`, then delete it and `RespBatchExecutor`.
- **Known flakes**, each needing a capture rather than a guess: `TouchIdleTime` (6381), occasional
  `HashImportProbe`, `RespAggregateTiming`, and the rare ~5s RESP3 stall on a shared connection.

## Backlog (after the alpha)

- **Batch/transaction buffer packing**: write a batch's commands adjacently into one shared buffer, rather
  than one rented frame per command, and hand the transport one contiguous run. The abandoned v3-era RESPite
  spike implemented this fully (PR #2959, `marc/respite`, "WIP : RESPite overhaul" - still open but idle since 2026-08: `src/RESPite/RespBatch.cs`,
  `Connections/Internal/{Buffering,Merging}BatchConnection.cs`, `SynchronizedBlockBufferSerializer`; v4's
  `BlockBufferSerializer` remarks are the surviving trace).
  **Check first** whether the interpolated writer's reserved prologue - the `*N` count is back-filled into
  padding reserved ahead of each frame - still lets frames sit adjacently, or leaves gaps that cost a copy.
- **Trusted-callback completion mode** - test whether Respire's speed comes from completing callers inline
  on the reader with a watchdog; if so, offer it opt-in, default off. Read Respire's completion path first.

## Status

- `v4` branched 2026-10-06 from `marc/v4-core-operation` (now superseded); packages compute as
  `4.0.N-alpha`, assembly version `4.0.0.0`. `main` drift: 0 (merged at c176699f).
- First `v4` CI runs in progress. Previous branch's last full run green: 37391223850.
