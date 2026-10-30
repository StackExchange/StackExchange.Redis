# Command coverage

Which server commands StackExchange.Redis does not expose as typed APIs, and why. Anything not listed here is either implemented or disposed of by one of the rules below. Any command, listed or not, can be sent with [`Execute`/`ExecuteAsync`](Execute).

Upstream: redis/redis@6a5fee67583480de1fec323649f43fd000001b49 (`src/commands`, unstable, 2026-10-30). Maintained by the `audit-command-coverage` skill under `.claude/skills/`, which re-diffs this file against upstream and reports only what changed.

This supersedes the hand-maintained list in [#2055](https://github.com/StackExchange/StackExchange.Redis/issues/2055).

## Rules

These dispose of a command without an entry here:

- **`HELP` subcommands** (`CLUSTER HELP`, `XINFO HELP`, ...) are for humans at a console; not applicable.
- **Server-internal commands** (`SYSCMD` in the upstream `doc_flags`: `REPLCONF`, `RESTORE-ASKING`, `PFDEBUG`, `BITCONVERT`, ...) are not for clients; not applicable.
- **Deprecated upstream** (`deprecated_since` set) is never added. Rows below list them for reference only.
- **Blocking commands** (`BLOCKING` in the upstream `command_flags`) are a policy exclusion: a command that parks the connection stalls every other caller sharing the multiplexer. See [Pipelines and Multiplexers](PipelinesMultiplexers). Use pub/sub, polling, or a dedicated connection via `Execute` if you need one.

House rules applied by hand:

- **Second-precision read twins.** Where the server offers `XTTL` and `XPTTL` (or `XEXPIRETIME`/`XPEXPIRETIME`) from the same version, the library reads via the millisecond form only and converts. The seconds/milliseconds choice elsewhere (`TTL` vs `PTTL`) exists because of a server version delta; there is none here.
- **Cluster slot and shard administration** (adding, moving, trimming, flushing slots; node membership) is operator tooling, not client surface.
- **Module-shipped commands** (vector sets, search, JSON, time series, probabilistic) are specified in their module repositories, not in `redis/redis`; the ones the library exposes are listed as enum-only at the end.

Statuses: `policy` (excluded by design), `not-applicable` (not meaningful for a client library, or covered by an existing API), `deferred` (wanted, but needs a design conversation first), `todo` (accepted gap, a PR is welcome), `new` (upstream added it, no decision yet), `deprecated`, `enum-only`, `implemented` (exposed via a hand-written message the audit script cannot see; the row vouches for it).

## Policy: blocking

| Command | Since | Status | Notes |
|---|---|---|---|
| `WAIT` | 3.0.0 | `policy` | Blocks the connection until replicas acknowledge; same multiplexer-stall problem as the blocking pops. |
| `WAITAOF` | 7.2.0 | `policy` | As `WAIT`, for AOF fsync. |
| `BLMOVE` | 6.2.0 | `policy` | Blocking; `ListMove` covers `LMOVE`. |
| `BLMOVEM` | 8.10.0 | `policy` | Blocking; the non-blocking `LMOVEM` is implemented. |
| `BLMPOP` | 7.0.0 | `policy` | Blocking; `ListLeftPop`/`ListRightPop` cover `LMPOP`. |
| `BZMPOP` | 7.0.0 | `policy` | Blocking; `SortedSetPop` covers `ZMPOP`. |
| `BZPOPMAX` | 5.0.0 | `policy` | Blocking; `SortedSetPop` covers `ZPOPMAX`. |
| `BZPOPMIN` | 5.0.0 | `policy` | Blocking; `SortedSetPop` covers `ZPOPMIN`. |

## Not applicable: omitted on purpose

| Command | Since | Status | Notes |
|---|---|---|---|
| `HTTL` | 7.4.0 | `not-applicable` | Second-precision twin of `HPTTL`; the library always reads `HPTTL`. |
| `STTL` | 8.12.0 | `not-applicable` | Second-precision twin of `SPTTL`, as `HTTL`. |
| `RESET` | 6.2.0 | `not-applicable` | Discards the per-connection state the multiplexer negotiated (`HELLO`, `CLIENT SETNAME`, `SELECT`, subscriptions). Dispose and reconnect instead. |
| `CLIENT REPLY` | 3.2.0 | `not-applicable` | Suppressing replies breaks the request/response pairing the multiplexer relies on; `CommandFlags.FireAndForget` is the client-side equivalent. |
| `CLIENT UNBLOCK` | 5.0.0 | `not-applicable` | Unblocks a client parked in a blocking command, which this library does not issue. |
| `CLIENT GETNAME` | 2.6.9 | `not-applicable` | The library sets the name itself (`ConfigurationOptions.ClientName`) and knows it; `ClientList` shows every connection's name. |
| `CLIENT INFO` | 6.2.0 | `not-applicable` | The current connection's own `CLIENT LIST` row; connections are an internal detail of the multiplexer, and `ClientList` covers the server-wide view. |
| `HIMPORT DISCARDALL` | 8.10.0 | `not-applicable` | Drops every field-set on the connection. The library tracks field-sets individually and discards each on dispose; a blanket discard on a shared connection would invalidate other callers' live field-sets. See [HImport](HImport). |
| `SCRIPT DEBUG` | 3.2.0 | `not-applicable` | Interactive Lua debugger session for `redis-cli`; needs a dedicated connection and a REPL. |
| `CLIENT CACHING` | 6.0.0 | `not-applicable` | Only meaningful with `CLIENT TRACKING` in `OPTIN`/`OPTOUT` mode; the v4 cache owns that decision per connection. |
| `CLIENT GETREDIR` | 6.0.0 | `not-applicable` | Introspection of `CLIENT TRACKING` redirection; the v4 cache knows its own redirection. |
| `LOLWUT` | 5.0.0 | `not-applicable` | Art. `Execute("LOLWUT")` works. |
| `LATENCY GRAPH` | 2.8.13 | `not-applicable` | ASCII-art output for a console; `LATENCY HISTORY`/`LATEST` are exposed. |
| `XIDMPRECORD` | 8.6.2 | `not-applicable` | Internal: sets idempotent-producer metadata on an existing entry; `XADD` with IDMP options is the client path. |
| `PSYNC` | 2.8.0 | `not-applicable` | Replication protocol between servers. |
| `XSETID` | 5.0.0 | `not-applicable` | Internal: upstream describes it as a replication command for stream state. |
| `FAILOVER` | 6.2.0 | `not-applicable` | Operator-driven coordinated failover; `IServer.MakePrimaryAsync` covers the client-side story. |
| `SFLUSH` | 8.0.0 | `not-applicable` | Flush a slot range (experimental); cluster slot administration. |
| `TRIMSLOTS` | 8.4.0 | `not-applicable` | Deletes keys left in slots a node no longer owns after atomic slot migration; cluster slot administration. |

## Not applicable: server and cluster administration

Operator tooling. All of it works through `Execute` on an `IServer`; none of it is on the typed surface.

| Command | Since | Status | Notes |
|---|---|---|---|
| `CLUSTER ADDSLOTS` | 3.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER ADDSLOTSRANGE` | 7.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER BUMPEPOCH` | 3.0.0 | `not-applicable` | Cluster administration. |
| `CLUSTER COUNT-FAILURE-REPORTS` | 3.0.0 | `not-applicable` | Cluster administration. |
| `CLUSTER DELSLOTS` | 3.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER DELSLOTSRANGE` | 7.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER FLUSHSLOTS` | 3.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER FORGET` | 3.0.0 | `not-applicable` | Node membership. |
| `CLUSTER MEET` | 3.0.0 | `not-applicable` | Node membership. |
| `CLUSTER MIGRATION` | 8.4.0 | `not-applicable` | Atomic slot migration control. |
| `CLUSTER REPLICATE` | 3.0.0 | `not-applicable` | Cluster administration. |
| `CLUSTER SAVECONFIG` | 3.0.0 | `not-applicable` | Cluster administration. |
| `CLUSTER SET-CONFIG-EPOCH` | 3.0.0 | `not-applicable` | Cluster administration. |
| `CLUSTER SETSLOT` | 3.0.0 | `not-applicable` | Slot administration. |
| `CLUSTER SYNCSLOTS` | 8.4.0 | `not-applicable` | Internal to atomic slot migration between nodes. |
| `CLUSTER MYID` | 3.0.0 | `not-applicable` | Node identity; `ClusterConfiguration` carries node ids from `CLUSTER NODES`. |
| `CLUSTER MYSHARDID` | 7.2.0 | `not-applicable` | As `CLUSTER MYID`. |
| `CLUSTER KEYSLOT` | 3.0.0 | `not-applicable` | The client computes slots locally (`HashSlot`). |
| `CLUSTER COUNTKEYSINSLOT` | 3.0.0 | `not-applicable` | Slot administration (resharding tooling). |
| `CLUSTER GETKEYSINSLOT` | 3.0.0 | `not-applicable` | Slot administration (resharding tooling). |
| `SENTINEL CKQUORUM` | 2.8.4 | `not-applicable` | Sentinel administration; the client consumes sentinel via `GET-MASTER-ADDR-BY-NAME`, `SENTINELS`, `REPLICAS`, `MASTERS`. |
| `SENTINEL FLUSHCONFIG` | 2.8.4 | `not-applicable` | Sentinel administration. |
| `SENTINEL INFO-CACHE` | 3.2.0 | `not-applicable` | Sentinel internals. |
| `SENTINEL IS-MASTER-DOWN-BY-ADDR` | 2.8.4 | `not-applicable` | Sentinel-to-sentinel protocol. |
| `SENTINEL MYID` | 6.2.0 | `not-applicable` | Sentinel administration. |
| `SENTINEL PENDING-SCRIPTS` | 2.8.4 | `not-applicable` | Sentinel administration. |
| `SENTINEL SIMULATE-FAILURE` | 3.2.0 | `not-applicable` | Sentinel testing hook. |
| `SENTINEL CONFIG` | 6.2.0 | `not-applicable` | Sentinel administration. |
| `SENTINEL DEBUG` | 7.0.0 | `not-applicable` | Sentinel administration. |
| `SENTINEL MONITOR` | 2.8.4 | `not-applicable` | Sentinel administration (start monitoring a primary). |
| `SENTINEL REMOVE` | 2.8.4 | `not-applicable` | Sentinel administration (stop monitoring a primary). |
| `MODULE` | 4.0.0 | `not-applicable` | Module administration. |
| `MODULE LIST` | 4.0.0 | `not-applicable` | Module administration. |
| `MODULE LOAD` | 4.0.0 | `not-applicable` | Module administration. |
| `MODULE LOADEX` | 7.0.0 | `not-applicable` | Module administration. |
| `MODULE UNLOAD` | 4.0.0 | `not-applicable` | Module administration. |
| `BACKUP` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP ABORT` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP CLEANUP` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP LIST` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP SEAL` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP START` | 8.10.0 | `not-applicable` | Server backup administration. |
| `BACKUP STATUS` | 8.10.0 | `not-applicable` | Server backup administration. |
| `COMMAND DOCS` | 7.0.0 | `not-applicable` | Command introspection for tooling; `COMMAND COUNT`, `COMMAND LIST` and `COMMAND GETKEYS` are exposed on `IServer`. |
| `COMMAND INFO` | 2.8.13 | `not-applicable` | As `COMMAND DOCS`. |
| `COMMAND GETKEYSANDFLAGS` | 7.0.0 | `not-applicable` | As `COMMAND DOCS`. |

## Deferred: wanted, design first

| Command | Since | Status | Notes |
|---|---|---|---|
| `FUNCTION` | 7.0.0 | `deferred` | Redis Functions as a whole need an API design (loading, library management, `FCALL` keys/args, `ScriptEvaluate` parity). Tracked from #2055. |
| `FUNCTION DELETE` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION DUMP` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION FLUSH` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION KILL` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION LIST` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION LOAD` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION RESTORE` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FUNCTION STATS` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FCALL` | 7.0.0 | `deferred` | Part of the Functions design. |
| `FCALL_RO` | 7.0.0 | `deferred` | Part of the Functions design. |
| `ACL` | 6.0.0 | `deferred` | ACL administration needs a typed model for users, rules and the log; a partial surface is worse than none. Tracked from #2055. |
| `ACL CAT` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL DELUSER` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL DRYRUN` | 7.0.0 | `deferred` | Part of the ACL design. |
| `ACL GENPASS` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL GETUSER` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL LIST` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL LOAD` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL LOG` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL SAVE` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL SETUSER` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL USERS` | 6.0.0 | `deferred` | Part of the ACL design. |
| `ACL WHOAMI` | 6.0.0 | `deferred` | Part of the ACL design. |
| `CLIENT TRACKING` | 6.0.0 | `deferred` | Client-side caching is a connection-level feature (invalidation pushes routed by the multiplexer), not a command. Implemented in v4, where the handshake issues `CLIENT TRACKING`; not planned for v3. |
| `CLIENT TRACKINGINFO` | 6.2.0 | `deferred` | Part of client-side caching (v4). |
| `CLIENT NO-EVICT` | 7.0.0 | `deferred` | Per-connection flag; belongs in `ConfigurationOptions` and the handshake, not on `IServer`. |
| `CLIENT NO-TOUCH` | 7.2.0 | `deferred` | Per-connection flag, as `NO-EVICT`. |
| `CLUSTER SHARDS` | 7.0.0 | `deferred` | Not used anywhere today: `ClusterConfiguration` is built from `CLUSTER NODES`, and configuration export captures `NODES` and `SLOTS`. Moving topology discovery to `SHARDS`, or exposing it typed, is a design question. |
| `CLUSTER LINKS` | 7.0.0 | `deferred` | Topology introspection. |
| `CLUSTER SLOT-STATS` | 8.2.0 | `deferred` | Per-slot statistics; pairs with hot-key tooling. |

## Todo: accepted gaps

PRs welcome; follow the `implement-resp-command` skill. Pre-release server features go behind the matching `Experiments.Server_x_y` gate.

| Command | Since | Status | Notes |
|---|---|---|---|
| `BLESS` | 8.12.0 | `todo` | Key blessing (eviction protection). #3261, in progress on `marc/bless`. |
| `BLESS SET` | 8.12.0 | `todo` | As `BLESS`. |
| `BLESS GET` | 8.12.0 | `todo` | As `BLESS`. |
| `BLESS CLEAR` | 8.12.0 | `todo` | As `BLESS`. |
| `BLESS SCAN` | 8.12.0 | `todo` | As `BLESS`. |
| `SADDEX` | 8.12.0 | `todo` | Set member expiry; mirror the hash-field expiry surface (`HashFieldExpire` et al). |
| `SEXPIRE` | 8.12.0 | `todo` | Set member expiry. |
| `SEXPIREAT` | 8.12.0 | `todo` | Set member expiry. |
| `SEXPIRETIME` | 8.12.0 | `todo` | Set member expiry (mirrors `HEXPIRETIME`). |
| `SPERSIST` | 8.12.0 | `todo` | Set member expiry. |
| `SPEXPIRE` | 8.12.0 | `todo` | Set member expiry. |
| `SPEXPIREAT` | 8.12.0 | `todo` | Set member expiry. |
| `SPEXPIRETIME` | 8.12.0 | `todo` | Set member expiry. |
| `SPTTL` | 8.12.0 | `todo` | Set member expiry; the read path (see `STTL` above). |
| `BITRANGEINTARRAY` | 8.12.0 | `todo` | Bitmap to integer array; sits with the existing `BITFIELD`/`BITOP` surface. |
| `MEMORY USAGE` | 4.0.0 | `todo` | Per-key memory; `MEMORY DOCTOR`/`STATS`/`PURGE` are exposed, this one is the useful one for profiling. |
| `CLUSTER INFO` | 3.0.0 | `todo` | Cluster state summary (`cluster_state`, slot counts, epoch) on `IServer`, next to `ClusterNodes`. |
| `LATENCY HISTOGRAM` | 7.0.0 | `todo` | Per-command latency histograms on `IServer`, next to `LATENCY LATEST`/`HISTORY`. |
| `CLIENT PAUSE` | 3.0.0 | `todo` | Pause/unpause as an `IServer` pair; useful in coordinated failover scripts. Neither half is exposed today. |
| `CLIENT UNPAUSE` | 6.2.0 | `todo` | As `CLIENT PAUSE`. |
| `PUBSUB SHARDCHANNELS` | 7.0.0 | `todo` | Sharded pub/sub is implemented; the introspection half is not (`IServer.SubscriptionChannels` covers the global form). |
| `PUBSUB SHARDNUMSUB` | 7.0.0 | `todo` | As `SHARDCHANNELS` (`IServer.SubscriptionSubscriberCount` covers the global form). |
| `XGROUP CREATECONSUMER` | 6.2.0 | `todo` | Explicit consumer creation; `XGROUP CREATE`/`DESTROY`/`DELCONSUMER`/`SETID` are exposed. |

## Deprecated upstream

Listed for reference; the rule excludes them without an entry.

| Command | Since | Status | Notes |
|---|---|---|---|
| `SUBSTR` | 1.0.0 | `deprecated` | Deprecated 2.0.0; `StringGetRange` (`GETRANGE`). |
| `GEORADIUS_RO` | 3.2.10 | `deprecated` | Deprecated 6.2.0; `GeoSearch` (`GEOSEARCH BYRADIUS`). |
| `GEORADIUSBYMEMBER_RO` | 3.2.10 | `deprecated` | Deprecated 6.2.0; `GeoSearch` (`GEOSEARCH FROMMEMBER BYRADIUS`). |

## Implemented, invisible to the audit script

Subcommands written by a hand-rolled `Message` rather than a `RedisLiterals` token, which the coverage script cannot detect. Listed so the script does not report them; delete a row if the implementation moves to a literal.

| Command | Since | Status | Notes |
|---|---|---|---|
| `HOTKEYS START` | 8.6.0 | `implemented` | `HotKeys.StartMessage` writes the token raw. See [Hot keys](HotKeys). |
| `SCRIPT LOAD` | 2.6.0 | `implemented` | `RedisDatabase.ScriptLoadMessage`; `IServer.ScriptLoad`. |

## In the enum, not in the spec

Commands the library implements that `redis/redis` does not specify, because they ship as a module with their own spec.

| Command | Since | Status | Notes |
|---|---|---|---|
| `VADD` | 8.0.0 | `enum-only` | Vector sets (`redis/redis-vector-sets`); see [Vector sets](VectorSets). |
| `VCARD` | 8.0.0 | `enum-only` | Vector sets. |
| `VDIM` | 8.0.0 | `enum-only` | Vector sets. |
| `VEMB` | 8.0.0 | `enum-only` | Vector sets. |
| `VGETATTR` | 8.0.0 | `enum-only` | Vector sets. |
| `VINFO` | 8.0.0 | `enum-only` | Vector sets. |
| `VISMEMBER` | 8.0.0 | `enum-only` | Vector sets. |
| `VLINKS` | 8.0.0 | `enum-only` | Vector sets. |
| `VRANDMEMBER` | 8.0.0 | `enum-only` | Vector sets. |
| `VRANGE` | 8.0.0 | `enum-only` | Vector sets. |
| `VREM` | 8.0.0 | `enum-only` | Vector sets. |
| `VSETATTR` | 8.0.0 | `enum-only` | Vector sets. |
| `VSIM` | 8.0.0 | `enum-only` | Vector sets. |
