Ad-hoc commands
===

If you are *shipping a library* of such commands rather than calling one, see [Extending the client](Extending), which starts here and goes on to a surface of your own.

Sometimes you need to send a command that doesn't (yet) have a dedicated API - typically for a module, or a brand-new server feature the client hasn't caught up with. For new code, write it as an interpolated string and send it through the database's context: `db.Context.SendAsync<T>($"...")`. The `IDatabase` forms are `ExecuteResp(Async)`, the low-allocation-friendly overload, and `Execute(Async)`, the original `object[]`/`ICollection<object>`-based overload; both are covered further down, and neither is going away.

Basic use
---

```csharp
using ConnectionMultiplexer conn = /* init code */;
IDatabase db = conn.GetDatabase();

RedisKey key = "mykey";
RedisValue value = await db.Context.SendAsync<RedisValue>($"GET {key}");
```

The interpolated string is never built as a `string`: each hole is written straight into a pooled buffer as UTF-8, so there is no argument array and no boxing. Literal text between the holes is split on whitespace, and the first token is the command name. Known command names go through the command map (renaming and disabling) like any built-in command.

`SendAsync` hangs off `db.Context` rather than `db` itself: the context is what carries the database number, any key prefix, and the connection, and it is the same thing the command groups (`db.Strings`, ...) are built on.

**The type of each hole decides whether it is a key.** A `RedisKey` hole is a key - it takes part in cluster slot routing, takes the key prefix of a `WithKeyPrefix` database, and is what a client-side cache invalidates on. `RedisValue`, numbers, and the rest are values. The trap is a key held as a `string`: `$"{someString}"` binds as a *value*, so it must be written `(RedisKey)someString`, or it silently loses its prefix and its slot:

```csharp
string name = "mykey";
await db.Context.SendAsync<RedisValue>($"GET {(RedisKey)name}"); // a key: routed and prefixed
await db.Context.SendAsync<RedisValue>($"GET {name}");           // NOT a key: no slot, no prefix
```

On a single non-clustered server with no key prefix the two behave identically, which is exactly why the second one survives testing.

Name the reply type you want - `RedisValue`, `long`, `bool`, `double`, `string`, `ReadOnlyLease<T>`, or `RespResult` for the raw reply (see [Reading the result](#reading-the-result) below, and dispose it). `CommandFlags` and a `CancellationToken` follow the string:

```csharp
using RespResult reply = await db.Context.SendAsync<RespResult>(
    $"HGETALL {key}", CommandFlags.None, cancellationToken: cancellationToken);
```

Two things worth knowing before you use this on a hot path or behind a retry policy:

- literal tokens are parsed and encoded on every call, and the analyzer says so ([SER309](rules/SER309)); for a command you send often, declare it once as a `RespCommand` and use it as a hole - see [Extending the client](Extending#the-interpolated-string-is-not-a-string)
- the client has no retry category for a command it does not know, so it assumes the worst and will not replay it; if a replay is safe, say so with `CommandFlags.CommandRetryReadOnly` (or `flags.WithRetryCategory(...)`) - see [Extending the client](Extending#say-whether-your-command-can-be-retried)

If you are building more than an occasional call - a set of module commands, say - [Extending the client](Extending) takes this same call and gives it a surface of its own.

Building a command in pieces: `Compose`
---

A single interpolated string needs every argument at the call site. When some are only known at run time - an optional modifier, a variable number of keys - start the command with `Compose`, append the rest, and send it:

```csharp
// declared once: a command name in a field is encoded once, not on every call (see SER309)
private static readonly RespCommand Set = "SET".Command(), Del = "DEL".Command();

// Compose and its SendAsync live on the untyped context
RespContext ctx = (RespContext)db.Context;

// an optional argument
var set = ctx.Compose($"{Set} {key} {value}");
if (ttl is { } t)
{
    set.AppendFormatted((RedisValue)"EX");
    set.AppendFormatted((long)t.TotalSeconds);
}
bool ok = await ctx.SendAsync<bool>(ref set);

// a variable number of keys
var del = ctx.Compose($"{Del}");
foreach (RedisKey k in keys) del.AppendFormatted(k);
long removed = await ctx.SendAsync<long>(ref del);
```

- **Each `AppendFormatted` adds one argument**, typed exactly as a hole would be: a `RedisKey` is a key (routed, prefixed, and what a client-side cache invalidates on); anything else is a value.
- **The argument count need not be known up front.** The `*N` that begins a RESP command is filled in when the command is sent.
- **Send it from the context you composed it on.** The builder has already applied that context's key prefix and command map; sending it through a different context would mix the two.
- **Send it once.** Sending consumes it. If something between `Compose` and the send can throw, use `try`/`finally` and call `Dispose()` on it - it cannot be a `using` variable, because it is passed by `ref`.
- **For a command whose arguments are all known**, the single-expression `db.Context.SendAsync<T>($"...")` above is simpler and just as cheap; `Compose` is for when they are not.

`ExecuteResp`: the `IDatabase` form
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

`ExecuteResp` returns a `RespResult` (as does `SendAsync<RespResult>`) - a leased, undecoded view over the raw reply, backed by a pooled buffer rather than a fresh allocation per call. This is the more general form of the low-allocation pattern also used by [`ScriptEvaluateResp`](Scripting) - it's how you'd fetch a large blob value via an ad-hoc command without materializing a `RedisResult` wrapper on every call:

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

The interpolated form reads its reply the same way - name `RespResult` as the reply type - and needs no argument array at all. For a single scalar (blob) reply, reading it via `ExecuteResp`/`ScriptEvaluateResp` + `ReadLease()`/`CopyTo()` instead of the classic `Execute`/`ScriptEvaluate` + `(byte[])result` measured at roughly **50-95% less client-side allocation per call**, scaling up with the size of the blob (the old path always allocates a fresh array sized to the payload; the new path reuses a pooled one).

Leasing the argument buffer
---

The interpolated form has no argument collection, so this section is about `ExecuteResp`. The examples above allocate a fresh `RedisKeyOrValue[]` per call, which rather defeats the point of an API whose main selling point is low allocation. On a hot path, rent the array from `ArrayPool<RedisKeyOrValue>.Shared` instead - and you can return it as soon as the call returns:

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

`Execute(string command, params object[] args)` / `Execute(string command, ICollection<object> args, CommandFlags flags)` predate `RedisKeyOrValue` and `ExecuteResp`. They accept a loosely-typed bag of `object`s (each boxed to `RedisKey`/`RedisValue`/etc. internally) and always return a fully-materialized `RedisResult`. They still work and aren't going away, but for new code prefer `db.Context.SendAsync<T>($"...")`, or `ExecuteResp`/`ExecuteRespAsync` where you need the `IDatabase` form - typed `RedisKeyOrValue` args, no boxing, and low-allocation on the read side.
