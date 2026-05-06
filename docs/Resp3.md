# RESP3 and StackExchange.Redis

RESP2 and RESP3 are evolutions of the Redis protocol, with RESP3 existing from Redis server version 6 onwards (v7.2+ for Redis Enterprise). The main differences are:

1. RESP3 can carry out-of-band / "push" messages on a single connection, where-as RESP2 requires a separate connection for out-of-band (pub/sub) messages
    - this single connection can be of huge benefit in high-usage servers, as it halves the number of connections required
2. RESP3 supports *additional* out-of-band messages that cannot be expressed in RESP2, which allows advanced features such as "smart client handoffs" (a family of
   server maintenance notifications)
    - these features (not yet implemented in SE.Redis) allow for greater stability in complex deployments
3. RESP3 can (when appropriate) convey additional semantic meaning about returned payloads inside the same result structure
    - this is *mostly* relevant to client libraries that do not explicitly interpret the results before exposing to the user, so this does not directly impact SE.Redis itself,
      but it is relevant to consumers of SE.Redis that use Lua scripts or ad-hoc commands

For many users, using RESP3 is a "no-brainer" - it offers significant benefits with no real downsides. However, there are some important things to be aware of, and some
migration work that may be required. In particular, some commands *return different result structures* in RESP3 mode; for example a jagged (nested) array might become a "map"
(essentially an interleaved flat array). SE.Redis has been updated to handle these cases transparently, but if you are using `Execute[Async]` or `ScriptEvaluate[Async]` (or if
you are using an additional library that issues ad-hoc commands or scripts on your behalf) you may need to update your processing code to compensate for this. This is discussed more below.

# Enabling RESP3

RESP2 and RESP3 are both supported options (if the server does not support RESP3, RESP2 will always be used). To make full use of the benefits of RESP3,
the library is moving in the direction of *preferring* RESP3. The default behaviour is:

| Library version         | Endpoint                                                        | Default protocol
|-------------------------|-----------------------------------------------------------------|-
| &lt; 2.13               | (any)                                                           | RESP2
| &gt;= 2.13 and &lt; 3.0 | (non-AMR)                                                       | RESP2
| &gt;= 2.13 and &lt; 3.0 | [AMR](https://azure.microsoft.com/products/managed-redis) | RESP3
| &gt; 3.0<sup>†</sup>    | (any)                                                           | RESP3

<sup>†</sup> = planned

You can override this behaviour by setting the `protocol` option in the connection string, or by setting the `ConfigurationOptions.Protocol` property:

```csharp
var options = ConfigurationOptions.Parse("someserver");
options.Protocol = RedisProtocol.Resp3; // or .Resp2
var muxer = await ConnectionMultiplexer.ConnectAsync(options);
```

or

```csharp
var options = ConfigurationOptions.Parse("someserver,protocol=resp3"); // or =resp2
var muxer = await ConnectionMultiplexer.ConnectAsync(options);
```

You can use this configuration to *explicitly enable* RESP3 on earlier library versions, or to *explicitly disable* RESP3 on later versions, if you encounter issues.

# Handling RESP3

For most users, *no additional work will be required*, or the additional work may be limited to updating libraries; for example, For example, [NRedisStack](https://www.nuget.org/packages/NRedisStack/)
now fully supports RESP3 for the commands it exposes (search, json, time-series, etc).

Scenarios impacted by RESP3 include:

- Lua scripts invoked via the `ScriptEvaluate[Async](...)` or related APIs, that either:
  - Uses the `redis.setresp(3)` API and returns a value from `redis.[p]call(...)`
  - Returns a value that satisfies the [LUA to RESP3 type conversion rules](https://redis.io/docs/manual/programmability/lua-api/#lua-to-resp3-type-conversion)
- Ad-hoc commands that are invoked via the `Execute[Async](string command, ...)` API

This delta is *especially* pronounced for some of the "modules" in Redis, even those that now ship by default in OSS Redis, including:
- "search" (`FT.SEARCH`, `FT.AGGREGATE`, etc.)
- "time-series" (`TS.RANGE`, etc.)
- "json" (`JSON.NUMINCRBY`, etc.)

Note that NRedisStack wraps most of these common modules, and has been updated to understand RESP3; if you are using these modules via NRedisStack, you should update to the latest version; if
you are using these modules via ad-hoc commands, you may need to update your processing code to compensate for this, or consider using NRedisStack instead, which will handle the RESP3 conversion for you.

This leaves a small category of users who are currently using the `RedisResult` type directly (via `Execute[Async](...)` or `ScriptEvaluate[Async](...)`).

## Impact on RedisResult

Firstly, note that it is possible that the *structure* of the data changes between RESP2 and RESP3; for example, a jagged array might become a map, or a single string value might become an array. You will
need to identify these changes (typically via integration tests) and update your code accordingly, ideally with detection code to handle *either* structure so that the same code works in both REP2 and RESP3.

This is usually combined by using the `RedisResult.Resp3Type` property to query the type of data returned (integer, string, etc). Historically, you could use the `RedisResult.Type` property to query the type of data returned (integer, string, etc).
With RESP3, this is extended:

- Two new properties are added: `RedisResult.Resp2Type` and `RedisResult.Resp3Type`
  - The `Resp3Type` property exposes the new semantic data (when using RESP3) - for example, it can indicate that a value is a double-precision number, a boolean, a map, etc (types that did not historically exist)
  - The `Resp2Type` property exposes the same value that *would* have been returned if this data had been returned over RESP2
  - The `Type` property is now marked obsolete, but functions identically to `Resp2Type`, so that pre-existing code (for example, that has a `switch` on the type) is not impacted by RESP3
- The `ResultType.MultiBulk` is superseded by `ResultType.Array` (this is a nomenclature change only; they are the same value and function identically)

Possible changes required due to RESP3:

1. To prevent build warnings, replace usage of `ResultType.MultiBulk` with `ResultType.Array`, and usage of `RedisResult.Type` with `RedisResult.Resp2Type`
2. If you wish to exploit the additional semantic data when enabling RESP3, use `RedisResult.Resp3Type` where appropriate
3. If you are enabling RESP3, you must verify whether the commands you are using can give different result shapes on RESP3 connections

An example of the types of changes required may be seen in the [NRedisStack #471](https://github.com/redis/NRedisStack/pull/471) pull-request, which updates result processing for multiple modules
(and changes the integration tests to run on RESP2 and RESP3 separately).
