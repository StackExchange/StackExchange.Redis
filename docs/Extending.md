Extending the client with your own commands
===

This page is for **library authors**: you ship a package on top of StackExchange.Redis that adds commands this client does not have - a module (`JSON.*`, `FT.*`, `BF.*`, `TS.*`), a preview server feature, a vendor extension. The audience is NRedisStack and friends.

There are three levels, and they are not alternatives so much as a progression. All three send through the same connection, pipeline and backlog as every built-in command; none of them needs anything added to StackExchange.Redis.

| | you write | you get | since |
|---|---|---|---|
| **1. Ad-hoc** | `db.ExecuteResp("JSON.GET", args)` | one call, raw reply | 3.2 |
| **2. Ad-hoc, interpolated** | `db.Context.SendAsync<RedisValue>($"JSON.GET {key} {path}")` | typed reply, nothing allocated | 4.0 |
| **3. Your own surface** | `db.Json().GetAsync(key, path)` | your API, no `object[]`, no per-call allocation | 4.0 |

Level 3 is what the rest of this page is about, but start at level 1: if a command is used once, it does not need a surface - and level 1 works on 3.2, so a library that must support both majors has somewhere to stand.

Level 1: the ad-hoc call
---

`ExecuteResp`/`ExecuteRespAsync` is the right answer for a command you call occasionally, and the only one of the three available before 4.0. It takes the command name and one `ReadOnlyMemory<RedisKeyOrValue>` of arguments, in the order the command wants them:

```csharp
using RespResult reply = await db.ExecuteRespAsync(
    "SUBSTR", new RedisKeyOrValue[] { (RedisKey)key, (RedisValue)0, (RedisValue)4 });

RedisValue value = reply.ReadScalar().ReadRedisValue();
```

Note the casts: `RedisKeyOrValue` has no implicit conversion from `string` or `int`, and that is the point of the type. **Which arguments are keys is not recoverable from the bytes**, and the client needs to know: keys decide the cluster slot, they take the context's key prefix, and they are what a client-side cache invalidates on. A type that guessed would guess wrong.

See [Ad-hoc commands](Execute) for the full treatment - pooling the argument buffer, reading the reply, and the measured allocation difference against the old `Execute`.

Migrating from `Execute(string, object[])`
---

The original `IDatabase.Execute`/`ExecuteAsync` takes `object[]` or `ICollection<object>` and returns `RedisResult`. It still works and is going nowhere. The move is mechanical:

```csharp
// before: every argument boxed, key-ness inferred by position (it isn't - nothing is inferred)
RedisResult old = await db.ExecuteAsync("SUBSTR", new object[] { key, 0, 4 });
string? s = (string?)old;

// after: key-ness declared, reply leased rather than allocated
using RespResult reply = await db.ExecuteRespAsync(
    "SUBSTR", new RedisKeyOrValue[] { (RedisKey)key, (RedisValue)0, (RedisValue)4 });
string? s = (string?)reply.ReadScalar().ReadRedisValue();
```

| old | new | why |
|---|---|---|
| `object[]` args | `ReadOnlyMemory<RedisKeyOrValue>` | no boxing; and keys are declared rather than hoped for |
| `RedisResult` | `RespResult` | a leased window over the reply buffer, not a fresh object graph - **dispose it** |
| `(string)`, `(byte[])`, `(long)` casts | `reply.ReadScalar().ReadRedisValue()` / `.ReadLease()` / `.CopyTo(span)` | reads where the bytes landed |
| keys invisible to the client | keys routed, prefixed, and invalidated | correctness on cluster and with key prefixes |

The one behavioural difference worth reading twice: **`RespResult` is leased**. It is valid until you dispose it, and everything you read out of it that is not a copy dies with it. `using` is not decoration.

Level 2: the same thing, without the argument array
---

On 4.0 the request is built by the same interpolated writer the built-in commands use, so an occasional
command needs no argument collection at all - and the reply comes back typed rather than as a raw
`RespResult` you have to read yourself:

```csharp
RedisValue value = await db.Context.SendAsync<RedisValue>($"SUBSTR {key} {0} {4}");
```

Each hole is written straight into a pooled buffer as UTF-8; `key` is a `RedisKey` so it is marked as a
key, which is the same thing the `RedisKeyOrValue` wrapping bought at level 1 - only here the type system
already knows, so there is nothing to wrap. Literal text between the holes is tokenized, so `SUBSTR` is
the command name.

This is level 3's command body with the surface left off. If you find yourself writing the same
interpolation in several places, that is the signal to go one level further.

Level 3: your own command surface
---

If you are shipping more than a couple of commands, give them a home. The shape below is exactly what this client's own groups use, and it needs no cooperation from us - three pieces, and only the middle one is unusual.

```csharp
using RESPite.Messages;
using StackExchange.Redis;
using StackExchange.Redis.Protocol;   // RespCommand lives here; see below

// 1. the group: a context plus a name, and nothing else
public readonly struct ContosoCommands(in RespContext context)
{
    public RespContext Context { get; } = context;
}

public static class ContosoExtensions
{
    // rendered once, not per call: the name is a constant, and `preform` keeps its RESP bulk string ready
    private static readonly RespCommand Substr = "SUBSTR".Command(preform: true);

    // 2. the accessor, on any keyspace TARGET: IDatabase, IBatch, ITransaction - and a database context
    //    itself, which is a target too, so a prefixed context (db.Context.AppendKeyPrefix(...)) reaches it.
    //    The context is what carries the key prefix, the database and the services; the cast steps out of
    //    the typed world into the plumbing, deliberately
    public static ContosoCommands Contoso<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget
        => new((RespContext)target.Context);

    // 3. the command
    public static ValueTask<RedisValue> SubstringAsync(
        this in ContosoCommands contoso,
        RedisKey key,
        long start,
        long end,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => contoso.Context.SendAsync<RedisValue>(
            $"{Substr}{key}{start}{end}", flags, cancellationToken: cancellationToken);
}
```

and the caller writes:

```csharp
RedisValue value = await db.Contoso().SubstringAsync(key, 0, 4);
```

`IRespKeyspaceTarget` is carried by `IDatabase`, `IBatch` and `ITransaction`, and by `RespDatabaseContext` itself, so one accessor covers all of them; `IRespServerTarget` is the `IServer` (and `RespServerContext`) counterpart, for commands that belong to a node rather than a key. Everything reaches the connection through `Context`.

A command with optional modifiers, or a variable number of keys, cannot be one interpolated string; build it with `Compose` instead - see [Building a command in pieces](Execute#building-a-command-in-pieces-compose).

The contexts, the targets and `SendAsync` are all in the `StackExchange.Redis` namespace, which your callers already have. The second `using` above is the other half of the split: `StackExchange.Redis.Protocol` holds the request- and reply-building types - `RespCommand`, `RespRequestFrame`, `RespFragment`, `IRespArgument`, and `RespHandlers`/`IRespHandler<T>` for a reply shape the defaults do not cover. You name those when you write a command, and never otherwise.

That split is deliberate: the context surface is the primary API, the frame machinery is not, and a namespace is the cheapest way to say which is which. It is also why level 2 needs nothing extra - `db.Context.SendAsync<RedisValue>($"SUBSTR {key} {0} {4}")` names no protocol type, because the interpolated string is lowered into one rather than written as one.

> On C# 14 the accessor can be an extension **property** (`extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget { public ContosoCommands Contoso => new((RespContext)target.Context); }`), giving `db.Contoso.SubstringAsync(...)` without the parentheses - the shape this client's own groups use. The classic form above compiles everywhere.
>
> One generic accessor rather than one per type: the constraint is satisfied by a struct context exactly as by an interface, and for a struct the JIT specialises the call, so `ctx.Contoso` costs what a hand-written accessor on the context would - measured identical once tiered compilation has reached tier 1.

### The interpolated string is not a string

`$"{Substr}{key}{start}{end}"` never builds one. The compiler lowers each hole into a call on a handler that writes UTF-8 straight into a pooled buffer, counting arguments as it goes; there is no `string`, no `string.Format`, no `object[]`, and no boxing. Whitespace between holes contributes nothing, so `$"{Substr} {key} {start} {end}"` renders identically - write whichever reads better.

Literal text works too, and is tokenized: `$"JSON.GET {key} {path}"` sends three arguments, with `JSON.GET` as the command. Prefer the `RespCommand` form for a command you send often - a name this client does not know is rendered once at startup instead of being tokenized and encoded per call. (A name it *does* know stays deferred whatever you ask for, because the context's command map may rename or disable it, and already holds the bytes.)

### Keys are keys - this is the rule that matters

```csharp
$"{Substr}{key}{start}{end}"    // key: routed, prefixed, tracked
$"{Substr}{(RedisValue)key}"    // NOT a key: no slot, no prefix, no invalidation
```

The hole's **type** decides. `RedisKey` binds to the key overload; `RedisValue`, `long`, `double` and friends bind to value overloads. The trap to know about: `$"{someString}"` binds to the **`RedisValue`** overload - there is an explicit `string` overload that makes sure of it, because a bare string converts equally well to `RedisKey`, `RedisValue` and `RedisChannel` and would otherwise be ambiguous. So a key held as a `string` must be spelled `(RedisKey)someString`, or it silently loses its prefix and its slot. If you hold a key as bytes or a span, `RespKey` is the no-allocation spelling.

Getting this wrong is invisible on a single non-clustered server with no key prefix, and wrong everywhere else. If your library has one test, make it this one: run a command through a prefixed context and check the server saw the prefix.

### Reading the reply

If the result type is one the client already knows - `RedisValue`, `long`, `bool`, `double`, `string`, `ReadOnlyLease<T>`, `RespResult` - name it and you are done:

```csharp
=> contoso.Context.SendAsync<RedisValue>($"{Substr}{key}{start}{end}", flags, cancellationToken: cancellationToken);
```

For a shape only your library knows, supply a handler. It is handed the reader positioned on the reply, and returns whatever you want:

```csharp
private sealed class LengthHandler : IRespHandler<int>
{
    public static readonly LengthHandler Instance = new();

    public int Parse(ref RespReader reader) => reader.ScalarLength();
}

// ...
=> contoso.Context.SendAsync($"{Substr}{key}{start}{end}", flags, LengthHandler.Instance, cancellationToken: cancellationToken);
```

A singleton, not a lambda: the handler is stateless, so one instance serves every call and the send allocates nothing for it.

`RespReader` gives you the reply where it landed - `ReadInt64()`, `ReadRedisValue()`, `ReadString()`, `CopyTo(Span<byte>)`, `CopyTo(Span<char>, Encoding?)` for text without a `string`, and `ScalarChunks()` when you want the bytes as they arrived. Test the shape of a reply with `IsScalar`/`IsAggregate`/`IsNull` rather than comparing `Prefix` to a literal: RESP2 and RESP3 spell the same reply differently, and the category tests cover both.

### Say whether your command can be retried

This one is easy to miss and has no visible symptom. The client keeps a retry category per command - can this be replayed safely after a reconnect? - and it has no entry for yours, so it assumes **the worst**: not replayed, not cached.

```csharp
// opt in, at the call site or in your method's default
=> contoso.Context.SendAsync<RedisValue>(
    $"{Substr}{key}{start}{end}",
    flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly),
    cancellationToken: cancellationToken);
```

`WithRetryCategory` is **caller-wins**: a caller who passes an explicit category keeps it, so this sets a default rather than overriding a decision. Use `CommandRetryReadOnly` for a read, `CommandRetryWriteChecked` for a write whose replay converges (conditional or idempotent), and leave it alone if a replay could double an effect.

### Synchronous forms

Write the asynchronous method only. A caller who needs to block makes the context blocking - `Blocking()` on the target's context - and every send through it then waits on the calling thread, so your `...Async` method returns a `ValueTask` that has already completed:

```csharp
var contoso = db.Context.Blocking().Contoso();
RedisValue value = contoso.SubstringAsync(key, 0, 4).GetAwaiter().GetResult();
```

If you want to offer a synchronous method yourself, it is that, inside the method - no second implementation, and nothing internal to bracket the call with:

```csharp
public static RedisValue Substring(this in ContosoCommands contoso, RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
    => new ContosoCommands(contoso.Context.Blocking()).SubstringAsync(key, start, end, flags).GetAwaiter().GetResult();
```

This is how the synchronous `IDatabase` methods work. A command that takes several round trips works too, because each send completes before the next is issued. It does not wait for a thread-pool thread to wake the caller, which is the usual hazard of blocking on an async API. A retrying context blocks too, pausing between attempts on the calling thread. The exception is a batch or transaction, whose commands are not sent until it is executed - see the remarks on `RespContext.Blocking`.

### Announcing your library

Redis records which client library each connection belongs to (`CLIENT SETINFO lib-name`, shown in `CLIENT LIST`). To add your library's name to it, call `AddLibraryNameSuffix` on the multiplexer - which a context exposes:

```csharp
db.Context.Multiplexer?.AddLibraryNameSuffix("Contoso");
```

It is idempotent and applies to every connection, including ones made later, so once per multiplexer is enough - not once per command: each call takes a lock to find out it has nothing to do. `Multiplexer` is `null` for a context with no multiplexer behind it (a test fake, say); for a multi-group connection it is the group, so the suffix reaches every member.

### What you do not have to do

Handled for you, on every command written this way:

- **cluster slot** - computed from the keys in the request, with a loud failure if they disagree
- **key prefixes** - a `WithKeyPrefix`/`AppendKeyPrefix` context rewrites your keys without your code knowing
- **command map** - renaming and disabling apply to known command names
- **RESP2 vs RESP3** - one request; the reader copes with both spellings of the reply
- **pipelining, the backlog, reconnects, timeouts** - the same path as every built-in command

Supporting both majors
---

If your package targets 3.x as well as 4.0, level 1 is the common denominator: `ExecuteResp` exists in both, with the same signature and the same `RespResult`. A surface of your own can then be added for 4.0 callers behind a `#if`, over the same request-building code, rather than being a second implementation.

The samples on this page are compiled and run as tests (`RespExtensionAuthorTests`), so they do not rot. `SUBSTR` stands in for the module command you would actually be adding: it is a real server command this client has no API for - not even a `RedisCommand` entry - so it exercises the unknown-command path exactly as `JSON.GET` would.
