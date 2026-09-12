# Interpolated-string RESP writer

**Exploratory notes — ideas, not decisions.** Nothing here is agreed or committed to; it is a log of
what was tried, what was verified empirically, what seems to follow, and what is still open. Treat
recommendations as "this looked right at the time", not as a plan of record. A working spike lives in `src/StackExchange.Redis/Interpolated/` with unit tests
in `tests/StackExchange.Redis.Tests/InterpolatedWriterUnitTests.cs`; everything there is `internal`, so
there is no public API commitment yet.

The idea: let command construction read as

```csharp
Execute($"{cmd}{key}{value}", handler);
```

where `$"..."` binds to a custom interpolated string handler that writes RESP directly, rather than
building a `Message` + argument array. The handler is pure formatting: it runs on the caller's
thread, ahead of the critical section, and the bytes it produces double as the client-side cache
key before anything touches the muxer core. It is never near a connection.

This is intended to **replace the writer half of the `marc/respite` v3 PoC spike** — its manual
`RespWriter` + `RespOperationBuilder`. The execution API around it (`RespContext`, cancellation,
`RespContextDatabase`) is good and carries over unchanged. See §8.

---

## 1. Is the handler pattern usable down-level?

**Spec:** [Improved Interpolated Strings](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/improved-interpolated-strings.md)
(C# 10 feature spec). Quoted below where it settles a question; the empirical checks agree with it
throughout. Notably it imposes **no requirement that the attributes come from corelib** — they are
recognised by name — which is what makes the polyfill legitimate rather than a trick that happens to work.

**Yes, with no runtime support at all.** Interpolated string handlers are 100% compiler lowering,
and the marker attributes are matched *by full name*, so declaring them `internal` in our own source
works exactly like the existing `SkipLocalsInit` (`src/RESPite/Shared/SkipLocalsInit.cs`) and
`IsExternalInit` (`src/StackExchange.Redis/FrameworkShims.IsExternalInit.cs`) shims.
`LangVersion 14` is already set repo-wide in `Directory.Build.props`.

Verified by compiling across `netstandard2.0` / `net472` / `net8.0`:

| Feature | Down-level | Notes |
| --- | --- | --- |
| `[InterpolatedStringHandler]` on a `ref struct` | works | polyfill the attribute in source |
| `[InterpolatedStringHandlerArgument(nameof(x))]` | works | caller's `stackalloc` buffer reaches the ctor |
| `[InterpolatedStringHandlerArgument("")]` | works | `""` passes the **receiver** (`this`) |
| `[InterpolatedStringHandlerArgument("", nameof(cmd))]` | works | receiver *and* a parameter |
| `out bool shouldAppend` conditional ctor | works | |
| `bool`-returning `Append*` | works | compiler emits a short-circuiting `&&` chain |
| Use inside an `async` method | works | fails only if a hole itself contains `await` (CS8850) |
| `u8` literals (`"..."u8`) | works | **no polyfill needed**; needs only `ReadOnlySpan<byte>` |
| C# 14 extension members (`extension(...) { }`) | works | also pure lowering |
| `[OverloadResolutionPriority]` | works | polyfill the attribute in source |
| `scoped` on span params | works | `ScopedRefAttribute` is compiler-synthesized |
| `System.Index` / `System.Range` | works, but **`internal` only** | public polyfill would collide on newer TFMs; unusable in public API |

`net461` was not tested (no reference assemblies to hand) but uses the same compiler path; the only
dependency is `ReadOnlySpan<byte>`, which RESPite already has there via `System.Memory`.

The spec text behind three of those rows, since they shape the design elsewhere:

- **Constructor** — *"The first two arguments are integer constants, representing the literal length of
  `i`, and the number of interpolation components in `i`, respectively."* Extra parameters come from
  `InterpolatedStringHandlerArgumentAttribute`, and the trailing `out bool` is optional: *"If no
  applicable constructors were found, step 3 is retried, removing the final `bool` parameter."*
- **Short-circuiting** — *"If `Fax` returns a `bool`, the result is logically anded with all preceding
  `Fax` calls."* So `bool` returns genuinely stop later holes being evaluated (§2.4 rejects this for the
  disabled-command case, which must throw rather than quietly truncate).
- **`AppendFormatted` shapes** — the value by itself; plus an `int alignment` when the hole carries
  `,N`; plus a `string format` when it carries `:F`. See §2.3 for why the `format` route was not used.

**`InlineArray` is the one thing that is *not* polyfillable** — it needs .NET 8+ runtime layout
support, and down-level the attribute is inert, silently giving a one-element struct. Use
`stackalloc` at the call site or `fixed` buffers instead.

---

## 2. Shape

### 2.1 Literals are banned

`AppendLiteral` is declared but marked `[Obsolete(..., error: true)]`, so every part of the command
must be a hole:

```csharp
// error CS0619: All parts must be holes: $"{RedisCommand.SET}{key}{value}"
$"SET{key}{value}"
```

Two options were compared. *Omitting* `AppendLiteral` also rejects literals, but produces a
confusing pair of diagnostics (`CS1061` plus a bogus `CS8941` "does not return void or bool").
`[Obsolete]` produces one error carrying our own message. Use `[Obsolete]`.

**Why ban them:** with no literal segments, the compiler-supplied `formattedCount` *is* the argument
count, as a compile-time constant — so `*N\r\n` can be written in the constructor with no counting
and no back-fill.

**Verified non-hazard:** C# 10 makes an all-constant interpolated string a *constant expression*, so
`$"{"GET"}{"mykey"}"` could in principle have been folded to a single literal and routed to
`AppendLiteral`, silently corrupting the frame. It is not — the handler conversion wins and each hole
stays a hole. Confirmed both at runtime and by the fact that it compiles against a handler that has
no `AppendLiteral` at all.

**The compiler always passes a `string`, never u8.** The spec is explicit, and one rule accounts for
every case below:

> The argument list `Al` is constructed with one value parameter of type `string`. Traditional method
> invocation resolution is performed with method group `Ml` and argument list `Al`.

So the argument is *always* a `string`, and binding is then ordinary overload resolution. Verified across
the three plausible overload shapes:

| `AppendLiteral` overload | Binds to a literal segment? |
| --- | --- |
| `string` | yes — this is what the compiler passes |
| `ReadOnlySpan<char>` | yes, via the implicit `string` → span conversion |
| `ReadOnlySpan<byte>` | **no** — `CS1503: cannot convert from 'string' to 'System.ReadOnlySpan<byte>'` |

All three follow from the rule: `string`→`ReadOnlySpan<char>` is an applicable conversion, `string`→
`ReadOnlySpan<byte>` is not, and there is no step at which the compiler would UTF8-encode. The
`ReadOnlySpan<char>` form buys nothing (the argument is a constant `string` either way), and the absence
of a `u8` route is one more reason to ban literals rather than encode them at runtime.

("Traditional method invocation resolution" is also why the ban holds: see below.)

**The ban does not leak.** With *both* an obsolete `AppendLiteral(string)` and a non-obsolete
`AppendLiteral(ReadOnlySpan<char>)`, the **obsolete one still wins** — `CS0619`, not a silent bind to
the span overload. That is the "traditional method invocation resolution" rule again: the exact `string`
match beats the span conversion, and `[Obsolete]` is a post-resolution diagnostic rather than a candidate
filter. Adding overloads therefore cannot bypass the ban.

The corollary is the guard rail worth knowing: the ban depends on the obsolete `string` overload
continuing to *exist*. Delete it and leave only a span overload, and literal segments silently start
binding again.

#### Idea: relax the ban to allow exactly one space

`$"{RedisCommand.SET} {key} {value}"` reads as `SET key value` — the form every Redis doc and
`redis-cli` session uses — where `$"{RedisCommand.SET}{key}{value}"` does not. Permitting a single
space, discarded at runtime, buys that.

**It does not cost the `*N` constant**, which is the ban's main justification. Spaces are literal
segments, not holes, so `formattedCount` is unchanged; only `literalLength` moves, which merely nudges
the buffer size hint. Measured:

```
tight  : args=3 literals=0 formattedCount=3 literalLength=0
spaced : args=3 literals=2 formattedCount=3 literalLength=2
```

**Nor does it cost anything measurable.** 0.45 ns per space, against an 8.6 ns baseline of pure handler
machinery in a synthetic loop that does no buffer work at all; a real render is tens to hundreds of ns,
and the operation around it is orders beyond that. Both spellings also render byte-identically, since
the space is discarded — so cache identity (§6.2) is unaffected.

**What it does cost is the compile-time guarantee.** `AppendLiteral` can no longer be
`[Obsolete(error: true)]`, so `$"SET{key}"` becomes an exception on first execution rather than a build
break. The analyzer (§7) can restore that, and it is already required for the `Resp.Raw` rules, so this
is one more rule on existing machinery rather than new machinery — with the runtime check demoted to a
backstop.

Three cases the analyzer must cover, because a runtime check handles them badly:

- **Two spaces look exactly like one.** Throwing on `$"{a}  {b}"` is correct but arrives at the worst
  moment, and the defect is invisible on the page.
- **Position.** "Exactly one space" also permits `$" {a}"` and `$"{a} "`; the rule wanted is *between*
  holes.
- **Formatters.** Nothing stops a tool normalising whitespace inside an interpolated string.

Note this also retires the "ban does not leak" property below: with no obsolete overload there is no
overload-resolution argument to lean on, and enforcement rests on the analyzer plus the runtime check.

**Non-interpolated strings do *not* bind to the handler.** If a `string` overload exists alongside,
`Write(buf, "plain literal")` silently takes it while `Write(buf, $"GET {key}")` takes the handler.
Either don't provide a `string` overload, or accept that callers must write `$"PING"`.

### 2.2 The overload set is closed — permanently

Extension `AppendFormatted` methods **do not bind**. Verified three ways, all rejected in a hole
while compiling fine as ordinary calls:

```csharp
public static void AppendFormatted(this ref H h, Geo v)   // no
public static void AppendFormatted(this H h, Vec v)       // no
extension(ref H h) { public void AppendFormatted(Blob v) } // no (C# 14 extension block)
```

The lowering does member lookup against instance members declared on the handler type and stops.
So nobody — not a consumer, not another assembly here — can extend it after the fact.

Consequences:

- Prefer a **few correct funnels over an enumeration**. Adding overloads later is additive and safe;
  removing or retyping them is breaking (AGENTS.md). Ship the minimum set.
- The funnels: `RedisCommand`, `RedisKey`, `RedisValue`, `Resp.Raw`.
- **Do not define `AppendFormatted<T>`.** A generic catch-all is an exact match by inference, so it
  beats any overload needing a conversion — anything not explicitly declared silently falls into a
  `ToString()` path and goes on the wire wrong. Omitting it makes those compile errors instead.
  (Cost: the resulting `CS1503` names an arbitrary overload from the set. Analyzer candidate.)
- With no catch-all, `RedisValue`'s existing implicit conversions cover `string`, `int`, `byte[]`
  etc. for free.

A clean rule for the two byte-ish funnels:

- **`Resp.Raw` = "I already framed this"** → explicit, because the compiler can't check the claim.
- **`RedisValue` = "you frame this"** → implicit is safe, because the framing is ours.

Note `Resp.Raw` is a `ref struct`, so it is *structurally* incapable of falling into a generic
catch-all even if one were added later.

### 2.3 `Resp.Raw` and `u8`

Pre-framed fragments enter as `Resp.Raw`, a `readonly ref struct` over `ReadOnlySpan<byte>` plus an
`ArgCount`. Reuse goes through static **properties** (a `ReadOnlySpan<byte>` cannot be a field):

```csharp
public static Raw Ex               => "$2\r\nEX\r\n"u8.Resp();
public static Raw ExpireSeconds300 => "$2\r\nEX\r\n$3\r\n300\r\n"u8.Resp(2);
```

Zero allocation; inlines to an RVA load.

`ArgCount` is the reason the wrapper exists rather than a bare `ReadOnlySpan<byte>`: **a raw fragment
can be more than one bulk string**, so without it `formattedCount` stops equalling the argument count
and the constant `*N` header silently breaks. Measured: a 2-arg fragment in a 3-hole interpolation
yields 4 args.

An implicit `ReadOnlySpan<byte>` → `Raw` conversion was tried and rejected: it re-opens the hole it
was meant to close, because *any* span — including a runtime `byte[]` payload — then claims to be a
framed fragment. The analyzer can catch bad *literals*, but the conversion's new risk is non-literal
spans, which is exactly what it cannot see. `.Resp()` costs seven characters and is the whole
assertion.

Ship `Resp()` and `Resp(int argCount)` as **separate overloads**, not one optional parameter —
adding an optional parameter later is a binary break (AGENTS.md).

**Considered and dropped: a format specifier.** The compiler supports `{value:R}`, binding to
`AppendFormatted(T value, string format)`, so a hole could have been *marked* raw rather than typed raw.
It was tried and works, but loses on three counts: the specifier is only checked at runtime, so `{x:r}`
or `{x:Raw}` compiles and silently falls through to whatever the default branch does; it cannot carry
`ArgCount`; and once raw fragments are a distinct type the marker is redundant anyway. The typed wrapper
gives compile-time dispatch and a place to put the arg count, so `:R` earns nothing.

### 2.4 `RedisCommand` and CommandMap

The command must be a `RedisCommand` so it routes through `CommandMap` (renaming/disabling per server
type). The plumbing already exists: `CommandMap.GetResp(command)` returns a **pre-encoded RESP
bulk-string fragment** (`$6\r\nLRANGE\r\n`) from one shared ~3k buffer, already uppercased
(`CommandMap.cs:225`, built at `CommandMap.cs:248-270`). So `AppendFormatted(RedisCommand)` is a
lookup and a blit.

Two consequences:

1. **It forces the receiver-passing form.** `CommandMap` is per-`ConfigurationOptions` and resolved
   at runtime, so no static lookup is possible.
2. **It rules out the `bool` short-circuit pattern.** A disabled command must *throw*, not return
   `false` — returning `false` would abandon the remaining holes and emit a truncated frame. Use
   `void` Append methods.

---

## 3. Two call shapes

### 3.1 Argument form — preferred

```csharp
public Resp Begin(RedisCommand command,
    [InterpolatedStringHandlerArgument("", nameof(command))] ref Resp handler) => handler;

using var r = writer.Begin(RedisCommand.SET, $"{key}{value}");
```

`("", nameof(command))` passes **both** the receiver and the `command` parameter into the constructor,
so the CommandMap, the command and the target are all known up front.

### 3.2 Conversion form

```csharp
using Resp r = $"{RedisCommand.SET}{key}{value}";
```

This is an interpolated string *conversion*, not an argument, so `[InterpolatedStringHandlerArgument]`
does not apply and only the `(int literalLength, int formattedCount)` ctor runs. The handler therefore
loses the receiver, which means:

- it must own a pooled buffer;
- the CommandMap has to arrive at `Close(map)`;
- the prologue must be a **fixed worst-case** reservation, since `map.MaxRespLength` is not available
  yet. (Hit as an `ArgumentOutOfRangeException` while building this.) Not a real cost: `*N` is ≤12
  bytes and command names are bounded.

Prefer the argument form. Its terseness advantage is small and the receiver is the thing you need.

### 3.3 The receiver, and a context object

`[InterpolatedStringHandlerArgument("")]` passes the **receiver** of the call into the handler's
constructor — per the spec, *"The empty string is matched to the receiver of `M1`."* Verified working in
every shape that matters — concrete receiver, receiver via an
interface, implicit `this` from inside the type, an `object`-typed ctor parameter, and extension
methods (where the receiver is the first parameter, so `nameof(db)` rather than `""`).

**Rule:** the receiver's *static type at the call site* must be convertible to the ctor parameter
type. Declaring `Execute` on `IDatabase` therefore forces the ctor to accept `IDatabase`.

That is a problem, because `CommandMap` is not reachable from there — it is not on
`IConnectionMultiplexer` and is not public API at all; `IDatabase` reaches only
`IConnectionMultiplexer Multiplexer` (`IRedisAsync.cs:14`). A downcast would work inside the
assembly but **breaks every `IDatabase` mock**, and breaks it during command *construction*, in the
caller's frame, before the mock's `Execute` is reached.

**Resolution: a dedicated context type as the receiver** — `ctx.Execute($"...")` — carrying:

| Shared per multiplexer | Varies per instance |
| --- | --- |
| CommandMap, buffer manager, client-side cache, `ServerType` | `KeyPrefix`, database index |

`ServerType` matters: `HashSlot` short-circuits to `NoSlot` for standalone
(`ServerSelectionStrategy.cs:101-102`), so without it the handler computes CRC16 over every key for
standalone deployments that never use the result.

That granularity is one context per *(multiplexer, db, prefix)* — what `RedisDatabase` already has —
so cache one per database instance rather than allocating per command.

**Class or struct:** a context that *stores* all of the above wants to be a class, since a five- or
six-field struct is copied into the handler on every command. But it does not have to store them —
see §8, where the `marc/respite` spike keeps `RespContext` to four fields and derives `CommandMap`
through the connection. That factoring is better and keeps a `readonly struct` viable.

**Cost: the context type must be public.** The accessibility chain is forced, and verified
cross-assembly — an `internal` handler constructor fails at the consumer call site with
`CS1729: does not contain a constructor that takes 3 arguments`, because it is the *consumer's*
lowered code that constructs the handler. Public ctor therefore implies a public parameter type.

Its **members can all be internal**, though: a `public sealed class` whose `CommandMap`/`KeyPrefix`/
`BufferManager` are internal works cross-assembly and gives consumers a name they can neither
construct nor read from. Verified. That is a small commitment, but a permanent one, so it belongs in
`PublicAPI.Shipped.txt` deliberately rather than being noticed at pack time.

If the context is unavailable for some path, command resolution can be **deferred** instead — the
handler stores the `RedisCommand` and the concrete implementation resolves it at `Close`/`Execute`.
That is the same mechanism §3.2 already needs, and it keeps mocks working.

### 3.4 The context is not a new idea — it is `MessageWriter`'s parameter list

Long term this replaces `MessageWriter`, and that is the clearest way to see what the context is for:
`MessageWriter`'s constructor **already takes it**.

```csharp
public MessageWriter(byte[]? channelPrefix, CommandMap? map, IBufferWriter<byte> writer)
```

`TestHarness` — already `[Experimental]`, already built for "render RESP and inspect the bytes" — goes
one further and carries all three prefixes:

```csharp
public class TestHarness(CommandMap? commandMap = null, RedisChannel channelPrefix = default, RedisKey keyPrefix = default)
```

So the context is that triple plus the routing and cancellation state (`Database`, `ServerType`,
`CancellationToken`). `TestHarness` is the closest thing to a prototype already in the tree.

**The two prefixes reach the wire by different routes today**, which is the asymmetry the context is
meant to end:

| | How it is applied today | Conditional? |
| --- | --- | --- |
| `ChannelPrefix` | writer state, applied at write time (`MessageWriter.cs:77`) | yes — skipped when `channel.IgnoreChannelPrefix` |
| `KeyPrefix` | rides on the `RedisKey` itself, put there upstream by the `KeyPrefixed*` decorators | no |

`TestHarness` mirrors that split exactly — it hands `ChannelPrefix` to the `MessageWriter` but simulates
the decorator for keys by rewriting the arguments (`TestHarness.cs:133`).

`IgnoreChannelPrefix` is not incidental: keyspace and keyevent notification channels are server-generated
names and opt out (`RedisChannel.cs:336`, `:422`), so `AppendFormatted(RedisChannel)` has to honour it
rather than prefixing unconditionally. The read side already strips the channel prefix
(`PhysicalConnection.Read.cs:817`); keys never got the equivalent, which is §8.4's read-half problem.

### 3.5 A struct context must be valid in its `default` state

If the context is a `struct`, `new RespContext()` binds the **implicit parameterless constructor** that
zeroes every field — *not* an all-optional-arguments constructor, however tempting that looks. So no
field may be assumed non-null, and `CommandMap` has to fall back to `CommandMap.Default` on read.

Found the hard way: every test in the spike threw `NullReferenceException` at the first
`AppendFormatted(RedisCommand)`. It fails at the first command rather than at construction, which is the
wrong end to debug from.

---

## 4. Deferred composition

For conditional arguments:

```csharp
using var r = writer.Begin(RedisCommand.SET, $"{key}{value}");
if (withTtl) { r.AppendFormatted("EX"); r.AppendFormatted(300); }
if (withNx)  r.AppendFormatted("$2\r\nNX\r\n"u8.Resp());
var span = r.Close();      // back-fills *N into the reserved prologue, right-aligned
```

Verified output (parsed back with RESPite's `RespReader`, `DemandEnd()` enforcing exact consumption):

```
*6|$3|SET|$5|mykey|$7|myvalue|$2|EX|$3|300|$2|NX|
*5|$3|SET|$5|mykey|$7|myvalue|$2|EX|$3|300|
*2|$3|GET|$5|mykey|
```

Also passing: empty bulk string, multi-byte UTF-8, a 5000-byte payload forcing a pool regrow *mid-build*
(after the prologue is reserved), negative integers, and 22 args forcing a two-digit `*NN` header.

**The trade:** conditional appends make the total arg count runtime-only, so the compile-time-constant
header is lost. Keep the single-expression path alongside for fixed-arity commands, where `*N` stays
constant.

`using` works on both forms, and matters here: arbitrary user code sits between construction and
`Close()`, so a throw in that window leaks the rented buffer.

**Gotcha:** `using var` cannot be passed by `ref` (CS1657). Mark resolution members `readonly` so `in`
works, or callers are forced into `try`/`finally`.

### 4.1 `Compose` / `Execute(ref cmd)` — the shape for optional arguments

Implemented in the spike (§9):

```csharp
var cmd = ctx.Compose($"{RedisCommand.SET}{key}{value}");
if (withTtl) { cmd.AppendFormatted(ex); cmd.AppendFormatted(ttl); }
using var frame = ctx.Execute(ref cmd);
```

`Compose` carries `[InterpolatedStringHandlerArgument("")]` and simply returns the handler.

Three initializer forms exist, all returning the builder:

| Form | When |
| --- | --- |
| `ctx.Compose($"{cmd}{a}{b}")` | command as the first hole |
| `ctx.Compose(cmd, $"{a}{b}")` | **preferred** — command as a real argument (§3.1), so the map is consulted before the rent |
| `ctx.Compose(cmd, argHint)` | no interpolated part at all, for a fully dynamic argument list |

The last is for the variadic case — `DEL` over a runtime-sized key array, where there is no fixed prefix
to interpolate:

```csharp
var cmd = ctx.Compose(RedisCommand.DEL, keys.Length);
foreach (var key in keys) cmd.AppendFormatted(key);
using var frame = ctx.Execute(ref cmd);
```

`argHint` only sizes the initial rent; it is not a promise, and appending more simply grows the buffer.
**`Execute(ref cmd)` needs no new overload** — and could not have one, since the attribute does not change
the signature: it binds to the same `Execute`, because the interpolated-string-handler conversion applies
only when the argument *is* an interpolated string. Passing a real variable by `ref` is an ordinary
argument and the attribute is ignored. Verified.

The trade is the one from §4: the argument count is only known at `Complete`, so `*N` is back-filled
rather than a compile-time constant. `ComposedHeaderGrowsWithLateArguments` pins the case that would
otherwise be silently wrong — three arguments at the call site, twelve by execution, so a compile-time
`*3` would have framed a corrupt command.

Keys appended after the interpolation still track and route normally
(`ComposedKeysStillTrackAndRoute`): the second key of an `SMOVE` arrives via `AppendFormatted` and is
still marked and folded into the slot.

**Ownership:** `Execute` takes the buffer on success, so there is nothing to dispose afterwards. But the
window between `Compose` and `Execute` is arbitrary user code, and CS1657 means the handler cannot be
held in a `using` while also being passed by `ref` — so a throwing window needs `try`/`finally` calling
`Dispose`, not `using`.

---

## 5. Key and slot accumulation

Needed for routing (cluster slot) and client-side cache invalidation.

### 5.1 Routing is free

Slot folding is O(1) state — one `int` — using the same logic as
`ServerSelectionStrategy.CombineSlot` (`ServerSelectionStrategy.cs:272`). No key storage at any arity.

Better still, the handler can fold the slot over **the bytes it just wrote**, rather than
re-materializing the key. `RedisKey.CopyTo(Span<byte>)`/`TotalLength()` write straight into the
output buffer; `GetHashSlot` today has to copy the key into a separate scratch buffer purely to hash
it (`ServerSelectionStrategy.cs:67-92`).

Keyspace-isolation prefixes must be applied **before** both the write and the slot.

Verified:

```
zero keys                    slot=NoSlot         keys=0
one key                      slot=10778          keys=1  [user:1]
three keys, shared hashtag   slot=4574           keys=3  [{u1}:name, {u1}:age, {u1}:email]
three keys, cross-slot       slot=MultipleSlots  keys=3  [alpha, beta, gamma]
prefix: slot(user:1)=10778   slot(tenant7:user:1)=11022  (differ)
```

### 5.2 Key marks — an MSB-discriminated `ulong`

Measurement: testing a bitmap costs **nothing** — recovering one key of N and recovering all N are
indistinguishable. The entire cost is the RESP walk (~40–100 ns for typical small frames, ~400 ns at
65 args), versus single-digit ns for a direct slice from a stored offset. So the axis that matters is
**scan vs no-scan**, not bitmap vs offsets.

```
MSB clear → [ offset_b:31 | offset_a:31 ]   0, 1 or 2 keys, resolved with NO scan
MSB set   → bitmap of arg indices           3+ keys, scan required
zero      → no keys
```

- Offsets are **byte offsets of the `$` of the fragment**. No length needs storing — `$3\r\n` is
  self-describing.
- 31 bits is ample (`proto-max-bulk-len` caps at 512 MB).
- Zero is a free sentinel for an empty slot: offset 0 can never be a key, because the frame starts
  `*N\r\n`.
- Two slots is worth it — two-key commands are common (`SMOVE`, `RENAME`, `LMOVE`, `COPY`,
  `ZRANGESTORE`, `BITOP`, `SINTERSTORE`).
- Beyond arg 63: rented `long[]`. Rare, and those are the variadic bulk commands already allocating.

**Offsets must be buffer-absolute, not frame-relative.** `Close()` right-aligns `*N` into the reserved
prologue, so the *frame* start moves with the digit count of N. A frame-relative offset is then off by
one byte in the two-digit case — and one byte before a `$` is still inside the previous fragment's
CRLF, so you often parse *something plausible* and silently register the wrong key. Verified: with a
15-arg command (`*15`), buffer-absolute offsets still resolve correctly.

Verified:

```
PASS single key, 1-digit arg count                 keys(no scan)=[user:1]
PASS two keys                                      keys(no scan)=[src:set, dst:set]
PASS single key, 2-digit count (frame start moved) keys(no scan)=[user:1]
     three keys                                    -> scan required (as designed)
PASS zero keys                                     keys(no scan)=[]
```

**Do not expose `System.Range` in the key-resolution API.** `Index`/`Range` polyfill fine down-level
(the compiler matches them by name), but the polyfill must be `internal` — public would collide with
the real types on newer TFMs. A public method taking `Span<Range>` then fails with
`CS0051: Inconsistent accessibility`. Use a purpose-built `(offset, length)` struct.

**Open seam:** on promotion to bitmap mode the two stored *offsets* must become *arg indices*, which
were never recorded, and there aren't spare bits to carry both (1 + 31 + 31 leaves one). Pragmatic
answer: re-derive by walking what's already written — rare, partial, and in L1. Needs a deliberate
decision.

---

## 6. The frame as cache key

Because the rendered bytes are the client-side cache key, **canonicality is a correctness property**,
not tidiness: two logically identical commands must render byte-identical or you get duplicate entries
and phantom misses. `CommandMap` already uppercases command names (`AsciiHash.ToUpper`,
`CommandMap.cs:270`); that has to hold for every fragment.

`Resp.Raw` is the hole — an opaque blob bypasses every normalisation:

```csharp
$"{cmd}{key}{"$2\r\nex\r\n"u8.Resp()}"   // lowercase
$"{cmd}{key}{(RedisValue)"EX"}"           // uppercase
```

Same command, two cache entries, forever. Silent, so this is the highest-value analyzer rule.

Keyspace prefixes fall out correctly for free — applied before the write, so tenants cannot collide.

### 6.1 Three incremental folds

All O(1) state, all during the write, none needing a second pass:

| State | Purpose |
| --- | --- |
| `int _slot` | routing |
| `ulong _keyMarks` | key resolution for invalidation |
| rolling hash | cache probe |

A lookup is then hash → bucket → `SequenceEqual`, with no walk unless a real collision.

### 6.2 The database number is not in the frame

`SELECT` is a separate command on the connection, so `GET foo` on database 0 and database 3 render
**byte-identically**. Cache identity is therefore `(frame, database)`, never the frame alone.

This is obvious once stated and very easy to overlook, precisely because everything *else* that affects
identity is already in the bytes — the key prefix, a renamed command from the `CommandMap`, every
argument — so the frame feels self-sufficient. The context already carries `Database`, so the fix is to
fold it into the hash alongside the bytes; the cost is remembering to.

Pinned by `DatabaseIsNotPartOfTheRenderedFrame`.

Two neighbours, which resolve differently.

**The multiplexer**, if a process talks to more than one deployment: free by scoping, assuming the cache
is per-multiplexer. Worth not hoisting it somewhere more shared without revisiting.

**The protocol version is not an identity input**, despite RESP2 and RESP3 response shapes differing.
It is negotiated per `PhysicalConnection` (`SetProtocol`, `PhysicalConnection.cs:372`, propagated to the
bridge; `ServerEndPoint.cs:148` reads it back from the interactive connection), so mixed protocols
within one multiplexer are reachable *simultaneously* — a cluster mid-upgrade, or a primary and replica
at different versions.

That is still not a reason to key on it. Since the key is `(frame, database)`, both shapes collide on
the same entry: there is exactly one, holding whichever protocol wrote it last, and any reader parses it
because the result processors have to be shape-tolerant anyway — which RESP3 support requires of them
generally. No duplicate entries, no hit-rate cost; the protocol simply does not participate.

### 6.3 Caching the result: blob by default, value by exception

`HybridCache` is the model worth copying — see
[Reuse objects](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0#reuse-objects).

Its default is that every retrieval deserializes, so each concurrent caller gets a **separate instance**.
That is deliberate: it preserves the `IDistributedCache` behaviour most callers are migrating from, so
adopting `HybridCache` cannot introduce concurrency bugs. (`string` and `byte[]` are handled internally;
everything else goes through a serializer.) Reuse is opt-in, and requires **both**:

- the type is `sealed`, and
- the type carries `[ImmutableObject(true)]`.

Applied here, the default is to cache the raw RESP response bytes and re-run the `ResultProcessor<T>`.
That is safe for any `T`, and it is the same property that makes §6.2 work: the processor is the single
place that tolerates RESP2 versus RESP3, so a cached blob is readable whichever shape it holds.

Value-caching is then the optimisation — and **the `HybridCache` opt-in does not port directly**.
`RedisValue` and `RedisKey` are `readonly struct`s, so the `sealed` half is free, but
`[ImmutableObject(true)]` would be untrue of them, and a type-level attribute cannot express why: for
`RedisValue` the answer depends on the *value*, specifically its `StorageType`.

| `StorageType` | Backing | Safe to cache by value? |
| --- | --- | --- |
| `Null`, `Int64`, `UInt64`, `Double` | the overlapped field | yes — self-contained |
| `String` | a `string` | yes — immutable |
| `ShortBlob` | 1-8 bytes inline in the overlapped field | yes — self-contained |
| `ByteArray` | a `byte[]` | **no** — see below |
| `MemoryManager`, `Sequence` | memory owned elsewhere | **no** — see below |

**`ByteArray` aliases.** The `byte[]` conversion returns the **internal array** in exactly one case —
`StorageType.ByteArray` where the value spans the whole array (`RedisValue.cs:1198-1200`; `RedisKey`
does the same via `TryGetSimpleBuffer`). Every other branch copies. So a caller can take that array,
mutate it, and poison every other holder of the same cached instance.

**`MemoryManager`/`Sequence` are a different hazard.** These reference memory the `RedisValue` does not
own, which may be a pooled lease that is later recycled. Unsafe to *retain* — unless the lease is pinned,
which is exactly what §6.4 has the cache entry doing. See "windows, not copies" below: under pinning
these stop being a hazard and become the preferred representation.

So the eligibility test wants to be a value-oriented predicate over `StorageType`, not a type-level
marker — cheap to evaluate, but evaluated per value. The alternative is to copy on the way in for the
unsafe kinds, which costs an allocation exactly where value-caching was supposed to save one.

#### Idea: never hand the cache an exact-size array

The aliasing branch fires only on `_index is 0 && _length == arr.Length`, so ensuring a cached value is
never backed by an exactly-sized array forces the copying branch instead. That is structural rather than
incidental — `byte[]` cannot express a partial view, so the operator *has* to copy — and it is close to
free, because a pooled rent is over-sized by construction.

It defends one route, though, not the invariant:

| Route | Over-sizing defends? | Why |
| --- | --- | --- |
| `(byte[])` | **yes** | `byte[]` cannot be a partial view, so the operator must copy |
| `(ReadOnlyMemory<byte>)` | no | returns `new ReadOnlyMemory<byte>(arr, _index, _length)` at any size (`RedisValue.cs:1353`) |
| `(ReadOnlySequence<byte>)` | no | delegates to the above |

`ReadOnlyMemory<byte>` is read-only only by convention: `MemoryMarshal.AsMemory` makes it mutable in one
call, with no `unsafe`. Whether that counts is a judgement call — reaching for `MemoryMarshal` to mutate
someone else's read-only memory is arguably "you broke it, you own it", and on that reading over-sizing
does close the practical *mutation* surface.

The *lifetime* half is not a judgement call: a well-behaved caller can hold the returned
`ReadOnlyMemory<byte>` past eviction, or past the pooled array being recycled, with no misuse at all.
That is §6.4 again, and over-sizing does nothing for it.

**Better variant: start at index 1.** Breaking the `_index is 0` half instead costs exactly one byte and
is a property of how the `RedisValue` is *constructed* — fully under our control — rather than depending
on the allocator having over-sized the array.

Note it is **not** already true for response-derived values. `RedisValue.FromRaw` copies anything over
`MaxInlineBytes` (8) into an exactly-sized array at index 0:

```csharp
internal static RedisValue FromRaw(ReadOnlySpan<byte> bytes)
{
    if (bytes.IsEmpty) return EmptyString;
    if (bytes.Length <= MaxInlineBytes) return new RedisValue(bytes);   // inline
    return bytes.ToArray();                                             // exact-size, index 0
}
```

So every response payload over 8 bytes is exactly the aliasing case — which also means
`byte[] blob = db.StringGet(key)` is **zero-copy today**. Applying index-1 blanket in `FromRaw` would
turn that common pattern into a copy per call: a real pessimisation, not a free byte.

Applied at *cache insert* rather than universally, though, it is better than an eager defensive copy,
because **it makes the copy lazy**: a cache hit that never asks for `byte[]` pays nothing, and one that
does pays exactly the copy it needed for safety anyway.

The `ReadOnlyMemory<byte>` caveat above is unchanged either way — that conversion windows into the array
at any index.

#### Better still: windows, not copies — and the hack disappears

If the cache entry pins the buffer (§6.4), a cached value need not be copied out of it *at all*. The
payload is a slice of the frame, sitting between `$len\r\n` and the trailing `\r\n` — so a `RedisValue`
constructed over that buffer has `_index > 0` **and** `_length < arr.Length`. Both halves of the aliasing
condition fail on their own, because the trim was required regardless. No deliberate off-by-one, no
deliberate over-allocation, nothing to explain to a future reader.

The machinery already exists: the `ReadOnlyMemory<byte>` constructor takes exactly this shape
(`MemoryMarshal.TryGetArray` → `_index = segment.Offset; _length = segment.Count; _obj = segment.Array`),
and values of 8 bytes or fewer still go inline as a self-contained `ShortBlob`, so the small case has no
coupling at all.

This also collapses the blob-versus-value distinction for blob-shaped payloads: a `RedisValue` windowed
onto the cached buffer *is* both. Re-materialising it is an offset computation rather than a parse, so
the "re-run the parser" default costs almost nothing — and the immutability question that motivated
value-caching does not arise, because nothing was ever copied out to alias.

What remains is lifetime, and it is sharper rather than softer: the handed-out value now points *into*
the entry's lease, so a caller holding a `RedisValue` across eviction is looking at recycled memory. Note
the conversions are on our side here — `(byte[])` and `(string)` both copy out of a windowed value — so
the danger is narrowly a caller who retains the `RedisValue` itself and materialises later.

That is the same §6.4 problem, but concentrated in one place (the entry's lease) rather than spread
across copies, which is probably where you want it.

Array returns are common across this API, and they are exactly the poisoning hazard that blob-by-default
exists to prevent, so the default matters more here than it might elsewhere.

Nothing in the repo is annotated for this today — no `[ImmutableObject]`, no `HybridCache` reference.

#### The same trick, already anticipated

`HybridCache`'s own cache-key guidance recommends writing the key as an interpolated string *inline at
the call site*:

> Notice that the inline interpolated string syntax (`$"..."` [...]) is directly inside the
> `GetOrCreateAsync` call. This syntax is recommended when using `HybridCache`, as it allows for planned
> future improvements that bypass the need to allocate a `string` for the key in many scenarios.

That is this document's technique, in the public guidance of the library whose caching model §6.3 is
copying: keep the interpolation at the call site so a handler can consume the parts without ever
materialising a `string`. Worth knowing that the shape is already established rather than novel.

### 6.4 Buffer ownership

A pooled buffer **must not** be retained as a dictionary key without ownership transfer — `ArrayPool`
reuse would mutate live cache keys, and the failure mode is wrong data served from cache, not a crash.

Resolution: the cache entry **pins the lease** for its lifetime; the buffer is returned on eviction.

Two requirements:

- **Dispose must be neuterable.** The transfer decision comes late (only after dispatch do you know
  whether the response is cacheable), so the consumer's `using` is correct on every path *except* the
  one where ownership moved. Precedent exists: `MemoryTrackedPool.MemoryManager.Dispose` does
  `Interlocked.Exchange(ref array, null)` and only returns if it won (`MemoryTrackedPool.cs:58`).
  `TransferOwnership()` wins that exchange first.
- **Accept permanent rounding slack.** A pooled lease is power-of-two sized, so a 130-byte frame pins
  a 256-byte array for the entry's life — roughly a third overhead on retained bytes. Minor, and a
  custom chunk pool with buckets fitted to the real frame distribution would tighten it.

Pinning also **keeps the key offsets valid**: buffer-absolute offsets stay resolvable for the entry's
whole lifetime, so keys can be recovered lazily from a cached entry without re-rendering. Copying
would have forced rebasing them by the frame-start delta — the same off-by-a-few-bytes hazard as §5.2,
reintroduced at a second site.

### 6.5 Exception paths

The lowering puts construction and all `Append` calls in the *caller's* frame, before `Execute` is
entered:

```csharp
var h = new Resp(...);       // rents here
h.AppendFormatted(cmd);      // can throw: disabled command
h.AppendFormatted(key);      // can throw: a hole's property getter
Execute(h, handler);         // consumer's using/try-finally only starts HERE
```

So on a throw in that window there is no handler for the consumer to dispose. Dropping the buffer is
the only available behaviour, and that is **accepted, not merely tolerated**: `DefaultInterpolatedStringHandler`
does exactly the same — it rents from `ArrayPool<char>.Shared` and abandons the rental if an
interpolation throws, because the compiler emits no `try`/`finally` around the append sequence. Broken
usage dumping an incomplete buffer is the established behaviour of the pattern.

It is also harmless here: `MemoryTrackedPool` is a thin wrapper over `ArrayPool<T>.Shared`
(`MemoryTrackedPool.cs:34`) with no outstanding-rental tracking and no budget, so a dropped buffer is
simply garbage.

Two notes:

- **Validate the command before renting** where it is free to do so. A CommandMap-disabled command is
  both the most likely throw here and the most likely to *repeat*, being configuration-driven. The
  command-as-argument form (§4.1) gets this for nothing, since `command` reaches the constructor. This is
  a tidiness win rather than a correctness one — see the `DefaultInterpolatedStringHandler` precedent
  above — so it is not worth contorting the API for.
- **A bounded custom pool would invalidate this.** Dropping into `ArrayPool.Shared` is free because
  Shared doesn't track; dropping a chunk from a bounded free-list permanently removes capacity and
  silently degrades to allocating every time. `CycleBuffer.AppendOrRecycle(segment, maxDepth: 2)` shows
  the bounded pattern is idiomatic here, so this needs care. Keep any dedicated pool unbounded —
  allocate on miss, return opportunistically.

---

## 7. Analyzer rules

The analyzer **does** reach consumers: `StackExchange.Redis.csproj:83-100` packs both
`eng/StackExchange.Redis.Build` and `eng/StackExchange.Redis.CodeFixes` into `analyzers/dotnet/cs`,
with hard errors at pack time if either is missing. (AGENTS.md describes that project as "Not shipped",
which is now inaccurate.)

**`.Resp()` is permitted only on literals.** That makes every rule below decidable, and it does not
break the static-property reuse pattern — `.Resp()` is still on a literal at the declaration site, and
the call site is a plain property read.

For this to be enforced rather than advisory, `Raw`'s constructor must be `internal` with `.Resp()` as
the only public factory. Internal construction stays available for `CommandMap.GetResp` and
per-connection preambles.

Rules:

1. **Raw framing** — `$`, digits, CRLF, payload of exactly that length, CRLF; repeated exactly
   `ArgCount` times, consuming the whole blob.
2. **Canonical casing** — token payloads uppercase. The cache-key correctness rule; the only one whose
   violation is silent.
3. **Receiver is `u8`, not `string`** — otherwise a runtime encode was silently paid.
4. **No raw string literals for RESP blobs** — `.gitattributes` is `* text=auto`, so a `"""..."""`
   blob bakes in platform-native line endings (LF on Linux, CRLF on Windows). Require `\r\n` escapes.
5. **No keys inside a `Raw`** — invisible to the handler, so they would never be registered for
   invalidation or counted for routing.
6. **Shape** — at least one hole; first hole is a `RedisCommand`.
7. Possibly: a better diagnostic than `CS1503` for an unsupported hole type.

Rules 1–3 have mechanical code fixes, which is presumably what the CodeFixes assembly is for.

---

## 8. Prior art: the `marc/respite` spike

`origin/marc/respite` is the v3 PoC spike — a tip (nothing contains it), 117 commits ahead of `main`,
370 files under `src/`, last commit `2026-03-06` *"support WriteMode"*. Siblings from the same push:
`marc/localwriter` (2026-03-06) and `marc/resp-reader` (2026-03-13). `marc/respite-weekend`
(2025-09-22) is a sub-branch that merged into it, not the spike itself.

Projects: `RESP.Core`, `RESPite`, `RESPite.Redis`, `RESPite.StackExchange.Redis`,
`RESPite.Benchmark`, alongside `StackExchange.Redis`.

**This document replaces the spike's *writer* half only.** The execution API around it is good and
carries over.

### 8.1 What carries over

`RespContext` — a `readonly struct` of four fields:

```csharp
private readonly RespConnection _connection;
public readonly CancellationToken CancellationToken;
private readonly int _database;
private readonly RespContextFlags _flags;

public RespCommandMap CommandMap => _connection.NonDefaultCommandMap ?? RespCommandMap.Default;
```

- `With*` clone surface — `WithCancellationToken`, `WithDatabase`, `WithConnection`, `WithFlags`,
  `With(db, flags)`, `With(db, flags, mask)`, `ConfigureAwait` — each copying the struct and assigning
  through `Unsafe.AsRef(in clone._x)`.
- Cancellation: `WithCombineCancellationToken` / `WithCombineTimeout` / `WithCombine` return a
  `Lifetime : IDisposable` owning the linked CTS, with the no-op fast paths handled (uncancellable
  token, already-equal token, no existing token to link).
- `RespContextFlags` is bit-aligned with `CommandFlags`, so `RespContextDatabase.Context(flags)` is a
  cast plus a mask — no mapping table.
- `RespContextDatabase` implements `IDatabase` over an `IRespContextSource`, split across the usual
  per-type partials.

**This is the context object §3.3 arrives at independently**, and its factoring is better than what
§3.3 first proposed: it *derives* `CommandMap` from the connection rather than storing it, which is
what keeps it to four fields and makes the `With*` clone pattern affordable. Adopt that — store the
connection, derive the rest — rather than a class holding CommandMap + prefix + buffer manager +
cache + `ServerType`.

### 8.2 What the handler replaces

The spike's writer is a manual builder: `RespOperationBuilder<T>` from
`RespContextExtensions.Command<T>()`, then `Send`/`SendAsync`/`CreateOperation`, with the frame
assembled by hand through `RespWriter`. Three specific things get better:

| Spike | Handler |
| --- | --- |
| `WriteCommand(command, args)` — caller states the arg count | `formattedCount` is a compile-time constant |
| `WriteKey(...)` — a plain alias for `WriteBulkString`, no behaviour | `AppendFormatted(RedisKey)` — prefix, slot fold, key marks |
| `public RespCommandMap? CommandMap { get; set; }` on the writer | supplied via the receiver; immutable, cannot be forgotten |

The first is the substantive one. A hand-maintained argument count can disagree with the writes that
follow it, which is exactly the failure `RenderedArgs.ThrowArgCountMismatch` exists to catch on `main`
— a runtime check for something the handler makes unrepresentable.

The second matters because `WriteKey` is where routing and invalidation have to attach. The spike cut
the seam in the right place and left it empty.

### 8.3 The key-prefix gap

The spike has `RespConfiguration.KeyPrefix` (`ReadOnlySpan<byte>`, settable as `string` or `byte[]`
on the builder) *and* distinct `WriteKey` overloads on `RespWriter` — but nothing connects them; the
prefix is never applied, and `KeyspaceIsolation/KeyPrefixed*.cs` is still present on the branch.

So "the execution API that replaces `KeyPrefixedDatabase`" is the *pattern* — a cheap value-type
context threaded through with `With*` clones, instead of a decorator object per prefix — rather than
something finished. Wiring it is part of this work: `AppendFormatted(RedisKey)` applies the prefix
before both the write and the slot (§5.1).

---

### 8.4 Key prefixes: both mechanisms, permanently

The two prefix mechanisms coexist for good — the context's prefix, and the prefix a `RedisKey` already
carries from a `KeyPrefixed*` decorator. That is awkward conceptually but free in the writer:
`RedisKey.WithPrefix` only allocates in its *"two prefixes; darn"* branch because it must hand back a
`RedisKey`; the handler needs only the combined *bytes*, and `TotalLength()`/`CopyTo()` already include
the key's own prefix. So writing the context prefix immediately ahead of them composes both with no
intermediate object. Verified: **0 bytes allocated over 128 renders** with both prefixes in play.

The context normalises its key prefix to bytes once at construction, so a string-backed prefix does not
re-convert per command. `WithKeyPrefix` still composes eagerly via `WithPrefix`, but that is once per
context clone, not per command.

The two mechanisms stay distinguishable as *values* (`RedisKey.Equals` compares the carried prefix) but
render byte-identically — pinned by `BothPrefixMechanismsRenderIdenticalBytes`. That is what lets the
rendered frame serve as a cache key (§6): cache identity must come off the frame, never off the key
object.

#### The new `KeyPrefixedDatabase`

The write half collapses to `localCtx = downstreamCtx.WithKeyPrefix(prefix)` with no per-method
overrides — roughly 2600 lines of forwarding in `KeyspaceIsolation/` become one context clone.

**The read half is the open part**, and today it is largely unhandled rather than merely imperfect:

- `KeyRandom`/`KeyRandomAsync` **throw** `NotSupportedException`, documented in the `WithKeyPrefix`
  remarks (`DatabaseExtension.cs`).
- Script and `Execute` results carry prefixed keys — seven `// TODO: ... might make sense to 'unprefix'`
  sites, and the public docs state the caveat.
- RESP APIs opt out deliberately: `// note the Resp API explicitly doesn't unprefix keys`.
- Multi-key pop results (`ListPopResult` from `ListLeftPopAsync(RedisKey[], long)`) forward unstripped,
  not even TODO'd.

The decorator *cannot* fix this: it sits above the database with no hook into result processing, so
stripping would mean wrapping every return value. A prefix on the context, threaded through to the
result processor, makes it one concern in one place. `ChannelPrefix` already has exactly this shape on
the read side (`PhysicalConnection.Read.cs:817`); keys never got the equivalent.

Two constraints on doing it:

- **Strip at the API boundary, never in the cache or routing layer.** Invalidation pushes carry key
  names, and the cache is keyed on rendered frames, which contain *prefixed* keys — so an invalidation
  in prefixed form matches as-is. Stripping earlier would silently stop invalidations matching: stale
  reads, no error.
- **Stripping must be conditional.** A script can return a key it built itself that never carried the
  prefix, so it has to be "starts with the prefix → strip, else leave", and documented as such.

Commands that return key names, and so need this: `RANDOMKEY`, `KEYS`, `SCAN`, the blocking and multi
pops (`BLPOP`/`BRPOP`/`LMPOP`/`ZMPOP`/`BZPOPMIN`/`BZPOPMAX`), `XREAD`/`XREADGROUP` stream names,
keyspace notifications, and script/`Execute` results.

---

## 9. The spike in this repo

A working spike, all `internal`, so there is no public API commitment yet.

| File | What it is |
| --- | --- |
| `src/StackExchange.Redis/FrameworkShims.InterpolatedStringHandler.cs` | the attribute polyfill (§1), same shape as the `IsExternalInit` shim |
| `src/StackExchange.Redis/Interpolated/RespContext.cs` | CommandMap, KeyPrefix, ChannelPrefix, Database, ServerType, CancellationToken; `With*` clones; `Execute` |
| `src/StackExchange.Redis/Interpolated/RespCommandHandler.cs` | renders the frame, folds the slot, marks keys |
| `src/StackExchange.Redis/Interpolated/RespFrame.cs` | rendered frame + slot + key marks + `KeyRange` |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterUnitTests.cs` | 39 tests |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterDemo.cs` | 7 worked examples, each asserting the exact frame |

Green on net10.0 and net8.0 (46 tests); net481 compiles; `-c Release /p:CI=true /p:RunAnalyzers=true`
clean.

`InterpolatedWriterDemo` is the readable end-to-end example — each case asserts the exact rendered frame,
so it doubles as documentation of what the shapes produce:

```
FixedArity                *2|$3|GET|$6|user:1|                        slot=10778  keys=user:1
KeyAndValue               *3|$3|SET|$6|user:1|$4|marc|                slot=10778  keys=user:1
KeyspaceIsolation         *2|$3|GET|$9|t7:user:1|                     slot=13865  keys=t7:user:1
OptionalArguments         *5|$3|SET|$6|user:1|$4|marc|$2|EX|$3|300|   slot=10778  keys=user:1
VariadicWithSharedHashTag *4|$3|DEL|$5|{u}:a|$5|{u}:b|$5|{u}:c|       slot=11826  keys=<scan>
CrossSlotIsDetected       *3|$3|DEL|$5|alpha|$4|beta|                 slot=MULTI  keys=alpha,beta
ChannelPrefix             *3|$7|PUBLISH|$8|app:news|$2|hi|            slot=5631   keys=<none>
```

What the tests pin, grouped by the section they belong to:

- **Framing** — `RendersCommandKeyAndValue`, `RendersExactBytes`, `MultiByteAndEmptyPayloadsRoundTrip`,
  `LargePayloadForcesBufferGrowthMidBuild` (forces a pool regrow *after* the prologue is reserved).
- **Header back-fill (§4)** — `HeaderBackfillIsRightAligned`, theory over 1/9/10/120 extra arguments, so
  the frame start moves as `*N` gains digits; it also asserts the key offset survives that.
- **CommandMap (§2.4)** — `CommandMapRenamesAreApplied`, `DisabledCommandThrows`, `CommandMustComeFirst`.
- **Prefixes (§3.4, §8.4)** — `KeyPrefixIsAppliedToTheWire`, `KeyPrefixComposesWithAKeyThatAlreadyHasOne`,
  `NestedWithKeyPrefixComposes`, `BothPrefixMechanismsRenderIdenticalBytes`,
  `ComposingBothPrefixMechanismsDoesNotAllocate`, `ChannelPrefixIsApplied`,
  `ChannelPrefixIsSkippedWhenTheChannelOptsOut`.
- **Keys and routing (§5)** — `NoKeysMeansNoSlotAndNoMarks`, `OneAndTwoKeysResolveWithoutScanning`,
  `ThreeKeysFallBackToScanning`, `StandaloneSkipsSlotComputation`,
  `ClusterFoldsTheSlotFromTheWrittenBytes`, `SharedHashTagGivesOneSlot`, `CrossSlotKeysAreDetected`,
  `SlotIsComputedFromThePrefixedKey`.
- **Cache identity (§6.2)** — `DatabaseIsNotPartOfTheRenderedFrame`.
- **Cancellation (§3.3)** — `CancellationIsObservedAndTheBufferIsReturned`,
  `CancellationTokenFlowsThroughWithClones`.
- **Deferred composition (§4.1)** — `ComposeThenConditionallyAppend` (theory over the four
  ttl/nx combinations), `ComposedKeysStillTrackAndRoute`, `ComposedHeaderGrowsWithLateArguments`,
  `ComposeWithCommandArgument`, `ExecuteWithCommandArgument`, `ComposeWithNoInterpolationAtAll`,
  `DisabledCommandThrowsFromBothInitializerForms`.

**What the spike does not do:** it stops at "the right bytes were rendered, and we know which arguments
were keys". Nothing dispatches, nothing caches, and the read half (§8.4) is untouched — so the claim
that the context can be threaded through to result processing is design, not demonstration.

---

## 10. Open questions

- **Should a `RedisChannel` fold into the same slot as keys?** The spike folds it unconditionally, which
  suits sharded pub/sub (`SPUBLISH`) but is meaningless for plain `PUBLISH`, where the channel does not
  route by slot. `RedisChannel` carries a `KeyRouted` option (`Subscription.cs:83`) that presumably ought
  to gate it, and sharing one `_slot` field between keys and channels conflates two different things.
  Visible in the worked example as `ChannelPrefix` reporting `slot=5631` for a plain `PUBLISH`.
- **`Raw` multi-arg and the bit cursor.** A fragment with `ArgCount > 1` must advance the key-mark bit
  cursor by its arg count, not by 1. Either forbid keys in `Raw` (rule 5) or have `Raw` carry its own
  bitmap to shift and OR in.
- **Promotion seam** (§5.2) — re-derive arg indices by walking, or something better.
- **Single-arg vs multi-arg `Raw`.** Restricting `Raw` to exactly one bulk string keeps `*N` a
  compile-time constant; allowing multi-arg costs runtime counting. Possibly two types.
- **A runtime-validating `Raw` factory** for fragments assembled once at startup from config — the one
  legitimate case the literal-only rule closes off.
- **Public API commitment.** A public method taking the handler forces the handler type public, putting
  every `AppendFormatted` overload into `PublicAPI.Unshipped.txt` permanently. Can the interpolated
  surface start internal (RESPite-side, used by SE.Redis) to buy room to iterate?
- **Should the handler be a `ref struct`?** It holds only a `byte[]`. Ref struct prevents capture,
  copying and double-dispose, which is why it is right — but it also blocks `using var` + `ref` (§4)
  and any async retention.
- **Static key bitmaps.** For fixed-arity commands the key positions are statically known, so the JIT
  may constant-fold the bitmap when the Append chain inlines. Not to be designed around, but the
  structure permits it and an analyzer could emit the constant if it matters.

---

## 11. Verification log

Everything above marked "verified" was compiled and, where runtime behaviour was in question, run —
first in throwaway scratch projects multi-targeting `netstandard2.0;net472;net8.0` against the real
`src/RESPite` and `src/StackExchange.Redis`, and then in the in-repo spike of §9.

Generated frames were validated by parsing them back with RESPite's own `RespReader`, including
`DemandEnd()` so the frame must be exactly consumed — over- and under-run both fail.

Hash slots were checked against published `CLUSTER KEYSLOT` values: `foo`→12182, `bar`→5061,
`hello`→866, `somekey`→11058, `""`→0, all passing, with hash-tag handling matching
`ServerSelectionStrategy.GetClusterSlot` (first `{`, first `}` after it, non-empty between).

**Not verified:** nothing was run against a live server — these are local bytes only, which is the
right contract given where this sits, but it means only *framing* is proven, not that any particular
argument combination is semantically acceptable to a server. `net461` was not compiled.
Micro-benchmark numbers in §5.2 are indicative order-of-magnitude only, not a controlled benchmark.
