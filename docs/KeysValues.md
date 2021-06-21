Keys, Values and Channels
===

In dealing with redis, there is quite an important distinction between *keys* and *everything else*. A key is the unique name of a piece of data (which could be a String, a List, Hash, or any of the other [redis data types](http://redis.io/topics/data-types)) within a database. Keys are never interpreted as... well, anything: they are simply inert names. Further - when dealing with clustered or sharded systems, it is the key that defines the node (or nodes if there are replicas) that contain this data - so keys are crucial for routing commands.

This contrasts with *values*; values are the *things that you store* against keys - either individually (for String data) or as groups. Values do not affect command routing <small>(caveat: except for [the `SORT` command](http://redis.io/commands/sort) when `BY` or `GET` is specified, but that is *really* complicated to explain)</small>. Likewise, values are often *interpreted* by redis for the purposes of an operation:

- `incr` (and the various similar commands) interpret String values as numeric data
- sorting can interpret values using either numeric or unicode rules
- and many others

The key point is that the API needs to understand what is a key and what is a value. This is reflected in the StackExchange.Redis API, but the good news is that **most of the time** you don't need to know about this at all.

When using pub/sub, we are dealing with *channels*; channels do not affect routing (so they are not keys), but are quite distinct from regular values, so are considered separately.

Keys
---

StackExchange.Redis represents keys by the `RedisKey` type. The good news, though, is that this has implicit conversions to and from both `string` and `byte[]`, allowing both text and binary keys to be used without any complication. For example, the `StringIncrement` method takes a `RedisKey` as the first parameter, but *you don't need to know that*; for example:

```csharp
string key = ...
db.StringIncrement(key);
```

or

```csharp
byte[] key = ...
db.StringIncrement(key);
```

Likewise, there are operations that *return* keys as `RedisKey` - and again, it simply works:

```csharp
string someKey = db.KeyRandom();
```

Values
---

StackExchange.Redis represents values by the `RedisValue` type. As with `RedisKey`, there are implicit conversions in place which mean that most of the time you never see this type, for example:

```csharp
db.StringSet("mykey", "myvalue");
```

However, in addition to text and binary contents, values can also need to represent typed primitive data - most commonly (in .NET terms) `Int32`, `Int64`, `Double` or `Boolean`. Because of this, `RedisValue` provides a lot more conversion support than `RedisKey`:

```csharp
db.StringSet("mykey", 123); // this is still a RedisKey and RedisValue
...
int i = (int)db.StringGet("mykey");
```

Note that while the conversions from primitives to `RedisValue` are implicit, many of the conversions from `RedisValue` to primitives are explicit: this is because it is very possible that these conversions will fail if the data does not have an appropriate value.

Note additionally that *when treated numerically*, redis treats a non-existent key as zero; for consistency with this, nil responses are treated as zero:

```csharp
db.KeyDelete("abc");
int i = (int)db.StringGet("abc"); // this is ZERO
```

If you need to detect the nil condition, then you can check for that:

```csharp
db.KeyDelete("abc");
var value = db.StringGet("abc");
bool isNil = value.IsNull; // this is true
```

or perhaps more simply, just use the provided `Nullable<T>` support:

```csharp
db.KeyDelete("abc");
var value = (int?)db.StringGet("abc"); // behaves as you would expect
```

Hashes
---

Since the field names in hashes do not affect command routing, they are not keys, but can take both text and binary names; thus they are treated as values for the purposes of the API.

Channels
---

Channel names for pub/sub are represented by the `RedisChannel` type; this is largely identical to `RedisKey`, but is handled independently since while channel-names are rightly first-class elements, they do not affect command routing.

Scripting
---

[Lua scripting in redis](http://redis.io/commands/EVAL) has two notable features:

- the inputs must keep keys and values separate (which inside the script become `KEYS` and `ARGV`, respectively)
- the return format is not defined in advance: it is specific to your script

Because of this, the `ScriptEvaluate` method accepts two separate input arrays: one `RedisKey[]` for the keys, one `RedisValue[]` for the values (both are optional, and are assumed to be empty if omitted). This is probably one of the few times that you'll actually need to type `RedisKey` or `RedisValue` in your code, and that is just because of array variance rules:

```csharp
var result = db.ScriptEvaluate(TransferScript,
    new RedisKey[] { from, to }, new RedisValue[] { quantity });
```

(where `TransferScript` is some `string` containing Lua, not shown for this example)

The response uses the `RedisResult` type (this is unique to scripting; usually the API tries to represent the response as directly and clearly as possible). As before, `RedisResult` offers a range of conversion operations - more, in fact than `RedisValue`, because in addition to being interpreted as text, binary, primitives and nullable-primitives, the response can *also* be interpreted as *arrays* of such, for example:

```csharp
string[] items = db.ScriptEvaluate(...);
```

Conclusion
---

The types used in the API are very deliberately chosen to distinguish redis *keys* from *values*. However, in virtually all cases you will not need to directly refer to the underlying types involved, as conversion operations are provided.
