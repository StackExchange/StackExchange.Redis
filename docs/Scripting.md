Scripting
===

Basic [Lua scripting](https://redis.io/commands/EVAL) is supported by the `IServer.ScriptLoad(Async)`, `IServer.ScriptExists(Async)`, `IServer.ScriptFlush(Async)`, `IDatabase.ScriptEvaluate`, and `IDatabaseAsync.ScriptEvaluateAsync` methods.
These methods expose the basic commands necessary to submit and execute Lua scripts to redis.

More sophisticated scripting is available through the `LuaScript` class.  The `LuaScript` class makes it simpler to prepare and submit parameters along with a script, as well as allowing you to use cleaner variables names.

An example use of the `LuaScript`:

```csharp
const string Script = "redis.call('set', @key, @value)";

using (ConnectionMultiplexer conn = /* init code */)
{
	var db = conn.GetDatabase(0);

	var prepared = LuaScript.Prepare(Script);
	db.ScriptEvaluate(prepared, new { key = (RedisKey)"mykey", value = 123 });
}
```

The `LuaScript` class rewrites variables in scripts of the form `@myVar` into the appropriate `ARGV[someIndex]` required by redis.  If the 
parameter passed is of type `RedisKey` it will be sent as part of the `KEYS` collection automatically.

Any object that exposes field or property members with the same name as @-prefixed variables in the Lua script can be used as a parameter hash to
`Evaluate` calls.  Supported member types are the following:

 - int(?)
 - long(?)
 - double(?)
 - string
 - byte[]
 - bool(?)
 - RedisKey
 - RedisValue

StackExchange.Redis handles Lua script caching internally. It automatically transmits the Lua script to redis on the first call to 'ScriptEvaluate'. For further calls of the same script only the hash with [`EVALSHA`](https://redis.io/commands/evalsha) is used.

For more control of the Lua script transmission to redis, `LoadedLuaScripts` are evaluated with the [`EVALSHA`](https://redis.io/commands/evalsha), and referred to by hash.

If a previously loaded `Lua` script `SHA-1` hash was not found on the server, the script will be reloaded on the next call to `Evaluate()` or `EvaluateAsync()` with `SCRIPT LOAD`.
The first evaluation of a reloaded script will be carried out by the `EVAL` redis command, any subsequent evaluations will use `EVALSHA`.

An example use of `LoadedLuaScript`:

```csharp
const string Script = "redis.call('set', @key, @value)";

using (ConnectionMultiplexer conn = /* init code */)
{
	var db = conn.GetDatabase(0);
	var server = conn.GetServer(/* appropriate parameters*/);

	var prepared = LuaScript.Prepare(Script);
	var loaded = prepared.Load(server);
	loaded.Evaluate(db, new { key = (RedisKey)"mykey", value = 123 });
}
```

All methods on both `LuaScript` and `LoadedLuaScript` have Async alternatives, and expose the actual script submitted to redis as the `ExecutableScript` property.
