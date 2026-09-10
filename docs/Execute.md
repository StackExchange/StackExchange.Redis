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

See [Scripting](Scripting#reading-the-result-respresult-vs-redisresult) for the full rundown of `RespResult` - `IsNull`/`IsScalar`/`IsAggregate`, `ReadScalar()`/`Read()`, and the `ReadRedisValue`/`ReadLease`/`ReadRedisResult` accessors - it applies identically here; `ExecuteResp` and `ScriptEvaluateResp` share the same response-reading API, only the request differs (a command name instead of a script).

Test the shape of a reply with the category tests (`IsScalar`, `IsAggregate`, `IsNull`), not by comparing `Prefix` to a specific `RespPrefix` - see [Testing what came back](Scripting#testing-what-came-back). Ad-hoc commands are where this bites hardest, because you are handed whatever the command actually returns: `HGETALL` and `CONFIG GET` are arrays under RESP2 and maps under RESP3, `SMEMBERS` is an array under RESP2 and a set under RESP3, and so on. `IsAggregate` covers all of those; `Prefix == RespPrefix.Array` covers only the RESP2 spelling.

Measured effect
---

For a single scalar (blob) reply, reading it via `ExecuteResp`/`ScriptEvaluateResp` + `ReadLease()`/`CopyTo()` instead of the classic `Execute`/`ScriptEvaluate` + `(byte[])result` measured at roughly **50-95% less client-side allocation per call**, scaling up with the size of the blob (the old path always allocates a fresh array sized to the payload; the new path reuses a pooled one).

Leasing the argument buffer
---

The examples above allocate a fresh `RedisKeyOrValue[]` per call, which rather defeats the point of an API whose main selling point is low allocation. On a hot path, rent the array from `ArrayPool<RedisKeyOrValue>.Shared` instead - and you can return it as soon as the call returns:

```csharp
var args = ArrayPool<RedisKeyOrValue>.Shared.Rent(1); // usually larger!
try
{
    args[0] = (RedisKey)"mykey";
    using RespResult result = db.ExecuteResp("GET", args.AsMemory(0, 1));
    // use result...
}
finally
{
    ArrayPool<RedisKeyOrValue>.Shared.Return(args, clearArray: true);
}
```

No conditions, and nothing to work out from the exception that came back: the arguments are rendered into the request before the call returns, so the library is not looking at your array afterwards. That holds for a call that succeeds, one that throws for any reason at all, and a fire-and-forget call that never waits for a reply.

The async form is the same, and does *not* need the `await` to happen first - `ExecuteRespAsync` renders the arguments before it hands back the task, so the buffer is yours again the moment the method returns:

```csharp
var args = ArrayPool<RedisKeyOrValue>.Shared.Rent(1);
Task<RespResult> pending;
try
{
    args[0] = (RedisKey)"mykey";
    pending = db.ExecuteRespAsync("GET", args.AsMemory(0, 1));
}
finally
{
    ArrayPool<RedisKeyOrValue>.Shared.Return(args, clearArray: true); // before awaiting, deliberately
}

using RespResult result = await pending;
```

That also means one buffer can be refilled and reused across a whole run of queued calls - a batch, say - without waiting for any of them:

```csharp
var batch = db.CreateBatch();
var args = ArrayPool<RedisKeyOrValue>.Shared.Rent(3);
var pending = new List<Task<RespResult>>();
for (int i = 0; i < count; i++)
{
    args[0] = key;
    args[1] = (RedisValue)("field" + i);
    args[2] = (RedisValue)("value" + i);
    pending.Add(batch.ExecuteRespAsync("HSET", args.AsMemory(0, 3))); // rendered here, not at Execute()
}

ArrayPool<RedisKeyOrValue>.Shared.Return(args, clearArray: true);
batch.Execute();
await Task.WhenAll(pending);
```

One caveat, for values built over memory you own: a `RedisValue` can wrap a `ReadOnlyMemory<byte>`, and rendering copies the *value*, not the bytes behind it. Returning the `RedisKeyOrValue[]` is safe; overwriting the byte buffer a `RedisValue` points at, before the request has been written, is not. Values built from `string`, `byte[]` you don't then mutate, or numbers are unaffected.

This last gap is known, and is expected to close in a future update: the same work that moves serialization onto the calling thread consumes the payload bytes before the call returns too, at which point the rule becomes simply "everything you passed is yours again when the call returns", with no exception for memory-backed values.

The original `Execute`/`ExecuteAsync` overload
---

`Execute(string command, params object[] args)` / `Execute(string command, ICollection<object> args, CommandFlags flags)` predate `RedisKeyOrValue` and `ExecuteResp`. They accept a loosely-typed bag of `object`s (each boxed to `RedisKey`/`RedisValue`/etc. internally) and always return a fully-materialized `RedisResult`. They still work and aren't going away, but for new code prefer `ExecuteResp`/`ExecuteRespAsync` - typed `RedisKeyOrValue` args, no boxing, and low-allocation on the read side.
