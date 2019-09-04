# Release Notes

## 2.0.600

- add `ulong` support to `RedisValue` and `RedisResult` (#1103)
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
