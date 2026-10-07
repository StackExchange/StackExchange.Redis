Scripting
===

[Lua scripting](https://redis.io/commands/EVAL) lets you run a script server-side, atomically, in one round trip. StackExchange.Redis exposes this through `db.Scripts.EvaluateAsync(script, keys, args)` (and the read-only `EvaluateReadOnlyAsync` twin), plus the `IServer.ScriptLoad(Async)`/`ScriptExists(Async)`/`ScriptFlush(Async)` support commands.

The `IDatabase` forms - `ScriptEvaluateResp(Async)`/`ScriptEvaluateReadOnlyResp(Async)`, and the classic `ScriptEvaluate(Async)` - are unchanged and are not going anywhere; they are covered below where they differ. See [The original `IDatabase` API](LegacyApi).

Basic use
---

`EvaluateAsync` takes the script text, the keys (available to the script as `KEYS`), and the values (available as `ARGV`) as two separate `ReadOnlySpan<RedisKey>`/`ReadOnlySpan<RedisValue>` parameters - kept separate deliberately, since the script indexes them separately too (`KEYS[1]`, `ARGV[1]`, ...); there's no benefit to the caller in combining them into one collection:

```csharp
using ConnectionMultiplexer conn = /* init code */;
IDatabase db = conn.GetDatabase();

using RespResult result = await db.Scripts.EvaluateAsync(
    "return redis.call('set', KEYS[1], ARGV[1])",
    ["mykey"],
    [123]);
```

Both are optional, so a script with no inputs is just `using RespResult result = await db.Scripts.EvaluateAsync("return 42");`.

Keys participate in cluster slot routing and (if you're using `WithKeyPrefix`) key-prefixing, exactly like any other command's keys - values do not. **Every key the script touches must be passed as a key**: that is the server's rule for routing a script in a cluster, and it is also what a client-side cache invalidates on. The script itself is cached automatically: the client computes its SHA1 hash and sends `EVALSHA`, preceded - only when the server is not already known to hold the script - by a `SCRIPT LOAD` in the same write, so the body crosses the wire once. If the server reports `NOSCRIPT` anyway (after a `SCRIPT FLUSH`, a restart, or a failover), the body is re-sent transparently. Inside a batch or transaction the body is sent with `EVAL` instead.

`EvaluateReadOnlyAsync` is the same shape, for the [`EVAL_RO`/`EVALSHA_RO`](https://redis.io/commands/eval_ro) read-only variant, which can't run write commands and so is eligible to run against a replica.

The `IDatabase` equivalent is `ScriptEvaluateResp(Async)` (and `ScriptEvaluateReadOnlyResp(Async)`), which takes `ReadOnlyMemory<RedisKey>`/`ReadOnlyMemory<RedisValue>` and returns the same `RespResult`; its first call sends the full script text (`EVAL`), and later calls send only the hash (`EVALSHA`), with the same `NOSCRIPT` recovery:

```csharp
using RespResult result = db.ScriptEvaluateResp(
    "return redis.call('set', KEYS[1], ARGV[1])",
    new RedisKey[] { "mykey" },
    new RedisValue[] { 123 });
```

Reading the result: `RespResult` vs `RedisResult`
---

`db.Scripts.EvaluateAsync` and `ScriptEvaluateResp` both return a `RespResult`: a leased, undecoded view over the raw reply, backed by a pooled buffer rather than a fresh allocation per call. You `using` it to return the buffer once you're done. This is in contrast to the classic `ScriptEvaluate` (no `Resp` suffix, taking `RedisKey[]?`/`RedisValue[]?` arrays), which returns a `RedisResult` - a fully-materialized, general-purpose tree that's easy to cast (`(string)`, `(long)`, `(RedisValue[])`, etc.) but that always allocates: a wrapper object per node, plus a decoded value per scalar. There is no `RedisResult`-returning form on `db.Scripts`; the casts become reads from the `RespResult`. Prefer the `RespResult` forms for new code, especially when the result is a single scalar (the common case, especially for a blob payload):

```csharp
using RespResult result = await db.Scripts.EvaluateAsync("return redis.call('get', KEYS[1])", ["mykey"]);
if (!result.IsNull)
{
    var reader = result.ReadScalar();

    // cheapest: copy straight into a buffer you already own, sized exactly via ScalarLength()
    byte[] buffer = new byte[reader.ScalarLength()];
    int written = reader.CopyTo(buffer);

    // or, as an owned, poolable handle on the value - note this usually shares the reply's buffer
    // rather than copying, so dispose it promptly (see below):
    using Lease<byte>? lease = reader.ReadLease();

    // or, if you just want the usual RedisValue/string:
    RedisValue value = reader.ReadRedisValue();
}
```

`ReadScalar()` and `Read()` hand back a `RespReader`, which is a `ref struct`: reading it inside an `async` method needs C# 13 or later, and it cannot be held across an `await` - read what you need, then await.

Whether `ReadLease()` shares the underlying buffer or takes a copy depends on where the reader came from,
not on how you call it. Reading from a `RespResult` - as above - the reply's buffer is reference-counted,
so the lease points straight into it and no copy is made. Elsewhere, including the `Lease<byte>`-returning
commands like `HashGetLease` and any `RespReader` you build over your own bytes, you get an independent
copy. Either way the lease is yours and must be disposed, so calling code does not need to know which it
got.

The distinction does matter for how long you hold it. In the sharing case the reply stays rented until
both the `RespResult` and every lease taken from it have been disposed, so a short value taken from a
large reply keeps the whole reply alive. That is the right trade for the case this exists for - pulling
back a large blob without copying it - but dispose leases promptly, and if you want to keep a small part
of a large reply around for a long time, copy it out (`CopyTo` into your own buffer) rather than holding
the lease.

A `RespResult` is never itself a `null` C# reference - the reply is always a real, non-null `RespResult`, and `IsNull` tells you whether the underlying RESP reply itself was a null. There are three distinct null encodings on the wire and `RespResult` preserves which one you got rather than collapsing them, but `IsNull` is the test you want; see below. This also leaves room for RESP3 attribute metadata on a null reply in future.

### Testing what came back

Prefer the category tests - `IsScalar`, `IsAggregate`, `IsNull`, `IsError` - over comparing `Prefix` against a specific `RespPrefix`. The category is stable; the specific prefix is not, because the same command can be encoded differently under RESP2 and RESP3, and which protocol you get depends on the server version and on configuration rather than on your code:

```csharp
var reader = result.Read();
if (reader.IsNull) { /* no value */ }
else if (reader.IsScalar) { RedisValue value = reader.ReadRedisValue(); }
else if (reader.IsAggregate) { /* walk it - see below */ }
```

This matters most for **aggregates**. RESP2 has exactly one aggregate encoding - `*`, the array - so under RESP2 everything aggregate-shaped arrives as `Array`. RESP3 splits that into several: `HGETALL` and `CONFIG GET` come back as a map (`%`), `SMEMBERS` and the other set-returning commands as a set (`~`), and pub/sub delivery as a push (`>`). Code written as `Prefix == RespPrefix.Array` therefore works perfectly against a RESP2 connection and silently stops matching the moment the same code talks RESP3 - whereas `IsAggregate` is true for all of them.

Scalars vary too, just less dramatically: RESP3 adds `,` (double), `#` (boolean), `(` (big integer) and `=` (verbatim string) where RESP2 would have sent a bulk string or an integer - `ZSCORE`, for example, is a bulk string under RESP2 and a double under RESP3. Nulls are the same story: RESP2 has a null bulk string (`$-1`) and a null array (`*-1`), RESP3 has the single `_`, which is why `IsNull` is the test rather than any prefix comparison. Note that a script is subject to this as well whenever its reply passes the protocol through - for example after `redis.setresp(3)`.

`Prefix` remains available for when the exact wire encoding genuinely is what you care about - telling a verbatim string from a bulk string, say, or logging what actually arrived - but that is the exception, and reaching for it as a general shape test is the common way to write code that breaks on protocol upgrade.

If the script can return a tree (an array, or a mix of shapes depending on input), `RespResult.Read()` gives you a `RespReader` positioned at the root. `.ReadRedisResult()` is the convenient option - it falls back to the familiar `RedisResult` materialization for the whole value, at the cost of allocating that same wrapper-object-per-node tree the low-allocation APIs elsewhere in this doc are trying to avoid. If efficiency actually matters for a tree-shaped reply, walk the `RespReader` directly instead: it's a forwards-only iterator over the raw reply, with the same low-level accessors (`ReadRedisValue`, `ReadLease`, `CopyTo`, `ScalarLength`, ...) available at each node, so you can read exactly what you need without materializing the rest of the tree:

```csharp
using RespResult result = await db.Scripts.EvaluateAsync("return {1,2,'three'}");
RedisResult tree = result.Read().ReadRedisResult();
var values = (RedisValue[])tree!;
```

For that same reply, walking it directly via `AggregateChildren()` avoids materializing the `RedisResult` tree at all - each child is a `RespReader` in its own right, so the usual scalar accessors (`ReadRedisValue`, `ReadLease`, `CopyTo`, ...) apply per element:

```csharp
using RespResult result = await db.Scripts.EvaluateAsync("return {1,2,'three'}");
var parent = result.Read();
var children = parent.AggregateChildren();
while (children.MoveNext())
{
    // note that .Value should be preferred over .Current, but they have the same result
    RedisValue value = children.Value.ReadRedisValue();
    // use value ...
}
children.MovePast(out parent); // positions `parent` right past the aggregate, e.g. to keep reading sibling data in a larger tree
```

If you just want the whole aggregate as a typed array via a projection, without the manual loop, `RespReader.ReadPastArray<TResult>` (or its non-mutating twin `ReadArray<TResult>`) does that in one call - `scalar: true` is a further hint that lets it skip the more general child-walking machinery, valid here because every element of `{1,2,'three'}` is itself a scalar rather than a nested sub-tree:

```csharp
RedisValue[]? values = parent.ReadPastArray(static (ref r) => r.ReadRedisValue(), scalar: true);
```

This is equivalent to the manual loop above, just without needing to write it out yourself, and capturing the results as an array.

Leasing the keys and values
---

`db.Scripts.EvaluateAsync` takes its keys and values as `ReadOnlySpan<>`, so there is no array to manage: a collection expression (`["mykey"]`, as above) or a slice of a buffer you already own both work, and the call is finished with them before it returns.

`ScriptEvaluateResp` takes `ReadOnlyMemory<>` instead, so on a hot path you can rent those arrays rather than allocating a pair per call - and return them as soon as the call returns, with no conditions attached:

```csharp
var keys = ArrayPool<RedisKey>.Shared.Rent(1);
var values = ArrayPool<RedisValue>.Shared.Rent(2);
try
{
    keys[0] = "mykey";
    values[0] = 123;
    values[1] = 456;
    using RespResult result = db.ScriptEvaluateResp(script, keys.AsMemory(0, 1), values.AsMemory(0, 2));
    // use result...
}
finally
{
    ArrayPool<RedisKey>.Shared.Return(keys, clearArray: true);
    ArrayPool<RedisValue>.Shared.Return(values, clearArray: true);
}
```

The arguments are rendered into the request before the call returns - before the task is handed back, for the async form - so the library is not reading your arrays afterwards, whether the call succeeded, threw, or was fire-and-forget. See [Leasing the argument buffer](Execute#leasing-the-argument-buffer) for the async and batched shapes, and for the one caveat: a `RedisValue` wrapping a `ReadOnlyMemory<byte>` is rendered by value, so the array is yours again but the bytes behind such a value are not - a known gap, expected to close in a future update.

Ad-hoc commands
---

For an arbitrary Redis command (not necessarily Lua), `db.Context.SendAsync<RespResult>($"...")` returns the same `RespResult`, and `IDatabase.ExecuteResp(Async)` follows the same pattern - see [Ad-hoc commands](Execute) for details. Neither takes separate keys/values, because - unlike a script's fixed `KEYS`-then-`ARGV` shape - an arbitrary command can place keys anywhere in its argument list: the interpolated form marks each `RedisKey` hole as a key, and `ExecuteResp` takes a single `ReadOnlyMemory<RedisKeyOrValue>`.

Named parameters via `LuaScript` (legacy)
---

Before the `RespResult` forms existed, an alternative way to pass parameters to a script was the `LuaScript` class, which rewrites `@name`-style placeholders in your script text into the `KEYS`/`ARGV` indices Redis actually expects, using reflection over an anonymous object's members:

```csharp
const string Script = "redis.call('set', @key, @value)";
var prepared = LuaScript.Prepare(Script);
db.ScriptEvaluate(prepared, new { key = (RedisKey)"mykey", value = 123 });
```

This still works (`ScriptEvaluate`/`ScriptEvaluateAsync`, and `LoadedLuaScript` for the `EVALSHA`-only variant loaded via `LuaScript.Load(IServer)`), but the reflection-based parameter binding and the `@name` rewriting are more machinery than most callers need. Prefer `db.Scripts.EvaluateAsync` (or `ScriptEvaluateResp`) with explicit `RedisKey`/`RedisValue` arguments for new code; reach for `LuaScript` only if you specifically want the named-parameter ergonomics.
