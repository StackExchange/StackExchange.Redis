# RESP3 and StackExchange.Redis

RESP2 and RESP3 are evolutions of the Redis protocol, with RESP3 existing from Redis server version 6 onwards (v7.2+ for Redis Enterprise). The main differences are:

1. RESP3 can carry out-of-band / "push" messages on a single connection, where-as RESP2 requires a separate connection for these messages
2. RESP3 can (when appropriate) convey additional semantic meaning about returned payloads inside the same result structure
3. Some commands (see [this topic](https://github.com/redis/redis-doc/issues/2511)) return different result structures in RESP3 mode; for example a flat interleaved array might become a jagged array

For most people, #1 is the main reason to consider RESP3, as in high-usage servers - this can halve the number of connections required.
This is particularly useful in hosted environments where the number of inbound connections to the server is capped as part of a service plan.
Alternatively, where users are currently choosing to disable the out-of-band connection to achieve this, they may now be able to re-enable this
(for example, to receive server maintenance notifications) *without* incurring any additional connection overhead.

Because of the significance of #3 (and to avoid breaking your code), the library does not currently default to RESP3 mode. This must be enabled explicitly
via `ConfigurationOptions.Protocol` or by adding `,protocol=resp3` (or `,protocol=3`) to the configuration string.

---

#3 is a critical one - the library *should* already handle all documented commands that have revised results in RESP3, but if you're using
`Execute[Async]` to issue ad-hoc commands, you may need to update your processing code to compensate for this, ideally using detection to handle
*either* format so that the same code works in both REP2 and RESP3. Since the impacted commands are handled internally by the library, in reality
this should not usually present a difficulty.

The minor (#2) and major (#3) differences to results are only visible to your code when using:

- Lua scripts invoked via the `ScriptEvaluate[Async](...)` or related APIs, that either:
  - Uses the `redis.setresp(3)` API and returns a value from `redis.[p]call(...)`
  - Returns a value that satisfies the [LUA to RESP3 type conversion rules](https://redis.io/docs/manual/programmability/lua-api/#lua-to-resp3-type-conversion)
- Ad-hoc commands (in particular: *modules*) that are invoked via the `Execute[Async](string command, ...)` API

...both which return `RedisResult`. **If you are not using these APIs, you should not need to do anything additional.**

Historically, you could use the `RedisResult.Type` property to query the type of data returned (integer, string, etc). In particular:

- Two new properties are added: `RedisResult.Resp2Type` and `RedisResult.Resp3Type`
  - The `Resp3Type` property exposes the new semantic data (when using RESP3) - for example, it can indicate that a value is a double-precision number, a boolean, a map, etc (types that did not historically exist)
  - The `Resp2Type` property exposes the same value that *would* have been returned if this data had been returned over RESP2
  - The `Type` property is now marked obsolete, but functions identically to `Resp2Type`, so that pre-existing code (for example, that has a `switch` on the type) is not impacted by RESP3
- The `ResultType.MultiBulk` is superseded by `ResultType.Array` (this is a nomenclature change only; they are the same value and function identically)

Possible changes required due to RESP3:

1. To prevent build warnings, replace usage of `ResultType.MultiBulk` with `ResultType.Array`, and usage of `RedisResult.Type` with `RedisResult.Resp2Type`
2. If you wish to exploit the additional semantic data when enabling RESP3, use `RedisResult.Resp3Type` where appropriate
3. If you are enabling RESP3, you must verify whether the commands you are using can give different result shapes on RESP3 connections