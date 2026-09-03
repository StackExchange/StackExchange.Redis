Ad-hoc commands
===

`IDatabase.ExecuteResp(Async)` and `IDatabase.Execute(Async)` let you send a command that doesn't (yet) have a dedicated API - typically for a module, or a brand-new server feature the client hasn't caught up with. `ExecuteResp` is the modern, low-allocation-friendly overload; `Execute` is the original, `object[]`/`ICollection<object>`-based overload, kept for compatibility.

Basic use
---

`ExecuteResp` takes the command name and a single `ReadOnlyMemory<RedisKeyOrValue>` of arguments, in whatever order the command itself expects them - it's the command, not the API, that decides where keys fall in the argument list (unlike [`ScriptEvaluateResp`](Scripting), where Lua's `KEYS`/`ARGV` never interleave, an arbitrary command can place keys anywhere, so a single ordered collection is used rather than two separate ones). Wrap each argument as a key or a value to match what the command expects at that position:

```csharp
using ConnectionMultiplexer conn = /* init code */;
var db = conn.GetDatabase();

// note: see "Leasing the argument buffer" below
using RespResult result = db.ExecuteResp("GET", new RedisKeyOrValue[] { (RedisKey)"mykey" });
// ...
```

Keys passed this way participate in cluster slot routing and `KeyPrefixed` key-prefixing, just like a key argument to any built-in command. `ExecuteResp` recognizes known command names (applying the same command-map renaming/disabling rules as everything else) and falls back to treating the command as opaque only if it isn't recognized.

The `new RedisKeyOrValue[]` above is fine for occasional use, but allocates on every call - see [Leasing the argument buffer](#leasing-the-argument-buffer) below for the low-allocation form once you're on a hot path.

Reading the result
---

`ExecuteResp` returns a `RespResult` - a leased, undecoded view over the raw reply, backed by a pooled buffer rather than a fresh allocation per call. This is the more general form of the low-allocation pattern also used by [`ScriptEvaluateResp`](Scripting) - it's how you'd fetch a large blob value via an ad-hoc command without materializing a `RedisResult` wrapper on every call:

```csharp
// note: see "Leasing the argument buffer" below
using RespResult result = db.ExecuteResp("GET", new RedisKeyOrValue[] { (RedisKey)"mykey" });
if (!result.IsNull)
{
    RedisValue value = result.ReadScalar().ReadRedisValue();
    // use value ...
}
// on a genuinely hot path, ReadScalar().ScalarLength() + .CopyTo(yourBuffer) avoids
// even that allocation, by copying straight into a buffer you already own
```

See [Scripting](Scripting#reading-the-result-respresult-vs-redisresult) for the full rundown of `RespResult` - `IsNull`/`Prefix`, `ReadScalar()`/`Read()`, and the `ReadRedisValue`/`ReadLease`/`ReadRedisResult` accessors - it applies identically here; `ExecuteResp` and `ScriptEvaluateResp` share the same response-reading API, only the request differs (a command name instead of a script).

Measured effect
---

For a single scalar (blob) reply, reading it via `ExecuteResp`/`ScriptEvaluateResp` + `ReadLease()`/`CopyTo()` instead of the classic `Execute`/`ScriptEvaluate` + `(byte[])result` measured at roughly **50-95% less client-side allocation per call**, scaling up with the size of the blob (the old path always allocates a fresh array sized to the payload; the new path reuses a pooled one).

Leasing the argument buffer
---

The examples above allocate a fresh `RedisKeyOrValue[]` per call, which rather defeats the point of an API whose main selling point is low allocation. On a hot path, rent the array from `ArrayPool<RedisKeyOrValue>.Shared` instead - but the buffer can only be recycled once you know the server has fully received the write. For a synchronous call, that's once `ExecuteResp` has returned successfully *or* thrown `RedisServerException` (the server still definitely got the command - it just responded with an error). Any other exception (a timeout, a dropped connection) can mean a retry is still using the same buffer, so don't recycle it in that case:

```csharp
var args = ArrayPool<RedisKeyOrValue>.Shared.Rent(1); // usually larger!
args[0] = (RedisKey)"mykey";
var canReturn = true;
try
{
    using RespResult result = db.ExecuteResp("GET", args.AsMemory(0, 1));
    // use result...
}
catch (RedisServerException)
{
    throw; // the server responded - still safe to recycle below
}
catch
{
    canReturn = false; // e.g. a timeout/connection failure - a retry may still need this buffer
    throw;
}
finally
{
    if (canReturn) ArrayPool<RedisKeyOrValue>.Shared.Return(args, clearArray: true);
}
```

The same rule applies to `ExecuteRespAsync` - just `await` the call before deciding whether it's safe to return the lease.

The original `Execute`/`ExecuteAsync` overload
---

`Execute(string command, params object[] args)` / `Execute(string command, ICollection<object> args, CommandFlags flags)` predate `RedisKeyOrValue` and `ExecuteResp`. They accept a loosely-typed bag of `object`s (each boxed to `RedisKey`/`RedisValue`/etc. internally) and always return a fully-materialized `RedisResult`. They still work and aren't going away, but for new code prefer `ExecuteResp`/`ExecuteRespAsync` - typed `RedisKeyOrValue` args, no boxing, and low-allocation on the read side.
