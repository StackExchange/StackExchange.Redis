# Release Notes

# Important: .NET 4.0 support is currently **disabled**; if you need .NET 4.0, please stick with 1.2.1 while we keep investigating

## 1.2.6

- fix change to `cluster nodes` output when using cluster-enabled target and 4.0+ (see [redis #4186]https://github.com/antirez/redis/issues/4186)

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