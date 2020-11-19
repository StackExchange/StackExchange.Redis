# Release Notes

## 2.2.4

- fix ambiguous signature of the new `RPUSHX`/`LPUSHX` methods (#1620)

## 2.2.3

- add .NET 5 target
- fix mutex race condition (#1585 via arsnyder16)
- allow `CheckCertificateRevocation` to be controlled via the config string (#1591 via lwlwalker)
- fix range end-value inversion (#1573 via tombatron)
- add `ROLE` support (#1551 via zmj)
- add varadic `RPUSHX`/`LPUSHX` support (#1557 via dmytrohridin)
- fix server-selection strategy race condition (#1532 via deepakverma)
- fix sentinel default port (#1525 via ejsmith)
- fix `Int64` parse scenario (#1568 via arsnyder16)
- force replication check during failover (via joroda)
- documentation tweaks (multiple)
- fix backlog contention issue (#1612, see also #1574 via devbv)

## 2.1.58

- fix: `[*]SCAN` - fix possible NRE scenario if the iterator is disposed with an incomplete operation in flight
- fix: `[*]SCAN` - treat the cursor as an opaque value whenever possible, for compatibility with `redis-cluster-proxy`
- add: `[*]SCAN` - include additional exception data in the case of faults

## 2.1.55

- identify assembly binding problem on .NET Framework; drops `System.IO.Pipelines` to 4.7.1, and identifies new `System.Buffers` binding failure on 4.7.2

## 2.1.50

- add: bind direct to sentinel-managed instances from a configuration string/object (#1431 via ejsmith)
- add last-delivered-id to `StreamGroupInfo` (#1477 via AndyPook)
- update naming of replication-related commands to reflect Redis 5 naming (#1488/#945)
- fix: the `IServer` commands that are database-specific (`DBSIZE`, `FLUSHDB`, `KEYS`, `SCAN`) now respect the default database on the config (#1460)
- library updates

## 2.1.39

- fix: mutex around connection was not "fair"; in specific scenario could lead to out-of-order commands (#1440)
- fix: update libs (#1432)
- fix: timing error on linux (#1433 via pengweiqhca)
- fix: add `auth` to command-map for sentinal (#1428 via ejsmith)

## 2.1.30

- fix deterministic builds

## 2.1.28

- fix: stability in new sentinel APIs
- fix: include `SslProtocolos` in `ConfigurationOptions.ToString()` (#1408 via vksampath and Sampath Vuyyuru
- fix: clarify messaging around disconnected multiplexers (#1396)
- change: tweak methods of new sentinel API (this is technically a breaking change, but since this is a new API that was pulled quickly, we consider this to be acceptable)
- add: new thread`SocketManager` mode (opt-in) to always use the regular thread-pool instead of the dedicated pool
- add: improved counters in/around error messages
- add: new `User` property on `ConfigurationOptions`
- build: enable deterministic builds (note: this failed; fixed in 2.1.30)

## 2.1.0

- fix: ensure active-message is cleared (#1374 via hamish-omny)
- add: sentinel support (#1067 via shadim; #692 via lexxdark)
- add: `IAsyncEnumerable<T>` scanning APIs now supported (#1087)
- add: new API for use with misbehaving sync-contexts ([more info](https://stackexchange.github.io/StackExchange.Redis/ThreadTheft))
- add: `TOUCH` support (#1291 via gkorland)
- add: `Condition` API (transactions) now supports `SortedSetLengthEqual` (#1332 via phosphene47)
- add: `SocketManager` is now more configurable (#1115, via naile)
- add: NRediSearch updated in line with JRediSearch (#1267, via tombatron; #1199 via oruchreis)
- add: support for `CheckCertificatRevocation` configuration (#1234, via BLun78 and V912736)
- add: more details about exceptions (#1190, via marafiq)
- add: new stream APIs (#1141 and #1154 via ttingen)
- add: event-args now mockable (#1326 via n1l)
- fix: no-op when adding 0 values to a set (#1283 via omeaart)
- add: support for `LATENCY` and `MEMORY` (#1204)
- add: support for `HSTRLEN` (#1241 via eitanhs)
- add: `GeoRadiusResult` is now mockable (#1175 via firenero)
- fix: various documentation fixes (#1162, #1135, #1203, #1240, #1245, #1159, #1311, #1339, #1336)
- fix: rare race-condition around exception data (#1342)
- fix: `ScriptEvaluateAsync` keyspace isolation (#1377 via gliljas)
- fix: F# compatibility enhancements (#1386)
- fix: improved `ScriptResult` null support (#1392)
- fix: error with DNS resolution breaking endpoint iterator (#1393)
- tests: better docker support for tests (#1389 via ejsmith; #1391)
- tests: general test improvements (#1183, #1385, #1384)

## 2.0.601

- add: tracking for current and next messages to help with debugging timeout issues - helpful in cases of large pipeline blockers

## 2.0.600

- add: `ulong` support to `RedisValue` and `RedisResult` (#1103)
- fix: remove odd equality: `"-" != 0` (we do, however, still allow `"-0"`, as that is at least semantically valid, and is logically `== 0`) (related to #1103)
- performance: rework how pub/sub queues are stored - reduces delegate overheads (related to #1101)
- fix #1108 - ensure that we don't try appending log data to the `TextWriter` once we've returned from a method that accepted one

## 2.0.593

- performance: unify spin-wait usage on sync/async paths to one competitor
- fix #1101 - when a `ChannelMessageQueue` is involved, unsubscribing *via any route* should still unsubscribe and mark the queue-writer as complete

## 2.0.588

- stability and performance: resolve intermittent stall in the write-lock that could lead to unexpected timeouts even when at low/reasonable (but concurrent) load

## 2.0.571

- performance: use new [arena allocation API](https://mgravell.github.io/Pipelines.Sockets.Unofficial/docs/arenas) to avoid `RawResult[]` overhead
- performance: massively simplified how `ResultBox<T>` is implemented, in particular to reduce `TaskCompletionSource<T>` allocations
- performance: fix sync-over-async issue with async call paths, and fix the [SemaphoreSlim](https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html) problems that this uncovered
- performance: re-introduce the unsent backlog queue, in particular to improve async performance
- performance: simplify how completions are reactivated, so that external callers use their originating pool, not the dedicated IO pools (prevent thread stealing)
- fix: update Pipelines.Sockets.Unofficial to prevent issue with incorrect buffer re-use in corner-case
- fix: `KeyDeleteAsync` could, in some cases, always use `DEL` (instead of `UNLINK`)
- fix: last unanswered write time was incorrect
- change: use higher `Pipe` thresholds when sending

## 2.0.519

- adapt to late changes in the RC streams API (#983, #1007)
- documentation fixes (#997, #1005)
- build: switch to SDK 2.1.500

## 2.0.513

- fix #961 - fix assembly binding redirect problems; IMPORTANT: this drops to an older `System.Buffers` version - if you have manually added redirects for `4.0.3.0`, you may need to manually update to `4.0.2.0` (or remove completely)
- fix #962 - avoid NRE in edge-case when fetching bridge

## 2.0.505

- fix #943 - ensure transaction inner tasks are completed prior to completing the outer transaction task
- fix #946 - reinstate missing `TryParse` methods on `RedisValue`
- fix #940 - off-by-one on pre-boxed integer cache (NRediSearch)

## 2.0.495

- 2.0 is a large - and breaking - change

The key focus of this release is stability and reliability.

- HARD BREAK: the package identity has changed; instead of `StackExchange.Redis` (not strong-named) and `StackExchange.Redis.StrongName` (strong-named), we are now
  only releasing `StackExchange.Redis` (strong-named). This is a binary breaking change that requires consumers to be re-compiled; it cannot be applied via binding-redirects
- HARD BREAK: the platform targets have been rationalized - supported targets are .NETStandard 2.0 (and above), .NETFramework 4.6.1 (and above), and .NETFramework 4.7.2 (and above)
  (note - the last two are mainly due to assembly binding problems)
- HARD BREAK: the profiling API has been overhauled and simplified; full documentation is [provided here](https://stackexchange.github.io/StackExchange.Redis/Profiling_v2.html)
- SOFT BREAK: the `PreserveAsyncOrder` behaviour of the pub/sub API has been deprecated; a *new* API has been provided for scenarios that require in-order pub/sub handling -
  the `Subscribe` method has a new overload *without* a handler parameter which returns a `ChannelMessageQueue`, which provides `async` ordered access to messsages)
- internal: the network architecture has moved to use `System.IO.Pipelines`; this has allowed us to simplify and unify a lot of the network code, and in particular 
  fix a lot of problems relating to how the library worked with TLS and/or .NETStandard
- change: as a result of the `System.IO.Pipelines` change, the error-reporting on timeouts is now much simpler and clearer; the [timeouts documentation](Timeouts.md) has been updated
- removed: the `HighPriority` (queue-jumping) flag is now deprecated
- internal: most buffers internally now make use of pooled memory; `RedisValue` no longer pre-emptively allocates buffers
- internal: added new custom thread-pool for handling async continuations to avoid thread-pool starvation issues
- internal: all IL generation has been removed; the library should now work on platforms that do not allow runtime-emit
- added: asynchronous operations now have full support for reporting timeouts
- added: new APIs now exist to work with pooled memory without allocations - `RedisValue.CreateFrom(MemoryStream)` and `operator` support for `Memory<byte>` and `ReadOnlyMemory<byte>`; and `IDatabase.StringGetLease[Async](...)`, `IDatabase.HashGetLease[Async](...)`, `Lease<byte>.AsStream()`)
- added: ["streams"](https://redis.io/topics/streams-intro) support (thanks to [ttingen](https://github.com/ttingen) for their contribution)
- various missing commands / overloads have been added; `Execute[Async]` for additional commands is now available on `IServer`
- fix: a *lot* of general bugs and issues have been resolved
- ACCIDENTAL BREAK: `RedisValue.TryParse` was accidentally ommitted in the overhaul; this has been rectified and will be available in the next build

a more complete list of issues addressed can be seen in [this tracking issue](https://github.com/StackExchange/StackExchange.Redis/issues/871)

Note: we currently have no plans to do an additional 1.* release. In particular, even though there was a `1.2.7-alpha` build on nuget, we *do not* currently have
plans to release `1.2.7`.

---

## 1.2.6

- fix change to `cluster nodes` output when using cluster-enabled target and 4.0+ (see [redis #4186](https://github.com/antirez/redis/issues/4186)

## 1.2.5

- critical fix: "poll mode" was disabled in the build for net45/net60 - impact: IO jams and lack of reader during high load

## 1.2.4

- fix: incorrect build configuration (#649)

## 1.2.3

- fix: when using `redis-cluster` with multiple replicas, use round-robin when selecting replica (#610)
- add: can specify `NoScriptCache` flag when using `ScriptEvaluate` to bypass all cache features (always uses `EVAL` instead of `SCRIPT LOAD` and `EVALSHA`) (#617)

## 1.2.2 (preview):

- **UNAVAILABLE**: .NET 4.0 support is not in this build, due to [a build issue](https://github.com/dotnet/cli/issues/5993) - looking into solutions
- add: make performance-counter tracking opt-in (`IncludePerformanceCountersInExceptions`) as it was causing problems (#587)
- add: can now specifiy allowed SSL/TLS protocols  (#603)
- add: track message status in exceptions (#576)
- add: `GetDatabase()` optimization for DB 0 and low numbered databases: `IDatabase` instance is retained and recycled (as long as no `asyncState` is provided)
- improved connection retry policy (#510, #572)
- add `Execute`/`ExecuteAsync` API to support "modules"; [more info](http://blog.marcgravell.com/2017/04/stackexchangeredis-and-redis-40-modules.html)
- fix: timeout link fixed re /docs change (below)
- [`NRediSearch`](https://www.nuget.org/packages/NRediSearch/) added as exploration into "modules"

Other changes (not library related)

- (project) refactor /docs for github pages
- improve release note tracking
- rework build process to use csproj

## 1.2.1

- fix: avoid overlapping per-endpoint heartbeats

## 1.2.0

- (same as 1.2.0-alpha1)

## 1.2.0-alpha1

- add: GEO commands (#489)
- add: ZADD support for new NX/XX switches (#520)
- add: core-clr preview support improvements

## 1.1.608

- fix: bug with race condition in servers indexer (related: 1.1.606)

## 1.1.607

- fix: ensure socket-mode polling is enabled (.net)

## 1.1.606

- fix: bug with race condition in servers indexer

## and the rest

(I'm happy to take PRs for change history going back in time)
