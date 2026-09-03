Scripting
===

[Lua scripting](https://redis.io/commands/EVAL) lets you run a script server-side, atomically, in one round trip. StackExchange.Redis exposes this through `IDatabase.ScriptEvaluateResp(Async)` (and the read-only `ScriptEvaluateReadOnlyResp(Async)` twin), plus the `IServer.ScriptLoad(Async)`/`ScriptExists(Async)`/`ScriptFlush(Async)` support commands.

Basic use
---

`ScriptEvaluateResp` takes the script text, the keys (available to the script as `KEYS`), and the values (available as `ARGV`) as two separate `ReadOnlyMemory<RedisKey>`/`ReadOnlyMemory<RedisValue>` parameters - kept separate deliberately, since the script indexes them separately too (`KEYS[1]`, `ARGV[1]`, ...); there's no benefit to the caller in combining them into one collection:

```csharp
using ConnectionMultiplexer conn = /* init code */;
var db = conn.GetDatabase();

using RespResult result = db.ScriptEvaluateResp(
    "return redis.call('set', KEYS[1], ARGV[1])",
    new RedisKey[] { "mykey" },
    new RedisValue[] { 123 });
```

Keys participate in cluster slot routing and (if you're using `KeyPrefixed`) key-prefixing, exactly like any other command's keys - values do not. The script itself is cached automatically: the first call sends the full script text (`EVAL`); subsequent calls with the same script send only its SHA1 hash (`EVALSHA`), and a transparent retry re-sends the full script if the server reports `NOSCRIPT` (for example after a `SCRIPT FLUSH`).

`ScriptEvaluateReadOnlyResp(Async)` is the same shape, for the [`EVAL_RO`/`EVALSHA_RO`](https://redis.io/commands/eval_ro) read-only variant, which can't run write commands and so is eligible to run against a replica.

Reading the result: `RespResult` vs `RedisResult`
---

`ScriptEvaluateResp` returns a `RespResult`: a leased, undecoded view over the raw reply, backed by a pooled buffer rather than a fresh allocation per call. You `using` it to return the buffer once you're done. This is in contrast to the classic `ScriptEvaluate` (no `Resp` suffix, taking `RedisKey[]?`/`RedisValue[]?` arrays), which returns a `RedisResult` - a fully-materialized, general-purpose tree that's easy to cast (`(string)`, `(long)`, `(RedisValue[])`, etc.) but that always allocates: a wrapper object per node, plus a decoded value per scalar. Prefer `ScriptEvaluateResp` for new code, especially when the result is a single scalar (the common case, especially for a blob payload):

```csharp
using RespResult result = db.ScriptEvaluateResp("return redis.call('get', KEYS[1])", new RedisKey[] { "mykey" }, default);
if (!result.IsNull)
{
    var reader = result.ReadScalar();

    // cheapest: copy straight into a buffer you already own, sized exactly via ScalarLength()
    byte[] buffer = new byte[reader.ScalarLength()];
    int written = reader.CopyTo(buffer);

    // or, if you want an owned, poolable copy to hold on to for a while:
    using Lease<byte>? lease = reader.ReadLease();

    // or, if you just want the usual RedisValue/string:
    RedisValue value = reader.ReadRedisValue();
}
```

A `RespResult` is never itself a `null` C# reference - the reply is always a real, non-null `RespResult`, and `IsNull` tells you whether the underlying RESP reply itself was a null (there are three distinct null encodings on the wire; `RespResult` preserves which one you got via `Prefix`, rather than collapsing them). This also leaves room for RESP3 attribute metadata on a null reply in future.

If the script can return a tree (an array, or a mix of shapes depending on input), `RespResult.Read()` gives you a `RespReader` positioned at the root. `.ReadRedisResult()` is the convenient option - it falls back to the familiar `RedisResult` materialization for the whole value, at the cost of allocating that same wrapper-object-per-node tree the low-allocation APIs elsewhere in this doc are trying to avoid. If efficiency actually matters for a tree-shaped reply, walk the `RespReader` directly instead: it's a forwards-only iterator over the raw reply, with the same low-level accessors (`ReadRedisValue`, `ReadLease`, `CopyTo`, `ScalarLength`, ...) available at each node, so you can read exactly what you need without materializing the rest of the tree:

```csharp
using RespResult result = db.ScriptEvaluateResp("return {1,2,'three'}", default, default);
RedisResult tree = result.Read().ReadRedisResult();
var values = (RedisValue[])tree!;
```

For that same reply, walking it directly via `AggregateChildren()` avoids materializing the `RedisResult` tree at all - each child is a `RespReader` in its own right, so the usual scalar accessors (`ReadRedisValue`, `ReadLease`, `CopyTo`, ...) apply per element:

```csharp
using RespResult result = db.ScriptEvaluateResp("return {1,2,'three'}", default, default);
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

Ad-hoc commands
---

`IDatabase.ExecuteResp(Async)` follows the same `RespResult` pattern for an arbitrary Redis command (not necessarily Lua) - see [Ad-hoc commands](Execute) for details. It takes `ReadOnlyMemory<RedisKeyOrValue>` rather than separate keys/values, because - unlike a script's fixed `KEYS`-then-`ARGV` shape - an arbitrary command can place keys anywhere in its argument list.

Named parameters via `LuaScript` (legacy)
---

Before `ScriptEvaluateResp` existed, an alternative way to pass parameters to a script was the `LuaScript` class, which rewrites `@name`-style placeholders in your script text into the `KEYS`/`ARGV` indices Redis actually expects, using reflection over an anonymous object's members:

```csharp
const string Script = "redis.call('set', @key, @value)";
var prepared = LuaScript.Prepare(Script);
db.ScriptEvaluate(prepared, new { key = (RedisKey)"mykey", value = 123 });
```

This still works (`ScriptEvaluate`/`ScriptEvaluateAsync`, and `LoadedLuaScript` for the `EVALSHA`-only variant loaded via `LuaScript.Load(IServer)`), but the reflection-based parameter binding and the `@name` rewriting are more machinery than most callers need. Prefer `ScriptEvaluateResp` with explicit `RedisKey`/`RedisValue` arguments for new code; reach for `LuaScript` only if you specifically want the named-parameter ergonomics.
