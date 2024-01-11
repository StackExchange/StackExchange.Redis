# Release Notes

Current package versions:

| NuGet Stable | NuGet Pre-release | MyGet |
| ------------ | ----------------- | ----- |
| [![StackExchange.Redis](https://img.shields.io/nuget/v/StackExchange.Redis.svg)](https://www.nuget.org/packages/StackExchange.Redis/) | [![StackExchange.Redis](https://img.shields.io/nuget/vpre/StackExchange.Redis.svg)](https://www.nuget.org/packages/StackExchange.Redis/) | [![StackExchange.Redis MyGet](https://img.shields.io/myget/stackoverflow/vpre/StackExchange.Redis.svg)](https://www.myget.org/feed/stackoverflow/package/nuget/StackExchange.Redis) |

## Unreleased

- Fix [#2619](https://github.com/StackExchange/StackExchange.Redis/issues/2619): Type-forward `IsExternalInit` to support down-level TFMs ([#2621 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2621))
- `InternalsVisibleTo` `PublicKey` enhancements([#2623 by WeihanLi](https://github.com/StackExchange/StackExchange.Redis/pull/2623))
- Fix [#2576](https://github.com/StackExchange/StackExchange.Redis/issues/2576): Prevent `NullReferenceException` during shutdown of connections ([#2629 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2629))

## 2.7.10

- Fix [#2593](https://github.com/StackExchange/StackExchange.Redis/issues/2593): `EXPIRETIME` and `PEXPIRETIME` miscategorized as `PrimaryOnly` commands causing them to fail when issued against a read-only replica ([#2593 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2593))
- Fix [#2591](https://github.com/StackExchange/StackExchange.Redis/issues/2591): Add `HELLO` to Sentinel connections so they can support RESP3 ([#2601 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2601))
- Fix [#2595](https://github.com/StackExchange/StackExchange.Redis/issues/2595): Add detection handling for dead sockets that the OS says are okay, seen especially in Linux environments (https://github.com/StackExchange/StackExchange.Redis/pull/2610)

## 2.7.4

- Adds: RESP3 support ([#2396 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2396)) - see https://stackexchange.github.io/StackExchange.Redis/Resp3
- Fix [#2507](https://github.com/StackExchange/StackExchange.Redis/issues/2507): Pub/sub with multi-item payloads should be usable ([#2508 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2508))
- Add: connection-id tracking (internal only, no public API) ([#2508 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2508))
- Add: `ConfigurationOptions.LoggerFactory` for logging to an `ILoggerFactory` (e.g. `ILogger`) all connection and error events ([#2051 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2051))
- Fix [#2467](https://github.com/StackExchange/StackExchange.Redis/issues/2467): Add StreamGroupInfo EntriesRead and Lag ([#2510 by tvdias](https://github.com/StackExchange/StackExchange.Redis/pull/2510))

## 2.6.122

- Change: Target net6.0 instead of net5.0, since net5.0 is end of life. ([#2497 by eerhardt](https://github.com/StackExchange/StackExchange.Redis/pull/2497))
- Fix: Fix nullability annotation of IConnectionMultiplexer.RegisterProfiler ([#2494 by eerhardt](https://github.com/StackExchange/StackExchange.Redis/pull/2494))
- Fix [#2520](https://github.com/StackExchange/StackExchange.Redis/issues/2520): Improve cluster connections in down scenarios by not re-pinging successful nodes ([#2525 by Matiszak](https://github.com/StackExchange/StackExchange.Redis/pull/2525))
- Add: `Timer.ActiveCount` under `POOL` in timeout messages on .NET 6+ to help diagnose timer overload affecting timeout evaluations ([#2500 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2500))
- Add: `LibraryName` configuration option; allows the library name to be controlled at the individual options level (in addition to the existing controls in `DefaultOptionsProvider`) ([#2502 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2502))
- Add: `DefaultOptionsProvider.GetProvider` allows lookup of provider by endpoint ([#2502 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2502))

## 2.6.116

- Fix [#2479](https://github.com/StackExchange/StackExchange.Redis/issues/2479): Add `RedisChannel.UseImplicitAutoPattern` (global) and `RedisChannel.IsPattern` ([#2480 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2480))
- Fix [#2479](https://github.com/StackExchange/StackExchange.Redis/issues/2479): Mark `RedisChannel` conversion operators as obsolete; add `RedisChannel.Literal` and `RedisChannel.Pattern` helpers ([#2481 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2481))
- Fix [#2449](https://github.com/StackExchange/StackExchange.Redis/issues/2449): Update `Pipelines.Sockets.Unofficial` to `v2.2.8` to support native AOT ([#2456 by eerhardt](https://github.com/StackExchange/StackExchange.Redis/pull/2456))

## 2.6.111

- Fix [#2426](https://github.com/StackExchange/StackExchange.Redis/issues/2426): Don't restrict multi-slot operations on Envoy proxy; let the proxy decide ([#2428 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2428))
- Add: Support for `User`/`Password` in `DefaultOptionsProvider` to support token rotation scenarios ([#2445 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2445))
- Fix [#2449](https://github.com/StackExchange/StackExchange.Redis/issues/2449): Resolve AOT trim warnings in `TryGetAzureRoleInstanceIdNoThrow` ([#2451 by eerhardt](https://github.com/StackExchange/StackExchange.Redis/pull/2451))
- Adds: Support for `HTTP/1.1 200 Connection established` in HTTP Tunnel ([#2448 by flobernd](https://github.com/StackExchange/StackExchange.Redis/pull/2448))
- Adds: Timeout duration to backlog timeout error messages ([#2452 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2452))
- Adds: `DefaultOptionsProvider.LibraryName` for specifying lib-name passed to `CLIENT SETINFO` in Redis 7.2+ ([#2453 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2453))

## 2.6.104

- Fix [#2412](https://github.com/StackExchange/StackExchange.Redis/issues/2412): Critical (but rare) GC bug that can lead to async tasks never completing if the multiplexer is not held by the consumer ([#2408 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2408))
- Add: Better error messages (over generic timeout) when commands are backlogged and unable to write to any connection ([#2408 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2408))
- Fix [#2392](https://github.com/StackExchange/StackExchange.Redis/issues/2392): Dequeue *all* timed out messages from the backlog when not connected (including Fire+Forget) ([#2397 by kornelpal](https://github.com/StackExchange/StackExchange.Redis/pull/2397))
- Fix [#2400](https://github.com/StackExchange/StackExchange.Redis/issues/2400): Expose `ChannelMessageQueue` as `IAsyncEnumerable<ChannelMessage>` ([#2402 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2402))
- Add: Support for `CLIENT SETINFO` (lib name/version) during handshake; opt-out is via `ConfigurationOptions`; also support read of `resp`, `lib-ver` and `lib-name` via `CLIENT LIST` ([#2414 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2414))
- Documentation: clarify the meaning of `RedisValue.IsInteger` re [#2418](https://github.com/StackExchange/StackExchange.Redis/issues/2418) ([#2420 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2420))

## 2.6.96

- Fix [#2350](https://github.com/StackExchange/StackExchange.Redis/issues/2350): Properly parse lua script parameters in all cultures ([#2351 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2351))
- Fix [#2362](https://github.com/StackExchange/StackExchange.Redis/issues/2362): Set `RedisConnectionException.FailureType` to `AuthenticationFailure` on all authentication scenarios for better handling ([#2367 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2367))
- Fix [#2368](https://github.com/StackExchange/StackExchange.Redis/issues/2368): Support `RedisValue.Length()` for all storage types ([#2370 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2370))
- Fix [#2376](https://github.com/StackExchange/StackExchange.Redis/issues/2376): Avoid a (rare) deadlock scenario ([#2378 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2378))

## 2.6.90

- Adds: Support for `EVAL_RO` and `EVALSHA_RO` via `IDatabase.ScriptEvaluateReadOnly`/`IDatabase.ScriptEvaluateReadOnlyAsync` ([#2168 by shacharPash](https://github.com/StackExchange/StackExchange.Redis/pull/2168))
- Fix [#1458](https://github.com/StackExchange/StackExchange.Redis/issues/1458): Fixes a leak condition when a connection completes on the TCP phase but not the Redis handshake ([#2238 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2238))
- Internal: ServerSnapshot: Improve API and allow filtering with custom struct enumerator ([#2337 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2337))


## 2.6.86

- Fix [#1520](https://github.com/StackExchange/StackExchange.Redis/issues/1520) & [#1660](https://github.com/StackExchange/StackExchange.Redis/issues/1660): When `MOVED` is encountered from a cluster, a reconfigure will happen proactively to react to cluster changes ASAP ([#2286 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2286))
- Fix [#2249](https://github.com/StackExchange/StackExchange.Redis/issues/2249): Properly handle a `fail` state (new `ClusterNode.IsFail` property) for `CLUSTER NODES` and expose `fail?` as a property (`IsPossiblyFail`) as well ([#2288 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2288))
- Adds: `IConnectionMultiplexer.ServerMaintenanceEvent` (was on `ConnectionMultiplexer` but not the interface) ([#2306 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2306))
- Adds: To timeout messages, additional debug information: `Sync-Ops` (synchronous operations), `Async-Ops` (asynchronous operations), and `Server-Connected-Seconds` (how long the connection in question has been connected, or `"n/a"`) ([#2300 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2300))
- Fix: [#2321](https://github.com/StackExchange/StackExchange.Redis/issues/2321): Honor disposition of select command in Command Map for transactions [(#2322 by slorello89)](https://github.com/StackExchange/StackExchange.Redis/pull/2322)

## 2.6.80

- Adds: `last-in` and `cur-in` (bytes) to timeout exceptions to help identify timeouts that were just-behind another large payload off the wire ([#2276 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2276))
- Adds: general-purpose tunnel support, with HTTP proxy "connect" support included ([#2274 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2274))
- Removes: Package dependency (`System.Diagnostics.PerformanceCounter`) ([#2285 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2285))


## 2.6.70

- Fix: `MOVED` with `NoRedirect` (and other non-reachable errors) should respect the `IncludeDetailInExceptions` setting ([#2267 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2267))
- Fix [#2251](https://github.com/StackExchange/StackExchange.Redis/issues/2251) & [#2265](https://github.com/StackExchange/StackExchange.Redis/issues/2265): Cluster endpoint connections weren't proactively connecting subscriptions in all cases and taking the full connection timeout to complete as a result ([#2268 by iteplov](https://github.com/StackExchange/StackExchange.Redis/pull/2268))


## 2.6.66

- Fix [#2182](https://github.com/StackExchange/StackExchange.Redis/issues/2182): Be more flexible in which commands are "primary only" in order to support users with replicas that are explicitly configured to allow writes ([#2183 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2183))
- Adds: `IConnectionMultiplexer` now implements `IAsyncDisposable` ([#2161 by kimsey0](https://github.com/StackExchange/StackExchange.Redis/pull/2161))
- Adds: `IConnectionMultiplexer.GetServers()` to get all `IServer` instances for a multiplexer ([#2203 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2203))
- Fix [#2016](https://github.com/StackExchange/StackExchange.Redis/issues/2016): Align server selection with supported commands (e.g. with writable servers) to reduce `Command cannot be issued to a replica` errors ([#2191 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2191))
- Performance: Optimization around timeout processing to reduce lock contention in the case of many items that haven't yet timed out during a heartbeat ([#2217 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2217))
- Fix [#2223](https://github.com/StackExchange/StackExchange.Redis/issues/2223): Resolve sync-context issues (missing `ConfigureAwait(false)`) ([#2229 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2229))
- Fix [#1968](https://github.com/StackExchange/StackExchange.Redis/issues/1968): Improved handling of EVAL scripts during server restarts and failovers, detecting and re-sending the script for a retry when needed ([#2170 by martintmk](https://github.com/StackExchange/StackExchange.Redis/pull/2170))
- Adds: `ConfigurationOptions.SslClientAuthenticationOptions` (`netcoreapp3.1`/`net5.0`+ only) to give more control over SSL/TLS authentication ([#2224 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2224))
- Fix [#2240](https://github.com/StackExchange/StackExchange.Redis/pull/2241): Improve support for DNS-based IPv6 endpoints ([#2241 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2241))
- Adds: `ConfigurationOptions.HeartbeatInterval` (**Advanced Setting** - [see docs](https://stackexchange.github.io/StackExchange.Redis/Configuration#configuration-options)) To allow more finite control of the client heartbeat, which encompases how often command timeouts are actually evaluated - still defaults to 1,000 ms ([#2243 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2243))
- Fix [#1879](https://github.com/StackExchange/StackExchange.Redis/issues/1879): Improve exception message when the wrong password is used ([#2246 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2246))
- Fix [#2233](https://github.com/StackExchange/StackExchange.Redis/issues/2233): Repeated connection to Sentinel servers using the same ConfigurationOptions would fail ([#2242 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2242))


## 2.6.48

- URGENT Fix: [#2167](https://github.com/StackExchange/StackExchange.Redis/issues/2167), [#2176](https://github.com/StackExchange/StackExchange.Redis/issues/2176): fix error in batch/transaction handling that can result in out-of-order instructions ([#2177 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2177))
- Fix: [#2164](https://github.com/StackExchange/StackExchange.Redis/issues/2164): fix `LuaScript.Prepare` for scripts that don't have parameters ([#2166 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2166))

## 2.6.45

- Adds: [Nullable reference type](https://docs.microsoft.com/en-us/dotnet/csharp/nullable-references) annotations ([#2041 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2041))
  - Adds annotations themselves for nullability to everything in the library
  - Fixes a few internal edge cases that will now throw proper errors (rather than a downstream null reference)
  - Fixes inconsistencies with `null` vs. empty array returns (preferring an not-null empty array in those edge cases)
  - Note: does *not* increment a major version (as these are warnings to consumers), because: they're warnings (errors are opt-in), removing obsolete types with a 3.0 rev _would_ be binary breaking (this isn't), and reving to 3.0 would cause binding redirect pain for consumers. Bumping from 2.5 to 2.6 only for this change.
- Adds: Support for `COPY` with `.KeyCopy()`/`.KeyCopyAsync()` ([#2064 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2064))
- Adds: Support for `LMOVE` with `.ListMove()`/`.ListMoveAsync()` ([#2065 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2065))
- Adds: Support for `ZRANDMEMBER` with `.SortedSetRandomMember()`/`.SortedSetRandomMemberAsync()`, `.SortedSetRandomMembers()`/`.SortedSetRandomMembersAsync()`, and `.SortedSetRandomMembersWithScores()`/`.SortedSetRandomMembersWithScoresAsync()` ([#2076 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2076))
- Adds: Support for `SMISMEMBER` with `.SetContains()`/`.SetContainsAsync()` ([#2077 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2077))
- Adds: Support for `ZDIFF`, `ZDIFFSTORE`, `ZINTER`, `ZINTERCARD`, and `ZUNION` with `.SortedSetCombine()`/`.SortedSetCombineAsync()`, `.SortedSetCombineWithScores()`/`.SortedSetCombineWithScoresAsync()`, and `.SortedSetIntersectionLength()`/`.SortedSetIntersectionLengthAsync()` ([#2075 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2075))
- Adds: Support for `SINTERCARD` with `.SetIntersectionLength()`/`.SetIntersectionLengthAsync()` ([#2078 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2078))
- Adds: Support for `LPOS` with `.ListPosition()`/`.ListPositionAsync()` and `.ListPositions()`/`.ListPositionsAsync()` ([#2080 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2080))
- Adds: Support for `ZMSCORE` with `.SortedSetScores()`/.`SortedSetScoresAsync()` ([#2082 by ttingen](https://github.com/StackExchange/StackExchange.Redis/pull/2082))
- Adds: Support for `NX | XX | GT | LT` to `EXPIRE`, `EXPIREAT`, `PEXPIRE`, and `PEXPIREAT` with `.KeyExpire()`/`.KeyExpireAsync()` ([#2083 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2083))
- Adds: Support for `EXPIRETIME`, and `PEXPIRETIME` with `.KeyExpireTime()`/`.KeyExpireTimeAsync()` ([#2083 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2083))
- Fix: For streams, properly hash `XACK`, `XCLAIM`, and `XPENDING` in cluster scenarios to eliminate `MOVED` retries ([#2085 by nielsderdaele](https://github.com/StackExchange/StackExchange.Redis/pull/2085))
- Adds: Support for `OBJECT REFCOUNT` with `.KeyRefCount()`/`.KeyRefCountAsync()` ([#2087 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2087))
- Adds: Support for `OBJECT ENCODING` with `.KeyEncoding()`/`.KeyEncodingAsync()` ([#2088 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2088))
- Adds: Support for `GEOSEARCH` with `.GeoSearch()`/`.GeoSearchAsync()` ([#2089 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2089))
- Adds: Support for `GEOSEARCHSTORE` with `.GeoSearchAndStore()`/`.GeoSearchAndStoreAsync()` ([#2089 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2089))
- Adds: Support for `HRANDFIELD` with `.HashRandomField()`/`.HashRandomFieldAsync()`, `.HashRandomFields()`/`.HashRandomFieldsAsync()`, and `.HashRandomFieldsWithValues()`/`.HashRandomFieldsWithValuesAsync()` ([#2090 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2090))
- Adds: Support for `LMPOP` with `.ListLeftPop()`/`.ListLeftPopAsync()` and `.ListRightPop()`/`.ListRightPopAsync()` ([#2094 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2094))
- Adds: Support for `ZMPOP` with `.SortedSetPop()`/`.SortedSetPopAsync()` ([#2094 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2094))
- Adds: Support for `XAUTOCLAIM` with `.StreamAutoClaim()`/.`StreamAutoClaimAsync()` and `.StreamAutoClaimIdsOnly()`/.`StreamAutoClaimIdsOnlyAsync()` ([#2095 by ttingen](https://github.com/StackExchange/StackExchange.Redis/pull/2095))
- Fix [#2071](https://github.com/StackExchange/StackExchange.Redis/issues/2071): Add `.StringSet()`/`.StringSetAsync()` overloads for source compat broken for 1 case in 2.5.61 ([#2098 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2098))
- Fix [#2086](https://github.com/StackExchange/StackExchange.Redis/issues/2086): Correct HashSlot calculations for `XREAD` and `XREADGROUP` commands ([#2093 by nielsderdaele](https://github.com/StackExchange/StackExchange.Redis/pull/2093))
- Adds: Support for `LCS` with `.StringLongestCommonSubsequence()`/`.StringLongestCommonSubsequence()`, `.StringLongestCommonSubsequenceLength()`/`.StringLongestCommonSubsequenceLengthAsync()`, and `.StringLongestCommonSubsequenceWithMatches()`/`.StringLongestCommonSubsequenceWithMatchesAsync()` ([#2104 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2104))
- Adds: Support for `OBJECT FREQ` with `.KeyFrequency()`/`.KeyFrequencyAsync()` ([#2105 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2105))
- Performance: Avoids allocations when computing cluster hash slots or testing key equality ([#2110 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2110))
- Adds: Support for `SORT_RO` with `.Sort()`/`.SortAsync()` ([#2111 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2111))
- Adds: Support for `BIT | BYTE` to `BITCOUNT` and `BITPOS` with `.StringBitCount()`/`.StringBitCountAsync()` and `.StringBitPosition()`/`.StringBitPositionAsync()` ([#2116 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2116))
- Adds: Support for pub/sub payloads that are unary arrays ([#2118 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/2118))
- Fix: Sentinel timer race during dispose ([#2133 by ewisuri](https://github.com/StackExchange/StackExchange.Redis/pull/2133))
- Adds: Support for `GT`, `LT`, and `CH` on `ZADD` with `.SortedSetAdd()`/`.SortedSetAddAsync()` and `.SortedSetUpdate()`/`.SortedSetUpdateAsync()` ([#2136 by Avital-Fine](https://github.com/StackExchange/StackExchange.Redis/pull/2136))
- Adds: Support for `COMMAND COUNT`, `COMMAND GETKEYS`, and `COMMAND LIST`, with `.CommandCount()`/`.CommandCountAsync()`, `.CommandGetKeys()`/`.CommandGetKeysAsync()`, and `.CommandList()`/`.CommandListAsync()` ([#2143 by shacharPash](https://github.com/StackExchange/StackExchange.Redis/pull/2143))

## 2.5.61

- Adds: `GETEX` support with `.StringGetSetExpiry()`/`.StringGetSetExpiryAsync()` ([#1743 by benbryant0](https://github.com/StackExchange/StackExchange.Redis/pull/1743))
- Fix [#1988](https://github.com/StackExchange/StackExchange.Redis/issues/1988): Don't issue `SELECT` commands if explicitly disabled ([#2023 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2023))
- Adds: `KEEPTTL` support on `SET` operations ([#2029 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2029))
- Fix: Allow `XTRIM` `MAXLEN` argument to be `0` ([#2030 by NicoAvanzDev](https://github.com/StackExchange/StackExchange.Redis/pull/2030))
- Adds: `ConfigurationOptions.BeforeSocketConnect` for configuring sockets between creation and connection ([#2031 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2031))
- Fix [#1813](https://github.com/StackExchange/StackExchange.Redis/issues/1813): Don't connect to endpoints we failed to parse ([#2042 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2042))
- Fix: `ClientKill`/`ClientKillAsync` when using `ClientType` ([#2048 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2048))
- Adds: Most `ConfigurationOptions` changes after `ConnectionMultiplexer` connections will now be respected, e.g. changing a timeout will work and changing a password for auth rotation would be used at the next reconnect ([#2050 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2050))
  - **Obsolete**: This change also moves `ConnectionMultiplexer.IncludeDetailInExceptions` and `ConnectionMultiplexer.IncludePerformanceCountersInExceptions` to `ConfigurationOptions`. The old properties are `[Obsolete]` proxies that work until 3.0 for compatibility.
- Adds: Support for `ZRANGESTORE` with `.SortedSetRangeAndStore()`/`.SortedSetRangeAndStoreAsync()` ([#2052 by slorello89](https://github.com/StackExchange/StackExchange.Redis/pull/2052))

## 2.5.43

- Adds: Bounds checking for `ExponentialRetry` backoff policy ([#1921 by gliljas](https://github.com/StackExchange/StackExchange.Redis/pull/1921))
- Adds: `DefaultOptionsProvider` support for endpoint-based defaults configuration ([#1987 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1987))
- Adds: Envoy proxy support ([#1989 by rkarthick](https://github.com/StackExchange/StackExchange.Redis/pull/1989))
- Performance: When `SUBSCRIBE` is disabled, give proper errors and connect faster ([#2001 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2001))
- Adds: `GET` on `SET` command support (present in Redis 6.2+ - [#2003 by martinekvili](https://github.com/StackExchange/StackExchange.Redis/pull/2003))
- Performance: Improves concurrent load performance when backlogs are utilized ([#2008 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2008))
- Stability: Improves cluster connections when `CLUSTER` command is disabled ([#2014 by tylerohlsen](https://github.com/StackExchange/StackExchange.Redis/pull/2014))
- Logging: Improves connection logging and adds overall timing to it ([#2019 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/2019))

## 2.5.27 (prerelease)

- Adds: a backlog/retry mechanism for commands issued while a connection isn't available ([#1912 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1912))
  - Commands will be queued if a multiplexer isn't yet connected to a Redis server.
  - Commands will be queued if a connection is lost and then sent to the server when the connection is restored.
  - All commands queued will only remain in the backlog for the duration of the configured timeout.
  - To revert to previous behavior, a new `ConfigurationOptions.BacklogPolicy` is available - old behavior is configured via `options.BacklogPolicy = BacklogPolicy.FailFast`. This backlogs nothing and fails commands immediately if no connection is available.
- Adds: Makes `StreamEntry` constructor public for better unit test experience ([#1923 by WeihanLi](https://github.com/StackExchange/StackExchange.Redis/pull/1923))
- Fix: Integer overflow error (issue #1926) with 2GiB+ result payloads ([#1928 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1928))
- Change: Update assumed redis versions to v2.8 or v4.0 in the Azure case ([#1929 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1929))
- Fix: Profiler showing `EVAL` instead `EVALSHA` ([#1930 by martinpotter](https://github.com/StackExchange/StackExchange.Redis/pull/1930))
- Performance: Moved tiebreaker fetching in connections into the handshake phase (streamline + simplification) ([#1931 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1931))
- Stability: Fixed potential disposed object usage around Arenas (pulling in [Piplines.Sockets.Unofficial#63](https://github.com/mgravell/Pipelines.Sockets.Unofficial/pull/63) by MarcGravell)
- Adds: Thread pool work item stats to exception messages to help diagnose contention ([#1964 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1964))
- Fix/Performance: Overhauls pub/sub implementation for correctness ([#1947 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1947))
  - Fixes a race in subscribing right after connected
  - Fixes a race in subscribing immediately before a publish
  - Fixes subscription routing on clusters (spreading instead of choosing 1 node)
  - More correctly reconnects subscriptions on connection failures, including to other endpoints
- Adds "(vX.X.X)" version suffix to the default client ID so server-side `CLIENT LIST` can more easily see what's connected ([#1985 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1985))
- Fix: Properly including or excluding key names on some message failures ([#1990 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1990))
- Fix: Correct return of nil results in `LPOP`, `RPOP`, `SRANDMEMBER`, and `SPOP` ([#1993 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1993))

## 2.2.88

- Change: Connection backoff default is now exponential instead of linear ([#1896 by lolodi](https://github.com/StackExchange/StackExchange.Redis/pull/1896))
- Adds: Support for `NodeMaintenanceScaleComplete` event (handles Redis cluster scaling) ([#1902 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1902))

## 2.2.79

- NRediSearch: Support on json index ([#1808 by AvitalFineRedis](https://github.com/StackExchange/StackExchange.Redis/pull/1808))
- NRediSearch: Support sortable TagFields and unNormalizedForm for Tag & Text Fields ([#1862 by slorello89 & AvitalFineRedis](https://github.com/StackExchange/StackExchange.Redis/pull/1862))
- Fix: Potential errors getting socket bytes ([#1836 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1836))
- Logging: Adds (.NET Version and timestamps) for better debugging ([#1796 by philon-msft](https://github.com/StackExchange/StackExchange.Redis/pull/1796))
- Adds: `Condition` APIs (transactions), now supports `StreamLengthEqual` and variants ([#1807 by AlphaGremlin](https://github.com/StackExchange/StackExchange.Redis/pull/1807))
- Adds: Support for count argument to `ListLeftPop`, `ListLeftPopAsync`, `ListRightPop`, and `ListRightPopAsync` ([#1850 by jjfmarket](https://github.com/StackExchange/StackExchange.Redis/pull/1850))
- Fix: Potential task/thread exhaustion from the backlog processor ([#1854 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1854))
- Adds: Support for listening to Azure Maintenance Events ([#1876 by amsoedal](https://github.com/StackExchange/StackExchange.Redis/pull/1876))
- Adds: `StringGetDelete`/`StringGetDeleteAsync` APIs for Redis `GETDEL` command([#1840 by WeihanLi](https://github.com/StackExchange/StackExchange.Redis/pull/1840))

## 2.2.62

- Stability: Sentinel potential memory leak fix in OnManagedConnectionFailed handler ([#1710 by alexSatov](https://github.com/StackExchange/StackExchange.Redis/pull/1710))
- Fix: `GetOutstandingCount` could obscure underlying faults by faulting itself ([#1792 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1792))
- Fix [#1719](https://github.com/StackExchange/StackExchange.Redis/issues/1791): With backlog messages becoming reordered ([#1779 by TimLovellSmith](https://github.com/StackExchange/StackExchange.Redis/pull/1779))

## 2.2.50

- Performance: Optimization for PING accuracy ([#1714 by eduardobr](https://github.com/StackExchange/StackExchange.Redis/pull/1714))
- Fix: Improvement to reconnect logic (exponential backoff) ([#1735 by deepakverma](https://github.com/StackExchange/StackExchange.Redis/pull/1735))
- Adds: Refresh replica endpoint list on failover ([#1684 by laurauzcategui](https://github.com/StackExchange/StackExchange.Redis/pull/1684))
- Fix: `ReconfigureAsync` re-entrancy (caused connection issues) ([#1772 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1772))
- Fix: `ReconfigureAsync` Sentinel race resulting in NoConnectionAvailable when using DemandMaster ([#1773 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1773))
- Stability: Resolve race in AUTH and other connection reconfigurations ([#1759 by TimLovellSmith and NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1759))

## 2.2.4

- Fix: Ambiguous signature of the new `RPUSHX`/`LPUSHX` methods ([#1620 by stefanloerwald](https://github.com/StackExchange/StackExchange.Redis/pull/1620))

## 2.2.3

- Adds: .NET 5 target
- Fix: Mutex race condition ([#1585 by arsnyder16](https://github.com/StackExchange/StackExchange.Redis/pull/1585))
- Adds: `CheckCertificateRevocation` can be controlled via the config string ([#1591 by lwlwalker](https://github.com/StackExchange/StackExchange.Redis/pull/1591))
- Fix: Range end-value inversion ([#1573 by tombatron](https://github.com/StackExchange/StackExchange.Redis/pull/1573))
- Adds: `ROLE` support ([#1551 by zmj](https://github.com/StackExchange/StackExchange.Redis/pull/1551))
- Adds: varadic `RPUSHX`/`LPUSHX` support ([#1557 by dmytrohridin](https://github.com/StackExchange/StackExchange.Redis/pull/1557))
- Fix: Server-selection strategy race condition ([#1532 by deepakverma](https://github.com/StackExchange/StackExchange.Redis/pull/1532))
- Fix: Sentinel default port ([#1525 by ejsmith](https://github.com/StackExchange/StackExchange.Redis/pull/1525))
- Fix: `Int64` parse scenario ([#1568 by arsnyder16](https://github.com/StackExchange/StackExchange.Redis/pull/1568))
- Add: Force replication check during failover ([#1563 by aravindyeduvaka & joroda](https://github.com/StackExchange/StackExchange.Redis/pull/1563))
- Documentation tweaks (multiple)
- Fix: Backlog contention issue ([#1612 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1612/), see also [#1574 by devbv](https://github.com/StackExchange/StackExchange.Redis/pull/1574/))

## 2.1.58

- Fix: `[*]SCAN` - fix possible NRE scenario if the iterator is disposed with an incomplete operation in flight
- Fix: `[*]SCAN` - treat the cursor as an opaque value whenever possible, for compatibility with `redis-cluster-proxy`
- Adds: `[*]SCAN` - include additional exception data in the case of faults

## 2.1.55

- Adds: Identification of assembly binding problem on .NET Framework. Drops `System.IO.Pipelines` to 4.7.1, and identifies new `System.Buffers` binding failure on 4.7.2

## 2.1.50

- Adds: Bind directly to sentinel-managed instances from a configuration string/object ([#1431 by ejsmith](https://github.com/StackExchange/StackExchange.Redis/pull/1431))
- Adds: `last-delivered-id` to `StreamGroupInfo` ([#1477 by AndyPook](https://github.com/StackExchange/StackExchange.Redis/pull/1477))
- Change: Update naming of replication-related commands to reflect Redis 5 naming ([#1488 by mgravell](https://github.com/StackExchange/StackExchange.Redis/issues/1488) & [#945 by mgravell](https://github.com/StackExchange/StackExchange.Redis/issues/945))
- Fix [#1460](https://github.com/StackExchange/StackExchange.Redis/issues/1460): `IServer` commands that are database-specific (`DBSIZE`, `FLUSHDB`, `KEYS`, `SCAN`) now respect the default database on the config ([#1468 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1468))
- Library updates

## 2.1.39

- Fix: Mutex around connection was not "fair"; in specific scenario could lead to out-of-order commands ([#1440 by kennygea](https://github.com/StackExchange/StackExchange.Redis/pull/1440))
- Fix [#1432](https://github.com/StackExchange/StackExchange.Redis/issues/1432): Update dependencies
- Fix: Timing error on linux ([#1433 by pengweiqhca](https://github.com/StackExchange/StackExchange.Redis/pull/1433))
- Fix: Add `auth` to command-map for Sentinel ([#1428 by ejsmith](https://github.com/StackExchange/StackExchange.Redis/pull/1428))

## 2.1.30

- Build: Fix deterministic builds ([#1420 by @mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1420))

## 2.1.28

- Fix: Stability in new sentinel APIs
- Fix [#1407](https://github.com/StackExchange/StackExchange.Redis/issues/1407): Include `SslProtocolos` in `ConfigurationOptions.ToString()` ([#1408 by vksampath and Sampath Vuyyuru](https://github.com/StackExchange/StackExchange.Redis/pull/1408))
- Fix: Clarify messaging around disconnected multiplexers ([#1396 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1396))
- Change: Tweak methods of new sentinel API (this is technically a breaking change, but since this is a new API that was pulled quickly, we consider this to be acceptable)
- Adds: New thread `SocketManager` mode (opt-in) to always use the regular thread-pool instead of the dedicated pool
- Adds: Improved counters in/around error messages
- Adds: New `User` property on `ConfigurationOptions`
- Build: Enable deterministic builds (note: this failed; fixed in 2.1.30)

## 2.1.0

- Fix: Ensure active-message is cleared ([#1374 by hamish-omny](https://github.com/StackExchange/StackExchange.Redis/pull/1374))
- Adds: Sentinel support ([#1067 by shadim](https://github.com/StackExchange/StackExchange.Redis/pull/1067), [#692 by lexxdark](https://github.com/StackExchange/StackExchange.Redis/pull/692))
- Adds: `IAsyncEnumerable<T>` scanning APIs now supported ([#1087 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1087))
- Adds: New API for use with misbehaving sync-contexts ([more info](https://stackexchange.github.io/StackExchange.Redis/ThreadTheft))
- Adds: `TOUCH` support ([#1291 by gkorland](https://github.com/StackExchange/StackExchange.Redis/pull/1291))
- Adds: `Condition` API (transactions) now supports `SortedSetLengthEqual` ([#1332 by phosphene47](https://github.com/StackExchange/StackExchange.Redis/pull/1332))
- Adds: `SocketManager` is now more configurable ([#1115 by naile](https://github.com/StackExchange/StackExchange.Redis/pull/1115))
- Adds: NRediSearch updated in line with JRediSearch ([#1267 by tombatron](https://github.com/StackExchange/StackExchange.Redis/pull/1267), [#1199 by oruchreis](https://github.com/StackExchange/StackExchange.Redis/pull/1199))
- Adds: Support for `CheckCertificatRevocation` configuration ([#1234 by BLun78 and V912736](https://github.com/StackExchange/StackExchange.Redis/pull/1234))
- Adds: More details about exceptions ([#1190 by marafiq](https://github.com/StackExchange/StackExchange.Redis/pull/1190))
- Adds: Updated `StreamCreateConsumerGroup` methods to use the `MKSTREAM` option ([#1141 via ttingen](https://github.com/StackExchange/StackExchange.Redis/pull/1141))
- Adds: Support for NOACK in the StreamReadGroup methods ([#1154 by ttingen](https://github.com/StackExchange/StackExchange.Redis/pull/1154))
- Adds: Event-args now mockable ([#1326 by n1l](https://github.com/StackExchange/StackExchange.Redis/pull/1326))
- Fix: No-op when adding 0 values to a set ([#1283 by omeaart](https://github.com/StackExchange/StackExchange.Redis/pull/1283))
- Adds: Support for `LATENCY` and `MEMORY` ([#1204 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1204))
- Adds: Support for `HSTRLEN` ([#1241 by eitanhs](https://github.com/StackExchange/StackExchange.Redis/pull/1241))
- Adds: `GeoRadiusResult` is now mockable ([#1175 by firenero](https://github.com/StackExchange/StackExchange.Redis/pull/1175))
- Fix: Various documentation fixes ([#1162 by SnakyBeaky](https://github.com/StackExchange/StackExchange.Redis/pull/1162), [#1135 by ttingen](https://github.com/StackExchange/StackExchange.Redis/pull/1135), [#1203 by caveman-dick](https://github.com/StackExchange/StackExchange.Redis/pull/1203), [#1240 by Excelan](https://github.com/StackExchange/StackExchange.Redis/pull/1240), [#1245 by francoance](https://github.com/StackExchange/StackExchange.Redis/pull/1245), [#1159 by odyth](https://github.com/StackExchange/StackExchange.Redis/pull/1159), [#1311 by DillonAd](https://github.com/StackExchange/StackExchange.Redis/pull/1311), [#1339 by vp89](https://github.com/StackExchange/StackExchange.Redis/pull/1339), [#1336 by ERGeorgiev](https://github.com/StackExchange/StackExchange.Redis/issues/1336))
- Fix: Rare race-condition around exception data ([#1342 by AdamOutcalt](https://github.com/StackExchange/StackExchange.Redis/pull/1342))
- Fix: `ScriptEvaluateAsync` keyspace isolation ([#1377 by gliljas](https://github.com/StackExchange/StackExchange.Redis/pull/1377))
- Fix: F# compatibility enhancements ([#1386 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1386))
- Fix: Improved `ScriptResult` null support ([#1392 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1392))
- Fix: Error with DNS resolution breaking endpoint iterator ([#1393 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1393))
- Tests: Better docker support for tests ([#1389 by ejsmith](https://github.com/StackExchange/StackExchange.Redis/pull/1389), [#1391 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1391))
- Tests: General test improvements ([#1183 by mtreske](https://github.com/StackExchange/StackExchange.Redis/issues/1183), [#1385 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1385), [#1384 by NickCraver](https://github.com/StackExchange/StackExchange.Redis/pull/1384))

## 2.0.601

- Adds: Tracking for current and next messages to help with debugging timeout issues - helpful in cases of large pipeline blockers

## 2.0.600

- Adds: `ulong` support to `RedisValue` and `RedisResult` ([#1104 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/1104))
- Fix: Remove odd equality: `"-" != 0` (we do, however, still allow `"-0"`, as that is at least semantically valid, and is logically `== 0`) (related to [#1103](https://github.com/StackExchange/StackExchange.Redis/issues/1103))
- Performance: Rework how pub/sub queues are stored - reduces delegate overheads (related to [#1101](https://github.com/StackExchange/StackExchange.Redis/issues/1101))
- Fix [#1108](https://github.com/StackExchange/StackExchange.Redis/issues/1108): Ensure that we don't try appending log data to the `TextWriter` once we've returned from a method that accepted one

## 2.0.593

- Performance: Unify spin-wait usage on sync/async paths to one competitor
- Fix [#1101](https://github.com/StackExchange/StackExchange.Redis/issues/1101): When a `ChannelMessageQueue` is involved, unsubscribing *via any route* should still unsubscribe and mark the queue-writer as complete

## 2.0.588

- Stability/Performance: Resolve intermittent stall in the write-lock that could lead to unexpected timeouts even when at low/reasonable (but concurrent) load

## 2.0.571

- Performance: Use new [arena allocation API](https://mgravell.github.io/Pipelines.Sockets.Unofficial/docs/arenas) to avoid `RawResult[]` overhead
- Performance: Massively simplified how `ResultBox<T>` is implemented, in particular to reduce `TaskCompletionSource<T>` allocations
- Performance: Fix sync-over-async issue with async call paths, and fix the [SemaphoreSlim](https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html) problems that this uncovered
- Performance: Reintroduce the unsent backlog queue, in particular to improve async performance
- Performance: Simplify how completions are reactivated, so that external callers use their originating pool, not the dedicated IO pools (prevent thread stealing)
- Fix: Update `Pipelines.Sockets.Unofficial` to prevent issue with incorrect buffer re-use in corner-case
- Fix: `KeyDeleteAsync` could, in some cases, always use `DEL` (instead of `UNLINK`)
- Fix: Last unanswered write time was incorrect
- Change: Use higher `Pipe` thresholds when sending

## 2.0.519

- Fix [#1007](https://github.com/StackExchange/StackExchange.Redis/issues/1007): Adapt to late changes in the RC streams API ([#983 by mgravell](https://github.com/StackExchange/StackExchange.Redis/pull/983))
- Documentation fixes ([#997 by MerelyRBLX](https://github.com/StackExchange/StackExchange.Redis/pull/997), [#1005 by zBrianW](https://github.com/StackExchange/StackExchange.Redis/pull/1005))
- Build: Switch to SDK 2.1.500

## 2.0.513

- Fix [#961](https://github.com/StackExchange/StackExchange.Redis/issues/962): fix assembly binding redirect problems; IMPORTANT: this drops to an older `System.Buffers` version - if you have manually added redirects for `4.0.3.0`, you may need to manually update to `4.0.2.0` (or remove completely)
- Fix [#962](https://github.com/StackExchange/StackExchange.Redis/issues/962): Avoid NRE in edge-case when fetching bridge

## 2.0.505

- Fix [#943](https://github.com/StackExchange/StackExchange.Redis/issues/943): Ensure transaction inner tasks are completed prior to completing the outer transaction task
- Fix [#946](https://github.com/StackExchange/StackExchange.Redis/issues/946): Reinstate missing `TryParse` methods on `RedisValue`
- Fix [#940](https://github.com/StackExchange/StackExchange.Redis/issues/940): Off-by-one on pre-boxed integer cache (NRediSearch)

## 2.0.495

2.0 is a large - and breaking - change. The key focus of this release is stability and reliability.

- **Hard Break**: The package identity has changed; instead of `StackExchange.Redis` (not strong-named) and `StackExchange.Redis.StrongName` (strong-named), we are now
  only releasing `StackExchange.Redis` (strong-named). This is a binary breaking change that requires consumers to be re-compiled; it cannot be applied via binding-redirects
- **Hard Break**: The platform targets have been rationalized - supported targets are .NETStandard 2.0 (and above), .NETFramework 4.6.1 (and above), and .NETFramework 4.7.2 (and above)
  (note - the last two are mainly due to assembly binding problems)
- **Hard Break**: The profiling API has been overhauled and simplified; full documentation is [provided here](https://stackexchange.github.io/StackExchange.Redis/Profiling_v2.html)
- **Soft Break**: The `PreserveAsyncOrder` behaviour of the pub/sub API has been deprecated; a *new* API has been provided for scenarios that require in-order pub/sub handling -
  the `Subscribe` method has a new overload *without* a handler parameter which returns a `ChannelMessageQueue`, which provides `async` ordered access to messages)
- Internal: The network architecture has moved to use `System.IO.Pipelines`; this has allowed us to simplify and unify a lot of the network code, and in particular fix a lot of problems relating to how the library worked with TLS and/or .NETStandard
- Change: As a result of the `System.IO.Pipelines` change, the error-reporting on timeouts is now much simpler and clearer; the [timeouts documentation](Timeouts.md) has been updated
- Removed: The `HighPriority` (queue-jumping) flag is now deprecated
- Internal: Most buffers internally now make use of pooled memory; `RedisValue` no longer preemptively allocates buffers
- Internal: Added new custom thread-pool for handling async continuations to avoid thread-pool starvation issues
- Internal: All IL generation has been removed; the library should now work on platforms that do not allow runtime-emit
- Adds: asynchronous operations now have full support for reporting timeouts
- Adds: new APIs now exist to work with pooled memory without allocations - `RedisValue.CreateFrom(MemoryStream)` and `operator` support for `Memory<byte>` and `ReadOnlyMemory<byte>`; and `IDatabase.StringGetLease[Async](...)`, `IDatabase.HashGetLease[Async](...)`, `Lease<byte>.AsStream()`)
- Adds: ["streams"](https://redis.io/topics/streams-intro) support (thanks to [ttingen](https://github.com/ttingen) for their contribution)
- Adds: Various missing commands / overloads have been added; `Execute[Async]` for additional commands is now available on `IServer`
- Fix: A *lot* of general bugs and issues have been resolved
- **Break**: `RedisValue.TryParse` was accidentally omitted in the overhaul; this has been rectified and will be available in the next build

A more complete list of issues addressed can be seen in [this tracking issue](https://github.com/StackExchange/StackExchange.Redis/issues/871)

Note: we currently have no plans to do an additional `1.*` release. In particular, even though there was a `1.2.7-alpha` build on nuget, we *do not* currently have plans to release `1.2.7`.

---

## 1.2.6

- Change: `cluster nodes` output when using cluster-enabled target and 4.0+ (see [redis #4186](https://github.com/antirez/redis/issues/4186)

## 1.2.5

- (Critical) Fix: "poll mode" was disabled in the build for `net45`/`net46` - Impact: IO jams and lack of reader during high load

## 1.2.4

- Fix: Incorrect build configuration ([#649 by jrlost](https://github.com/StackExchange/StackExchange.Redis/issues/649))

## 1.2.3

- Fix: When using `redis-cluster` with multiple replicas, use round-robin when selecting replica ([#610 by mgravell](https://github.com/StackExchange/StackExchange.Redis/issues/610))
- Adds: Can specify `NoScriptCache` flag when using `ScriptEvaluate` to bypass all cache features (always uses `EVAL` instead of `SCRIPT LOAD` and `EVALSHA`) ([#617 by Funbit](https://github.com/StackExchange/StackExchange.Redis/issues/617))

## 1.2.2 (preview)

- **Break**: .NET 4.0 support is not in this build, due to [a build issue](https://github.com/dotnet/cli/issues/5993) - looking into solutions
- Adds: Make performance-counter tracking opt-in (`IncludePerformanceCountersInExceptions`) as it was causing problems ([#587 by AlexanderKot](https://github.com/StackExchange/StackExchange.Redis/issues/587))
- Adds: Can now specifiy allowed SSL/TLS protocols  ([#603 by JonCole](https://github.com/StackExchange/StackExchange.Redis/pull/603))
- Adds: Track message status in exceptions ([#576 by deepakverma](https://github.com/StackExchange/StackExchange.Redis/pull/576))
- Adds: `GetDatabase()` optimization for DB 0 and low numbered databases: `IDatabase` instance is retained and recycled (as long as no `asyncState` is provided)
- Performance: Improved connection retry policy ([#510 by deepakverma](https://github.com/StackExchange/StackExchange.Redis/pull/510), [#572 by deepakverma](https://github.com/StackExchange/StackExchange.Redis/pull/572))
- Adds: `Execute`/`ExecuteAsync` API to support "modules"; [more info](https://blog.marcgravell.com/2017/04/stackexchangeredis-and-redis-40-modules.html)
- Fix: Timeout link fixed re /docs change (below)
- [`NRediSearch`](https://www.nuget.org/packages/NRediSearch/) added as exploration into "modules"
- Other changes (not library related)
  - Change: Refactor /docs for github pages
  - Change: Improve release note tracking
  - Build: Rework build process to use csproj

## 1.2.1

- Fix: Avoid overlapping per-endpoint heartbeats

## 1.2.0 (same as 1.2.0-alpha1)

- Adds: GEO commands ([#489 by wjdavis5](https://github.com/StackExchange/StackExchange.Redis/pull/489))
- Adds: ZADD support for new NX/XX switches ([#520 by seniorquico](https://github.com/StackExchange/StackExchange.Redis/pull/520))
- Adds: core-clr preview support improvements

## 1.1.608

- Fix: Bug with race condition in servers indexer (related: 1.1.606)

## 1.1.607

- Fix: Ensure socket-mode polling is enabled (.net)

## 1.1.606

- Fix: Bug with race condition in servers indexer

## ...and the rest

(We're happy to take PRs for change history going back in time or any fixes here!)
