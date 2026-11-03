# v3 → v4 behaviour audit (beyond the intended API changes)

An independent audit of observable behaviour differences between `main` (v3, at `9d17974f`) and `v4`
(at `b93041c0`), for release-note purposes. 2026-11-03.

**Method.** All 282 modified files were reviewed (127 of them test files — a changed test is a changed
expectation), plus the 51 deletions (a deleted test is possibly lost coverage) and the modified shipped
sources; suspicious claims were verified against the v4 source, and both branches ran the full net10.0
suite against the same local server (the CI-default `client-libs-test` image, 8.9.241). The intended
v4 changes — the new RESPite-based core, the grouped command surface (`db.Strings.*`), client-side
caching, leases, the `IRespTarget` interface-base additions — are taken as read and not re-listed here.

**Empirical headline:** the two branches' full suites, run against the same server, fail on exactly the
same tests (zero divergent failures); v4 runs ~1,270 more tests than v3, all new coverage.

---

## Release-note candidates

Ordered roughly by how likely a v3 user is to notice.

### Connection lifecycle

- **Connections are dialled lazily and asynchronously.** The RESP2 subscription socket is no longer
  opened inline before `Connect` returns: with a configuration channel it is dialled in the background
  (and `ConnectAsync` waits for it); without one, not until the first subscribe. Endpoints in general
  dial on demand, and nothing is queued at a node already known to be down. Observable: fewer
  connections in `CLIENT LIST` after connect; `GetConnectionId(ep, ConnectionType.Subscription)` can be
  null right after connect, or answer with the interactive connection's id; a first command against a
  `FailFast` multiplexer is deliberately allowed to attempt a connect rather than be refused;
  `IsConnected` for a never-needed endpoint can answer false where v3 answered true.
- **A refused AUTH no longer reports the multiplexer as disconnected.** v3: `IsConnected == false`
  after connecting with a bad password. v4 keeps the connection with the refusal recorded, and commands
  fail individually with the server's error. Code that gates on `IsConnected` after `Connect` with bad
  credentials behaves differently.
- **`ConnectionFailureType.SocketClosed`** is now reported when the *server* closes a connection
  (`CLIENT KILL`, idle timeout); v3 reported `SocketFailure`/`InternalFailure`. Anyone switching on
  `FailureType` sees a new value.
- **Fire-and-forget ordering is weaker across connections.** F&F promises the command is *sent*, as it
  always did — but in v4 a subsequent command can travel on a different connection (another database,
  an `IServer` command such as `SCAN`/`DBSIZE`/`SWAPDB`), so the v3 accident of "same socket ⇒ implicit
  ordering" is gone. Code that F&F-writes and immediately reads through a different route can see stale
  state. (F&F followed by a command on the *same* route still behaves as in v3.)

### Values and parsing

- **Nil array replies now come back as empty arrays, not null** — uniformly across the array handlers
  (string/value/key/double/hash-entry/sorted-set-entry/geo/latency/…). **In practice this is almost
  unobservable against genuine Redis**, which answers these commands with *empty* arrays, not nil, for
  missing keys (verified live: SMEMBERS/SORT/S·Z·HRANDMEMBER-with-count/GEOPOS/LATENCY all send `*0`;
  the HEXPIRE family answers per-field `-2`): v3 already returned empty arrays there, so `== null`
  checks on them were already dead code, and the missing-vs-empty distinction was never available
  through these APIs. The two commands that do send nil were already normalised or are untouched:
  XREAD's no-data nil was mapped to an empty array by v3 itself, and EXEC's nil is the transaction
  machinery (unchanged). `RedisResult` (scripts, `Execute`) keeps `IsNull` and null casts; inner
  element nils (MGET) remain `RedisValue.Null`; deliberately-nullable surfaces (multi-move, leases)
  keep null. The residual exposure is third-party RESP servers/proxies/modules that answer `*-1`
  where Redis answers `*0` — those now read as empty instead of returning null into a non-nullable
  `T[]` (in v3 usually a latent NRE at the iteration site), which is the fail-safe direction. Worth a
  release-note line mainly for contract honesty: behaviour now matches the declared non-nullable
  arrays.
- **`SentinelGetReplicaAddresses` returns an empty array instead of throwing** when a primary has no
  replicas (arguably a v3 bug).
- **Protocol-mismatch errors surface as a different exception.** v3 threw `RedisConnectionException`
  with "Unexpected response to {COMMAND}"; v4 surfaces the parse failure as the operation's own fault
  (not a connection-typed exception). Catch-blocks keyed on `RedisConnectionException` for malformed
  replies will not match. Relatedly, "OK"-demanding commands (`LSET`, `LTRIM`, `RESTORE`, …) now accept
  any non-error reply rather than demanding the literal `OK`.
- **`PTTL`/`PEXPIRETIME`-style nil replies now fail** rather than mapping to null (those commands answer
  `-1`/`-2`, never nil; only a non-conformant server notices). Several other reply-shape strictness
  changes are of the same character: visible only against non-conformant servers/proxies.

### Wire-visible changes (MONITOR, commandstats, ACLs, proxies)

- **Deprecated commands are respelled where the server supports it** (version-gated, with fallback):
  `StringGetSet` → `SET … GET`; `ListRightPopLeftPush` → `LMOVE`; all-read `BITFIELD` → `BITFIELD_RO`.
  Semantics of the old methods are pinned by tests (GETSET's null/TTL behaviour survives). The sharp
  edge: **an ACL permitting the old command but not its replacement now fails.**
- **The periodic role check sends `ROLE`, not `INFO replication`** (`ConfigCheckSeconds`, keep-alive and
  sentinel recovery). Same ACL/command-map caveat.
- **Script evaluation pairs `SCRIPT LOAD` with `EVALSHA` in one write** when the server is not known to
  hold the script (v3 sent `EVAL` first, `EVALSHA` thereafter); inside a batch/transaction the body is
  sent as `EVAL`. NOSCRIPT recovery is unchanged.
- **An empty transaction sends nothing** (v3 sent `PING` and demanded `PONG`).
- **`Execute()` sends an unknown command name verbatim** — v3 upper-cased it. Case-insensitive to Redis,
  visible to case-sensitive proxies/tooling.
- **`IdentifyEndpoint` no longer sends a command** (answered from routing state).
- Topology refresh issues `CLUSTER SLOTS` alongside `CLUSTER NODES` (graceful fallback either way);
  `ReplicaOf` additionally broadcasts on the configuration channel so other clients re-read topology.
- `HIMPORT` imports inject at worst one redundant, idempotent `PREPARE` per connection.

### Exceptions and diagnostics

- **Timeout/connection exception text changed.** The new core has no socket-poll/pipe counters, so
  `in:`, `in-pipe:`, `out-pipe:`, `last-in:`, `cur-in:` are omitted for its endpoints, as are the
  `Message-Current`/`Message-Next` (active/next) fields and the `OutboundDeltaKB`/`InboundDeltaKB`
  block; `outbound=`/`inbound=` figures appear instead. The stable prefix is `inst: qu: qs: aw: bw:`.
  Support tooling that parses timeout messages will notice.
- **Connect failures carry more detail**: with no explicit reason, the per-endpoint `LastException` is
  chained as `InnerException` and its innermost message appended (v3's bare "Error connecting right
  now" told the user nothing).
- **One argument-validation tightening**: `StreamAdd` (`XADD`) with an explicit `MessageId` *and* an
  idempotent id is refused up front (v3's message builder silently let `*` through).

### Profiling

- **`IProfiledCommand.Command` reports what was actually sent** — a cached script profiles as
  `EVALSHA` where v3 always said `EVAL`. (v3 already did this for e.g. `KeyDelete`→`UNLINK`; v4 is the
  consistent behaviour.) Order and count of profiled commands are unchanged.
- **`EnqueuedToSending` can be exactly zero** on a warm connection (no queue between stamp and write).

### API-surface edges beyond the intended changes

- **`RespReaderExtensions.ReadLease` is `[Obsolete]` and now always copies** (it used to share/pin the
  reply buffer when contiguous); `reader.ReadLease()` source-resolves to a new extension returning
  `ReadOnlyLease<byte>`. Binary-compatible; the zero-copy perf contract of the old method is gone
  (a mutable lease must not alias shared cache memory — design notes 6.16).
- **`ReadOnlyLease<T>` exposes no `ArraySegment`** (no way to reach outside the lease); read via
  span/stream/decode. Leases can be windows onto a shared receive buffer rather than copies.
- **`RedisKey`/`RedisValue` byte hash codes changed** (the old multiply-by-33 fold collided badly;
  now a 64-bit mix + Murmur3 finalizer). Hash codes were never a persistence contract, but anything
  that stored them breaks.
- **New `SEREDIS_<FLAG>` environment overrides** for all feature flags — including pre-existing
  `PreventThreadTheft`/`DedicatedThreads`, which the environment could not touch in v3. New flag
  `InlineSends` is **on by default** (a caller alone in flight writes its own bytes);
  `SingleReadLoop`/`CombineWrites` exist and default off.
- `CommandFlags` gains `CacheOptIn`/`CacheOptOut` (additive; nothing renumbered). New
  `ConfigurationOptions.ClientCache` (default null — nobody acquires a cache by upgrading; not part of
  the connection string) and internal `ConnectMode` (default `Eager`, explicitly keeping v3's
  dial-everything semantics).
- On netfx, `CommandFlags.ToString()` for the zero-adjacent new names differs cosmetically from modern
  TFMs (Enum.ToString quirk).

### Cluster

- **A key-routed channel subscription may be proactively re-homed to the slot owner**, even when the
  subscribe named a specific server — v3 pinned it to the server you sent it to.
- A node the client has dialled and found unreachable accumulates nothing (v3 queued probe traffic on
  its backlog); resubscription away from a dead node waits for the in-flight attempt to fail (~instant
  on Linux, up to a connect-timeout on Windows).

### Known gaps (as of this audit)

- **The new core has no proxy (`Proxy.Twemproxy`) handling yet** — stated in the code as "a real gap".
- The nine most recent `main` commits (up to `9d17974f`) are not yet merged into v4 (normal under the
  absorb-by-merging policy; at least #3330 has a v4-native port already, #3332).

---

## Verified unchanged (deliberately checked, worth stating in release notes as "still true")

- **Transactions/batches**: an aborted transaction returns `false` (no throw) with queued tasks
  `Canceled`; a failing inner operation in a committed `MULTI`/`EXEC` surfaces `RedisServerException`
  on that operation's task; condition semantics (`WasSatisfied`) unchanged; transactions and batches
  remain reusable after execute, with conditions judged per run.
- **Command retry categories** are identical for every pinned command (SET family, SORT/STORE, ZADD,
  XADD, XREADGROUP, EXPIRE, GETEX, COPY, scan cursors, …).
- **Wire bytes of every retained round-trip expectation** (BITFIELD token order, BITPOS, LMOVE-multi,
  XADD/XREAD shapes, SINTERCARD LIMIT handling, …) are unchanged.
- **Config-string parsing, option defaults, enum values, `RedisValue` conversion/equality**: clean —
  no new tokens, no default changes, no renumbering. `PublicAPI.Shipped.txt` loses nothing.
- Timeout exceptions keep the core diagnostic payload (`qu/qs/aw/bw`, `last-in`-replacement figures,
  `Redis-Last-Result-Bytes`, "async timeouts" counters); `RedisFeatures` gating of commands unchanged.
- NOSCRIPT recovery; LockTake/Extend/Release semantics; pub/sub ordering guarantees; keyspace-prefix
  behaviour; sync-vs-async parity.
- Handshake wire-economy: the cluster-mode tie-breaker `GET` suppression and the "discover server
  config only when undescribed" gate are preserved in `RespHandshake` (verified in source — but see
  coverage gaps: no test pins the former any more).

## Suite comparison (2026-11-03)

Both branches, full `net10.0` suite, same server, same machine, sequential: **both fully green** —
v4 8,136 tests / 0 failed / 167 skipped; main 6,866 / 0 / 163. (An earlier run of the day failed
`ScanTests.SortedSetScanLarge` ×8 on *both* branches equally — that was a leftover 8.12-rc2 container
with the btree ZSCAN duplicate issue, redis/redis#16158, not a branch difference; the rerun on the
CI-default image, 8.9.241, is clean.) v4's three most recent Windows
CI failures (`SlotLessNodesAreKnownButNotConnected`, `RetryTransactionFireAndForgetSharesOneCompletedTask`,
`ADeferredWalkAgainstMaterialiseThenRead`) did not reproduce on Linux and are CI-health items, not
v3/v4 behaviour deltas.

## Test-coverage debts (behaviour believed fine; the pin is gone)

Regression tests born from production incidents that died with the old core and were not re-pinned
end-to-end. Each is a candidate ticket, not a known bug:

1. **#2919 — no misrouted replies when write teardown itself fails.** The
   `ConnectionMultiplexer.BeforeWriteTeardown` seam still exists (its comment still cites #2919 and the
   deleted `PhysicalConnection.PoisonWrite`) but nothing uses it — re-pinning looks intended and
   unfinished. Frame-level integrity is covered structurally (`InterpolatedWriterFragmentTests`,
   reply-pairing in the test executor), not the cross-caller misroute scenario.
2. **#3282 — mid-serialization batch write failure**: the every-command-completes half is covered
   (`ConnectionDroppedExceptionTests`); the fault-injected-mid-batch half (written vs unwritten, under
   both backlog policies) is not.
3. **Cluster tie-breaker probe suppression** (and the one-unslotted-first-round residue of #2970):
   preserved in source, unpinned by tests.
4. **`RequestBufferPool` rents-vs-returns accounting over live traffic** (the old
   `RenderedArgsLeakTests`); the pool is still consumed (`RespTransportFactory`), only property
   round-trip is tested. A sibling bug (the configured *response* pool being silently ignored) was
   caught and fixed in-branch by a strengthened test — this is exactly the class of regression to pin.
5. **Caller-array aliasing across deferred batch `ExecuteResp` calls** — guaranteed by construction
   (arguments render to bytes at call time), near-covered, no exact end-to-end twin.
6. **"Ordinary closure never raises `InternalError`"** (Issue3167): the failing path is gone, but the
   contract on the still-public event is unpinned.
7. **Per-subcommand retry categories** (MEMORY PURGE, SLOWLOG GET, CONFIG GET): overrides verified
   present in source; only CLUSTER NODES keeps a direct test.
8. **`BeforeSocketConnect` effect**: the test now only counts invocations; verified in source that the
   callback runs against the real socket pre-connect (`RespTransportFactory`), with the original
   endpoint. The old writer's short-`GetSpan` torture tests are moot by construction (the new
   serializer owns its buffers and never consumes an external `IBufferWriter`).
9. **Per-command failure-annotation exception text** (old `ExceptionFactoryTests.MessageFail`): format
   unpinned; the type contract (`RedisConnectionException`, transport fault as inner, never a raw
   cancellation) is re-pinned in `ConnectionDroppedExceptionTests`.

## Fixed alongside this audit

- Stale "SUSPECTED REGRESSION … skipped" comments in `ResultProcessorUnitTests/BasicScalars.cs`
  (TYPE nil/empty → `None`) and `ResultProcessorUnitTests/BasicAggregates.cs` (scalar-as-array) —
  both behaviours are fixed in `RespHandlers` and the tests run (and pass) at the shipped expectation.
- `TestBase.GetDatabase` doc comment claimed `StringTests` overrides it onto the new RESP surface;
  no override exists yet.
