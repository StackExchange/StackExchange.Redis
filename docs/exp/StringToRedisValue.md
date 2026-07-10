StringToRedisValue
-

Oh dear, it looks like you fell into a `DEBUG` warning. **Don't panic!** You're probably fine.

Short version, *if you're in the core library when seeing this*:

1. If you're *writing* data (to the server), you're probably fine - just add `.AsRedisValue()` to say "trust me bro"; all strings are valid as `RedisValue` content - you're fine. If the value
   is a literal (i.e. `"some const string"`) or a `const`, you might want to look at `RedisLiterals` and the `.FromRaw(...)` API, which can be more efficient, but otherwise: you do you!
2. However, if you're *reading* data (from the server), then you need to be careful - there's a good chance you've used `reader.ReadString()` when you meant to use `reader.ReadRedisValue()`.

The impact of *2* would be that *string* payloads work fine, but you silently corrupt *binary* payloads. Additionally, if the value is *small* or an obvious integer, `RedisValue` may
be more efficient (zero-allocation), and even for non-trivial text-like payloads: using `RedisValue` defers the UTF8 decode cost to the caller. So: ask yourself - is this actually
a `string` in all cases?

This warning only exists in local `DEBUG` builds - it doesn't exist in the public package.

If you're actually in an auxiliary package (tests, benchmarks, etc): we care less. Feel free to add `<NoWarn>$(NoWarn);StringToRedisValue</NoWarn>` to the csproj. 