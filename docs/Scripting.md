Scripting
===

Basic [Lua scripting](http://redis.io/commands/EVAL) is supported by the `IServer.ScriptLoad(Async)`, `IServer.ScriptExists(Async)`, `IServer.ScriptFlush(Async)`, `IDatabase.ScriptEvaluate`, and `IDatabaseAsync.ScriptEvaluateAsync` methods.
These methods expose the basic commands necessary to submit and execute Lua scripts to redis.

More sophisticated scripting is available through the `LuaScript` class.  The `LuaScript` class makes it simpler to prepare and submit parameters along with a script, as well as allowing you to use 
cleaner variables names.

An example use of the `LuaScript`:

```
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


To avoid retransmitting the Lua script to redis each time it is evaluated, `LuaScript` objects can be converted into `LoadedLuaScript`s via `LuaScript.Load(IServer)`.
`LoadedLuaScripts` are evaluated with the [`EVALSHA`](http://redis.io/commands/evalsha), and referred to by hash.

An example use of `LoadedLuaScript`:

```
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
