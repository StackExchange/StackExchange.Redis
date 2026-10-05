# V4 alpha: retiring the old core

The working plan. `message-core-replacement.md` is the history of *why*; this file is *what next*, kept
current as items close. Whoever picks this up - a later session, after a context reset, after a power cut -
should be able to start from the first unchecked item without asking anyone.

## Goal

Delete `Message`, `ResultProcessor`, `PhysicalBridge`, `PhysicalConnection` and `RedisDatabase` - **once the
new core works without them**, which means three things a test suite can miss, not just green tests:

1. **Recovery** - reconnect, backlog, resubscribe, redirects, maintenance, failure events.
2. **Side-effects** - every piece of state a reply writes back onto the server or connection.
3. **Logging** - the generated `[LoggerMessage]` events, with their event ids preserved, because people
   filter and alert on them.

## The three gates

| gate | done when | measured by |
|---|---|---|
| **1. The coupled move works** | the coupled patch is committed as the behaviour under the engine flag | full suite under both flags, patch applied |
| **2. Capability parity** | every item in the inventory below is ticked | the inventory, plus an event-id diff between cores |
| **3. Decouple and delete** | the old core is gone and everything builds and passes | the compiler probe reaches zero, then the suite |

An alpha can ship after gate 2 with the old code present but unreachable, if the date matters more than the
deletion; that is the user's call, not this plan's.

## Work queue

In order. The first unchecked item is the next action; the order puts the largest unknown first.

### Gate 1

- [x] ~~MOVED to the same endpoint~~ - already fixed in `75065917`; all 22 `MovedUnitTests` pass under the
      coupled patch. This item was written from a memory note a week out of date.
- [x] **Reply desync on RESP3** - a confirmation push could answer an unrelated command. Guarded (3941e4fa, 9ak).
- [x] **Reconfiguration storm at a dead node** - ~1,200 reconfigurations/s; outstanding work thousands → 0 (9al).
- [x] **The reply to our own unsubscribe was read as unsolicited** - a desync, and the one root behind both
      `ClusterShardedTests.KeepSubscribed*` and `RetirementUnderMaintenanceTests` (9am). Pushes now match
      the command at the head of the queue; `RemoveIncorrectRouting` asks this core's map.
- [x] ~~Does `Protocol = Resp2` reach the handshake?~~ Yes - the "RESP3 frames in a RESP2 run" were two
      interleaved runs; counted by connection, the sets are disjoint (9am).
- [x] **`ConnectionRestored`**: `OnNewCoreConnected` moved into committed code with the landing.
- [x] **Land the coupled patch** as the committed engine-flag behaviour. "Coupled" and "flag-only" are now
      the same configuration and the patch file is retired. Landing fixes: eager connect skips inert
      cluster nodes (`RespTopology.ServesAnySlot`); the socket-count and slot-less tests re-baselined.
- [x] Sentinel (D2.7): the 14 `IServer` sentinel methods run on the context (`SentinelCommands`), so a
      sentinel is no longer asked through a shipped bridge. Two recovery bugs found on the way: a retired
      server's new-core executor reconnected and resurrected it (`RespNewCore.RetireEndpointAsync`), and a
      restore-triggered primary switch that failed stopped the retry timer and never re-armed it.

### Gate 2

- [x] **Logging - measured** with a connect + simulated failure + reconnect, event ids counted per core,
      stable over two pairs. Before the port the engine path was missing 31 shipped ids; after it, 9, each
      a deliberate difference: 35 (reconfigure after a plain socket loss - the engine flag does not),
      57/58/69/70 (bridge-pipeline mechanics), 75/76/84 (INFO/HELLO facts the new core learns from a
      different command, logged under that command's id), and 71 (see decisions).
- [x] Logging - ported under the shipped ids: connect lifecycle (92/95/110/100/96/87), TLS (98/99) in the
      shared transport factory, connect failed / lost / requested close (93/89/88), resurrecting (91,
      re-typed from `PhysicalBridge` to its name), and the auto-configure facts (61/62/63/67/68/72-86)
      logged in the handshake where each is learned. `LoggerTests` now passes under the engine flags.
- [x] Logging - the interpolated calls are generated events (124-126 for topology refresh; the rest
      replaced by shipped ids).
- [x] Side-effect found by the port: the new core never read `CONFIG GET timeout`, so `WriteEverySeconds`
      kept its default under the engine flag. Now read, with the shipped rule.
- [x] `IServer` methods off `Message` (non-Sentinel): `ClusterNodes`/`ClusterNodesRaw`/`ClusterSlots`
      (keeping the `SetClusterConfiguration` side-effect), `ScriptLoad` (keeping `AddScript`), `ReplicaOf`
      (tie-breaker removal and the prefixed config-channel broadcast), and `ConfigSet`'s read-back, now
      through one `RespHandshake.ApplySetting` rule shared with discovery. `Execute` and `Time` were already
      ported. Found on the way: `KeepAlive` gates whether discovery *asks* for `timeout`, never whether a
      reply is applied (`HeartbeatTests`). The 14 Sentinel methods stay with D2.7.
- [x] Side-effects audited: `SetLatency` was real (multi-group ranking read "never measured") - now
      sampled from the handshake's `CLIENT ID` and the keep-alive PING. `RunId` is written and never
      read. `SubscriptionCount` is covered: `IsIdle` asks the registry too. `MultiDatabasesOverride`
      (#2642, Alibaba) is covered by construction: the new core sets `ServerType` only from a declared
      mode, never from the `CLUSTER NODES` inference that override guarded.
- [x] Profiling: already has its new-core door (`ProfiledCommand.SetOperation`, populated by the
      endpoint executor, MOVED included), and `SLOWLOG GET` already parses through the context. What is
      left is deleting the `Message` door and `CommandTrace`'s nested processor - gate 3 work.

### Gate 3

Re-probed at 836f138a: **221 distinct error sites** (930 errors) from deleting `Message*`,
`ResultProcessor*`, `PhysicalBridge*`, `PhysicalConnection*`, `RedisDatabase*`. Of them, 84 die with the
deletion (old-core helpers: `RespMessageExecutor`, the old `RedisBatch`/`RedisTransaction`, per-command
`*Message`/`*ResultProcessor` files); 46 are old-core internals nested in surviving public types
(`Condition`, `CursorEnumerable`, `HashImport`, `ClientInfo`, `CommandTrace`, latency entries,
`ProfiledCommand`); 91 are real decoupling (`ServerEndPoint` 28, `RedisServer` 14,
`ConnectionMultiplexer` 13, `ExceptionFactory` 9, `RespHandlers` 5, `LoggerExtensions` 5, `RedisBase` 4, ...).

**Every one of those removals breaks the shipped configuration while it exists**, so the order is forced:

- [x] Zero the engine-flag triage list - CI sets no flags, so after the flip it inherits whatever is left.
      Done: `ReconnectRetryPolicyUnitTests` (the policy was asked before anything had failed, and with a
      failure count where it expects a retry count); `DefaultOptionsTests` x2 and `GetClients` (a real bug,
      not decision 2's trade-off: `GetConnectionId` created - and so dialled - a shipped bridge just to read
      an id); Sentinel (above). `MovedProfiling`: a real interval under TimeSpan's 100ns
      resolution, asserted strictly positive; now `>=`, as the test already allows `ResponseToCompletion`.
      Left, all load-only (0/15 each in isolation): `SubscribeToWrongServerAsync(false)` (a key-routed plain
      subscription moved to the slot owner inside the test's 50ms window), `PubSubOrderedRouted` (conn
      appears to receive one message twice - a possible duplicate subscription, worth a capture), and the stall.
- [x] **Make the old core unreachable under the engine flag.** Done: 179 -> 20, every remaining one a test driving `RedisDatabase` or bridges on purpose. Was: A probe on bridge construction across one
      full engine run counted 179 shipped bridges still created - each one dials - from: `ISubscriber.Ping`
      (71), `ServerEndPoint.OnConnectedAsync`'s activation path and its `AutoConfigureAsync` (41+42),
      `IServer.HotKeysStop` (24), `HashImport.SafeDiscardAsync` (16), `AddLibraryNameSuffix` (3),
      `GetEndpointsFromClusterNodes` (3), `MakePrimaryAsync` (1); the rest are tests driving `RedisDatabase`
      or bridges on purpose. The probe (log a stack from `ServerEndPoint.CreateBridge` when the engine flag
      is on) is the acceptance test: zero from library code.
- [x] **Flip the default**: the engine flags are on unless `SEREDIS_NEW_CORE_ENGINE=0` /
      `SEREDIS_NEW_DATABASE_SURFACE=0`. Default 0 / 2 / 0 over three net10.0 runs (two known flakes), legacy
      green. **Not verified locally: net481**, which CI runs on Windows - the new core has not been
      exercised on .NET Framework in this work, so the first CI run on this branch is the real test there.
- [x] Decouple the 91 + 46 above, then delete. Done: `Message*`, `ResultProcessor*`, `PhysicalBridge`,
      `PhysicalConnection*`, `RedisDatabase*`, `RedisBatch`, `RedisTransaction`, `RespMessageExecutor`,
      `MessageWriter`, the per-command `*Message` classes and `CursorEnumerable` are gone. What they owned
      and something still needed moved to `Protocol/RespParsers*.cs` (reply parsing), `Protocol/RespWire.cs`
      (raw RESP writing, also used by the toy server), `Protocol/AdminCommands.cs` and `ConnectionStatus.cs`
      (the status enums the logging events take - ids preserved). The flags are hard-wired on and the
      env vars ignored, so nothing can select the old core: there is no old core to select.
- [x] Logging enums: relocated to `ConnectionStatus.cs`, ids unchanged.
- [x] `SimulateConnectionFailure` demands `AllowAdmin` again, as shipped.
- [x] Delete. Build. Suite: net10.0 over three runs - see Status.
- [ ] **Gaps the deletion exposed** - each was the old core's, and has no new-core equivalent yet:
      the connection storm log (`ExceptionFactory` no longer writes one); the fire-and-forget counter (always
      reported 0); `GetProfile`'s per-connection op-count history; `DedicatedThreads` (decision 6). None is a
      correctness issue; all are diagnostics. Decide per item: port or drop (and document the drop).
- [ ] **Windows CI never had a synced replica.** Replicas there land the sync RDB on the `/mnt` (drvfs)
      mount and fail to load it, retrying forever - which the old core never noticed, because it read
      replicas from `CLUSTER NODES`; the new core reads `CLUSTER SLOTS`, which rightly omits a replica that
      has never synced, so replica routing failed on Windows only. CI now starts Windows replicas with
      `--repl-diskless-load swapdb` and fails fast unless every replica syncs and `CLUSTER SLOTS` lists all
      six nodes. Confirm on the next Windows run, and see whether `ARefusingNodeAccumulatesOnlyOurOwnTraffic`,
      the cache-churn timeouts and the 33-minute net10.0 duration were the primaries' resync storm too.

- [x] **Parked tests retargeted** (all but `DedicatedThreadsUnitTests`): the parser tests run through
      `RespExecutor.ParseFromSpan`, the round-trip tests through a capturing executor. Bugs they found, fixed:
      stream handlers swallowing malformed replies, nil VLINKS as null, TYPE nil as Unknown, scalar-as-array,
      COPY REPLACE and explicit-id XREADGROUP retry categories, SINTERCARD-family `LIMIT 0`. Accepted
      leniencies are pinned with comments in the tests (nil aggregates read as empty, sentinel/latency nils).
- [ ] **Two-core leftovers** (from the comment sweep's report; the high-confidence dead code is removed):
      - `Subscription._onNewCore` / `IsOwnedByNewCore` / `NewCoreOwnsAnySubscription` now only mean "something
        subscribed this"; collapsing them changes the never-sent case, so it needs a look, not a delete.
      - `TransitionalDatabase._fallback` and the `CanWriteRuns`/`CanWriteTransactions` gates: production always
        passes null / true; only tests supply a fallback. Retire with those tests.
      - ~30 `NewCoreIfCreated?.` null-tolerances, `ServerEndPoint.IsConnecting => false`, `GetProfile()`.
      - Topology flows both ways between `RespTopology` and `ServerSelectionStrategy` - live, because the
        selector is still the routing fallback and `IServer`'s source. Goes with "phase D".
- [x] **GC rooting of a dropped multiplexer.** v3 rooted the multiplexer from its heartbeat while caller work
      was in flight; nothing called `Root()` once the bridges went, and the dead mechanism is removed. Not
      needed: `DroppedMultiplexerTests` drops a multiplexer mid-command against a server holding every reply,
      and the multiplexer stays reachable (the connection roots it), so the heartbeat runs and the command
      times out at its own 500ms.

### Backlog (after the alpha gates; not blocking)

- [ ] **Trusted-callback completion mode - an experiment, then maybe an opt-in.** Respire, a new multiplexed
      .NET client, posts higher numbers in the bench (`redis-developer/ClientBench`, checked out at
      `~/code/RespFest`; added in f8ef921, measured in d23d45d). Hypothesis: it completes callers *inline on the
      reader*, trusting continuations to be quick, with a watchdog to catch one that blocks - great when it
      holds, dangerous when it does not (a slow or blocking continuation stalls every reply behind it). Aims:
      (a) **prove or refute the hypothesis** by adding the same mode here behind a config option or feature
      flag and measuring it in the bench against the current behaviour; (b) if it holds, **offer it opt-in**,
      default off, with the watchdog and with documentation that is plain about the failure mode. Read
      Respire's completion path first to confirm the mechanism rather than inferring it from the numbers.

## Decisions that need the user

Each has a default the work proceeds on; none of them blocks anything.

1. ~~`LoggerTests.BasicLoggerConfig`~~ - resolved by the logging port; passes under the engine flags.
2. ~~`DefaultOptionsTests` extra socket~~ - not a trade-off after all: asking `GetConnectionId` created a
   shipped bridge, which dialled. Fixed; no decision needed.
3. ~~`SlotLessNodesAreKnownButNotConnected` racy~~ - resolved at landing: the test no longer activates the
   node it is asserting on, and eager connect no longer dials inert nodes.
4. **Alpha before or after gate 3.** *Default:* proceed through gate 3; flag the moment gate 2 is done.
5. **Event 71, `Response from {Bridge} / {Command}: {Result}`** - the shipped path dumps every
   handshake reply at Information. *Default:* not ported; each fact those replies carry has its own
   auto-configure event, and a raw reply dump belongs at Debug if anywhere.
6. **`DedicatedThreads` (opt-in feature flag) is not implemented by the new core** - only `PhysicalConnection`
   has it. Its tests passed under the engine flag only by inspecting a shipped bridge built on the side.
   *Default:* the alpha ships without it; the tests skip under the engine flag and say why. Porting it means
   dedicated reader/writer threads in the RESPite transport.

## Operating rules

Learned the expensive way; see `message-core-replacement.md` 9aa-9aj for the incidents behind each one.

- **Three configurations, every time.** Shipped (no flags), engine flags only, and coupled (patch
  applied). Two green configurations can bracket a broken third (9ad).
- **Bisect, don't reason.** When a mechanism is in doubt, remove the suspect and re-run. Three wrong
  diagnoses in one afternoon each cost a build cycle; the bisects took two minutes each.
- **One run is an anecdote.** Any number used to justify a change gets at least five runs (9aj).
- **Read the assertion text before forming a theory.** It usually says the answer plainly.
- **`--no-build` only after building the project being run**, never just a project it references (9ad).
- **Never grep or kill by a pattern your own command line contains** (`pkill -f`, `pgrep -f`).
- **Commit verified work promptly**; scratch state does not survive a power cut. The coupled patch lives
  at `~/code/coupled-9p.patch`, outside `/tmp`.
- **Never end a turn idle.** End it only with a background task pending (its notification resumes the
  work), with the goal met, or with a question only the user can answer. A progress summary is not a
  reason to stop.

## Status

- **Old core deleted.** net10.0 over three runs after the deletion: 1 / 2 / 3 failures, each a one-off
  from the known-flake list (`SubscribeToWrongServerAsync(false)`, `SweepIfDueHonoursTheInterval`, a
  `MovedUnitTests` reconnect case). The `NewCore*Tests` re-run suites went with it - they built a second core
  beside each multiplexer, doubling the runs and colliding with their own originals on shared databases.
  Suite is now 8,330 tests. Multi-TFM analyzer build clean.
- **Open flake, engine flag only so far:** occasional ~5s windows where a shared RESP3 connection to 6379
  gets no replies (outbound queued, inbound 0), timing out whatever was in flight - 52 tests once, 2 once,
  otherwise absent over ~8 runs. `AsyncTimeoutIsNoticed`'s `CLIENT PAUSE` was the first suspect, but it is
  in the non-parallel collection. Needs a capture of what the connection was doing, not more guessing.
- Engine flags only: **3** (the known retry-policy rotators and Envoy). Coupled: 7 / 13 / 9 over three runs,
  every consistent failure on the triaged list. Shipped green.
- Fixed this session, each verified across all three configurations: `CLIENT KILL` port; topology
  publishing and one shared `CLUSTER SLOTS` parser (the shipped interactive bridge no longer dials);
  failure detection (`ConnectionFailed`, `OnRepeatedConnectFailure`, `LastException`); NOAUTH
  conversion; a reply desync on confirmations; a reconfiguration storm at dead nodes; and the
  solicited-unsubscribe desync behind the sharded and retirement tests.
- Compiler probe: 335 error sites, 137 of them real decoupling (measured at f7f36406).
