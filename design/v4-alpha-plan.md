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
- [ ] Sentinel (D2.7). Deliberately last in gate 1; may move to after the alpha.

### Gate 2

- [ ] **Logging - measure first.** Capture which event ids fire on a shipped connect, a reconnect, a
      failover and a maintenance window, then the same under the engine flag; the diff is the list.
- [ ] Logging - port the ~33 events whose only callers are in deleted files (`ResultProcessor` 15,
      `PhysicalConnection` 11, `PhysicalBridge` 7), using the same generated methods.
- [ ] Logging - convert the interpolated `LogInformation($"...")` calls added in 94d6fdb3 to generated events.
- [ ] Logging - re-type events whose signatures take `PhysicalBridge`/`Message`, preserving event ids.
- [ ] `IServer` methods still on `Message`: `ClusterNodes`, `ClusterNodesRaw`, `ConfigSet`, `Execute`,
      `ReplicaOf`, `ScriptLoad`, `Time`. Same shape as the `CLIENT KILL` port (a103e68e).
- [ ] Side-effects not yet written by the new core: `RunId`, `MultiDatabasesOverride`, `SetLatency`,
      `SubscriptionCount`. Confirm the 16 "ported" ones are writes, not just name matches.
- [ ] Profiling: `CommandTrace` / `ProfiledCommand` are built on `Message` and are public.

### Gate 3

- [ ] Decouple the 137 first-layer errors in surviving files: `ServerEndPoint` 34, `RedisServer` 20,
      `ConnectionMultiplexer` 19, `LoggerExtensions` 10, `ExceptionFactory` 9, `RedisBase` 7, profiling,
      the rest. Expect another layer of roughly half that once these are fixed.
- [ ] Delete. Build. Suite.

## Decisions that need the user

Each has a default the work proceeds on; none of them blocks anything.

1. **`LoggerTests.BasicLoggerConfig`** asserts more than 30 log lines during connect, calibrated against two
   bridges per endpoint. *Default:* left failing until the logging work in gate 2 lands, then re-measured -
   the generated events may close the gap honestly.
2. **`DefaultOptionsTests` Vanilla/Azure-RESP3 still see one extra socket** under the engine flags: an
   endpoint whose protocol is not yet known gets a dedicated subscription socket, and keeps it (the
   documented trade-off in `SubscriptionEndpoint`). *Default:* accept it for the alpha; revisit by
   closing the dedicated socket once RESP3 is confirmed.
3. ~~`SlotLessNodesAreKnownButNotConnected` racy~~ - resolved at landing: the test no longer activates the
   node it is asserting on, and eager connect no longer dials inert nodes.
4. **Alpha before or after gate 3.** *Default:* proceed through gate 3; flag the moment gate 2 is done.

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

- Engine flags only: **3** (the known retry-policy rotators and Envoy). Coupled: 7 / 13 / 9 over three runs,
  every consistent failure on the triaged list. Shipped green.
- Fixed this session, each verified across all three configurations: `CLIENT KILL` port; topology
  publishing and one shared `CLUSTER SLOTS` parser (the shipped interactive bridge no longer dials);
  failure detection (`ConnectionFailed`, `OnRepeatedConnectFailure`, `LastException`); NOAUTH
  conversion; a reply desync on confirmations; a reconfiguration storm at dead nodes; and the
  solicited-unsubscribe desync behind the sharded and retirement tests.
- Compiler probe: 335 error sites, 137 of them real decoupling (measured at f7f36406).
