---
feature: cluster-slot-stats
decision_class: C
estimated_size: small-medium
tracks: redis/redis CLUSTER SLOT-STATS (src/commands/cluster-slot-stats.json, since 8.2.0; implementation src/cluster_slot_stats.c)
target_version: "8.2"
unit_targets:
  - RoundTripUnitTests.ClusterSlotStatsRoundTrip
  - ClusterSlotStatsTests (live, cluster topology)
open_questions: 4
---

# CLUSTER SLOT-STATS: implementation plan

No shared HLD exists for this command (it predates the parity pipeline); the facts below are read
directly from the server: `src/commands/cluster-slot-stats.json` and `src/cluster_slot_stats.c`
(`clusterSlotStatsCommand`, `addReplySlotStat`), verified against a live 8.10 cluster
(`redislabs/client-libs-test`, the repo test topology). `docs/CommandCoverage.md` lists the command
as `deferred` ("pairs with hot-key tooling"); the hot-key tooling (`HOTKEYS`, `IServer.HotKeys*`)
has since shipped, so this promotes the row.

## Server facts (all observed)

- `CLUSTER SLOT-STATS <SLOTSRANGE start end | ORDERBY metric [LIMIT n] [ASC|DESC]>`, arity -4,
  flags STALE LOADING. Strictly a oneof: exactly one filter form per call.
- **Cluster-only**: a non-cluster server answers `ERR This instance has cluster support disabled`
  (observed on the topology's standalone node).
- Only slots assigned to the answering node's **shard** are reported; `SLOTSRANGE` returns them in
  ascending slot order (observed: a range owned by another shard returns an empty array), `ORDERBY`
  sorted by the metric (ties: ascending slot), default `DESC`, `LIMIT` default = all (1..16384;
  out of range → error).
- Reply: array of `[slot, map]` pairs; the map always carries `key-count`, plus `memory-bytes`,
  `cpu-usec`, `network-bytes-in`/`network-bytes-out` according to the `cluster-slot-stats-enabled`
  config (multi-arg enum: `no`/`yes`/`cpu`/`net`/`mem`). Observed: `CONFIG SET
  cluster-slot-stats-enabled "cpu net"` works at runtime; `yes` (which includes `mem`) is refused
  at runtime when memory tracking wasn't enabled at boot. A map under RESP3, flat pairs under
  RESP2 (`addReplyMapLen`).
- `ORDERBY` by a metric whose collection is disabled → `ERR Unrecognized sort metric for ORDERBY.`
- Metric wire names: `key-count`, `cpu-usec`, `memory-bytes`, `network-bytes-in`,
  `network-bytes-out`.

## Traced siblings

- **`CLUSTER SLOTS`** (`ClusterSlots.cs`, `ClusterSlots.ResultProcessor.cs`,
  `RedisServer.GetClusterSlotsMessage`, `ClusterSlotsTests.cs`) — the verified model for a parsed
  `CLUSTER` subcommand on `IServer`: partial-interface file named for the feature, message built
  with `Message.Create(-1, flags.WithCategory(NodeLocalRead), RedisCommand.CLUSTER, <literal>...)`,
  lenient aggregate parsing (drop a malformed entry, not the reply), `null` result for a nil reply.
  No `RedisCommand` enum change (CLUSTER exists), no keyspace-isolation changes (server-scoped).
- **`HOTKEYS`** (`HotKeys.cs`, `HotKeysField.cs`, `HotKeys.ResultProcessor.cs`,
  `HotKeys.Server.cs`) — the model for the *reply-field* handling: an internal `[AsciiHash]` enum
  (`HotKeysField`) with a generated `...Metadata.TryParse`, nominal not positional parsing, unknown
  fields skipped/preserved, `cpu`-style counters exposed as `TimeSpan` via microseconds, and the
  "profiling data on `IServer`" API shape this command pairs with operationally.

## Requirements

| # | Requirement | Where covered |
|---|---|---|
| R.1 | `SLOTSRANGE start end` form | `ClusterSlotStatsBySlotRange(Async)` |
| R.2 | `ORDERBY metric [LIMIT n] [ASC\|DESC]` form | `ClusterSlotStatsOrderBy(Async)` |
| R.3 | Reply: array of [slot, metric-map], RESP2 flat pairs / RESP3 map | `ClusterSlotStatsResult.Processor` (category tests, never `Prefix` comparisons) |
| R.4 | `key-count` always; other metrics config-dependent | nullable properties on `ClusterSlotStatsEntry`; absent → `null` |
| R.5 | Metrics the library doesn't know (future servers) | preserved in `ClusterSlotStatsEntry.Metadata`, as `ClusterSlotNode.Metadata` does |
| R.6 | Cluster-only; shard-local answers | XML docs; live test asserts the standalone error and per-node ownership |
| R.7 | Node-local read, safe to replay against that node | `flags.WithCategory(NodeLocalRead)`, as `GetClusterSlotsMessage` |
| R.8 | Version gating | `RedisFeatures.ClusterSlotStats` => `v8_2_0_rc1` (8.2 RC1 = 8.1.240, the existing constant) |
| NF.1 | New public surface is experimental | `[Experimental(Experiments.ClusterSlotStats)]`, new id `SER012` (SER011 is taken by the open set-member-expiry PR #3340) |

## Public API (all `[Experimental("SER012")]`, on `IServer`)

```csharp
ClusterSlotStatsResult? ClusterSlotStatsBySlotRange(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None);
Task<ClusterSlotStatsResult?> ClusterSlotStatsBySlotRangeAsync(int startSlot, int endSlot, CommandFlags flags = CommandFlags.None);
ClusterSlotStatsResult? ClusterSlotStatsOrderBy(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None);
Task<ClusterSlotStatsResult?> ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric metric, long limit = 0, Order order = Order.Descending, CommandFlags flags = CommandFlags.None);

public enum ClusterSlotStatsMetric { KeyCount, CpuTime, MemoryBytes, NetworkBytesIn, NetworkBytesOut }

public sealed class ClusterSlotStatsResult
{
    public IReadOnlyList<ClusterSlotStatsEntry> Slots { get; }
}

public sealed class ClusterSlotStatsEntry
{
    public int Slot { get; }
    public long KeyCount { get; }
    public TimeSpan? CpuTime { get; }        // cpu-usec; null when not collected
    public long? MemoryBytes { get; }        // null when not collected
    public long? NetworkBytesIn { get; }     // null when not collected
    public long? NetworkBytesOut { get; }    // null when not collected
    public IReadOnlyList<KeyValuePair<string, long>> Metadata { get; } // unrecognized metrics, preserved
}
```

Grounding: two distinct method names (not overloads) because the grammar is a strict oneof and the
optional-parameter shapes would otherwise trip RS0026; `Order` is the existing enum (`SORT`,
sorted-set ranges); `limit = 0` means "don't send `LIMIT`" exactly as `HotKeysStart(count = 0)`
means "server default"; `CpuTime` as `TimeSpan` from microseconds mirrors
`HotKeysResult.TotalCpuTime`; the result wrapper + entry list mirrors
`ClusterSlotsResult.Assignments`; `Metadata` preservation mirrors `ClusterSlotNode.Metadata`.
Client-side validation: slots in `[SlotRange.MinSlot, SlotRange.MaxSlot]`, `startSlot <= endSlot`,
`limit >= 0` (`ArgumentOutOfRangeException`), mirroring the server's own refusals.

On the wire, `ASC` is sent only when `Order.Ascending` is requested and `LIMIT` only when
`limit > 0`: the server defaults are descending/unlimited, and the repo convention (`Expiration`,
`GetHashFieldExpireMessage`) is to send the minimal form.

## Files

| File | Change |
|---|---|
| `src/StackExchange.Redis/RedisLiterals.cs` | add `ORDERBY`, `SLOT_STATS` ("SLOT-STATS"), `SLOTSRANGE` (ASC/DESC/LIMIT exist) |
| `src/StackExchange.Redis/ClusterSlotStats.cs` | add: `IServer` members, `ClusterSlotStatsMetric`, `ClusterSlotStatsResult`, `ClusterSlotStatsEntry` |
| `src/StackExchange.Redis/ClusterSlotStatsField.cs` | add: internal `[AsciiHash]` field enum (as `HotKeysField`) |
| `src/StackExchange.Redis/ClusterSlotStats.ResultProcessor.cs` | add: processor (as `ClusterSlots.ResultProcessor.cs`) |
| `src/StackExchange.Redis/ClusterSlotStats.Server.cs` | add: `RedisServer` implementations + internal static message builders (as `HotKeys.Server.cs` / `GetClusterSlotsMessage`) |
| `src/StackExchange.Redis/RedisFeatures.cs` | add `ClusterSlotStats` => `v8_2_0_rc1` |
| `src/RESPite/Shared/Experiments.cs` | add `ClusterSlotStats = "SER012"` |
| `Directory.Build.props` (root) | NoWarn += `SER012` |
| `docs/exp/SER012.md` | add (experiment page) |
| `docs/ClusterSlotStats.md` + `docs/index.md` | add usage page + index row |
| `src/StackExchange.Redis/PublicAPI/PublicAPI.Unshipped.txt` | `[SER012]`-prefixed entries + bare `RedisFeatures.ClusterSlotStats.get`; sorted by `eng/public-api.py` (RS0016 does not fire in this environment, so the entries are hand-written against the shipped `[SER009]`/branch `[SER011]` precedents) |
| `docs/CommandCoverage.md` | remove the `CLUSTER SLOT-STATS` `deferred` row |
| `tests/.../RoundTripUnitTests/ClusterSlotStatsRoundTrip.cs` | add: exact-bytes + parse tests, RESP2/RESP3 |
| `tests/.../ClusterSlotStatsTests.cs` | add: live cluster tests |

Not applicable: `Enums/RedisCommand.cs` (CLUSTER exists), keyspace isolation (server-scoped, as
HOTKEYS), transaction analyzer (read-only reporting command; no pre-8.2 MULTI composition exists).

## Ordered steps

1. Literals; field enum; API types file; processor; server file. Build → collect RS0016 strings
   into `PublicAPI.Unshipped.txt`.
2. Features flag, experiment id, NoWarn, docs pages, coverage row.
3. Round-trip tests (message bytes both forms incl. LIMIT/ASC minimality; replies: full map,
   key-count-only map, unknown-metric preservation, empty array, nil → null) under RESP2 and RESP3.
4. Live tests against the repo cluster topology (8.9.241 reports as 8.10; ≥ 8.2 requirement met):
   ownership (a routed write's slot is reported by exactly the owning node with `key-count ≥ 1`),
   ORDERBY limit/order, config-gated metrics appearing after `CONFIG SET cluster-slot-stats-enabled
   "cpu net"` (restored in `finally`), disabled-metric ORDERBY error, standalone-server error.
5. Full build `/p:CI=true`; run the new tests; update this plan's test section with results.

## Older servers

Pre-8.2 answers `ERR Unknown subcommand`; the feature flag lets callers probe. No behavioral
change for any existing API.

## Open questions

1. **SER012 vs joining an existing experiment** — SER011 is "Server 8.12" on the open #3340 and
   this is an 8.2 command; a feature-specific id (as SER007/SER010 were) avoids coupling the two
   PRs. Default: `SER012 = ClusterSlotStats`.
2. **`limit = 0` sentinel vs `long? limit = null`** — `HotKeysStart(count = 0)` is the precedent
   next door, so 0-means-default wins; `null` would be the greenfield choice.
3. **`CpuTime` naming** — the wire says `cpu-usec`; `HotKeysResult` exposes CPU microseconds as
   `TimeSpan` properties named `*CpuTime*`, so the entry property and the metric enum member follow
   it rather than the wire name.
4. **8.12 cluster-topology note (out of scope here)** — 8.12-rc2 introduces
   `cluster-bus-port-protected-mode` (default on), which refuses to start the repo's cluster nodes;
   the fix (`cluster-bus-port-protected-mode no` in `tests/RedisConfigs/Cluster/*.conf`) can only
   land together with an image bump, because servers ≤ 8.10 abort on the unknown directive. This
   PR stays on the main topology image; the open 8.12 PRs that bump the image need the conf change.

## Test plan

- Round-trip: exact request bytes for `SLOTSRANGE 100 200`, `ORDERBY key-count`, `ORDERBY cpu-usec
  LIMIT 5`, `ORDERBY network-bytes-out LIMIT 3 ASC`; replies parsed identically from RESP2 flat
  pairs and RESP3 maps; unknown metric lands in `Metadata`; nil → null result; empty array → empty
  `Slots`.
- Live (cluster topology, `TestConfig.Current.ClusterServersAndPorts`): see step 4.
- Results (2026-11-07, local): round-trip 10/10 pass (`ClusterSlotStatsRoundTrip`, includes the
  argument-validation facts). Live 12/12 pass, zero skips (`ClusterSlotStatsTests`, 6 tests ×
  RESP2/RESP3 against the repo cluster topology, server 8.9.241): the routed write was counted in
  its slot, whole-range answers stayed shard-local and ascending, ORDERBY honoured LIMIT and both
  orders, `CONFIG SET cluster-slot-stats-enabled "cpu net"` surfaced `CpuTime`/`NetworkBytes*`
  (with `MemoryBytes` still null, and the config restored), ORDERBY on the disabled `mem` metric was
  refused with the ORDERBY error, and the standalone node refused with "cluster support disabled".
  Full solution build clean (0 warnings, 0 errors) across net461/net472/netstandard2.0/net8.0/net10.0;
  `eng/public-api.py --check` passes.
