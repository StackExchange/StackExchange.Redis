# Interpolated-string RESP writer

Design notes for the v3 writer work. **Nothing here is implemented** — this records what was
verified empirically, what follows from it, and what is still open.

The idea: let command construction read as

```csharp
Execute($"{cmd}{key}{value}", handler);
```

where `$"..."` binds to a custom interpolated string handler that writes RESP directly, rather than
building a `Message` + argument array. The handler is pure formatting: it runs on the caller's
thread, ahead of the critical section, and the bytes it produces double as the client-side cache
key before anything touches the muxer core. It is never near a connection.

---

## 1. Is the handler pattern usable down-level?

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
constructor. Verified working in every shape that matters — concrete receiver, receiver via an
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
so cache one per database instance rather than allocating per command. Make it a **class**: a struct
with five or six fields is copied into the handler on every command, a reference is one field.

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

### 6.2 Buffer ownership

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

### 6.3 Exception paths

The lowering puts construction and all `Append` calls in the *caller's* frame, before `Execute` is
entered:

```csharp
var h = new Resp(...);       // rents here
h.AppendFormatted(cmd);      // can throw: disabled command
h.AppendFormatted(key);      // can throw: a hole's property getter
Execute(h, handler);         // consumer's using/try-finally only starts HERE
```

So on a throw in that window there is no handler for the consumer to dispose. Dropping the buffer is
the only available behaviour, and it is fine: `MemoryTrackedPool` is a thin wrapper over
`ArrayPool<T>.Shared` (`MemoryTrackedPool.cs:34`) with no outstanding-rental tracking and no budget,
so a dropped buffer is simply garbage.

Two notes:

- **Validate the command before renting.** A CommandMap-disabled command is both the most likely throw
  here and the most likely to *repeat* (config-driven, so every call). In the argument form `command`
  reaches the constructor, so it can be checked before the rent.
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

## 8. Open questions

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

## 9. Verification log

Everything above marked "verified" was compiled and, where runtime behaviour was in question, run.
Scratch projects multi-target `netstandard2.0;net472;net8.0`, `LangVersion 14`, referencing the real
`src/RESPite` and `src/StackExchange.Redis`.

Generated frames were validated by parsing them back with RESPite's own `RespReader`, including
`DemandEnd()` so the frame must be exactly consumed — over- and under-run both fail.

Hash slots were checked against published `CLUSTER KEYSLOT` values: `foo`→12182, `bar`→5061,
`hello`→866, `somekey`→11058, `""`→0, all passing, with hash-tag handling matching
`ServerSelectionStrategy.GetClusterSlot` (first `{`, first `}` after it, non-empty between).

**Not verified:** nothing was run against a live server — these are local bytes only, which is the
right contract given where this sits, but it means only *framing* is proven, not that any particular
argument combination is semantically acceptable to a server. `net461` was not compiled.
Micro-benchmark numbers in §5.2 are indicative order-of-magnitude only, not a controlled benchmark.
