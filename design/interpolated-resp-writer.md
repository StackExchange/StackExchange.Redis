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

### 2.1 Literals become arguments (was: rejected, except a single space)

> **Superseded.** Literal text is no longer discarded, and SER309 is a **warning**, not an error. What
> follows records why rejection looked right first; the reasoning that replaced it is here.

**What changed.** `AppendLiteral` tokenizes on whitespace. If nothing has been written yet, the first
token is the **command** - parsed against the known set, mapped through `CommandMap`, framed verbatim if
unrecognised. Every later token is an ordinary **value** argument, UTF-8 encoded straight into the frame.
Whitespace-only literals still contribute nothing, so `$"{cmd} {key} {value}"` is unchanged.

So `$"SET {key} {value}"` renders byte-identically to `$"{RedisCommand.SET}{key}{value}"`, and
`$"COMMAND INFO {name.Command()}"` works.

**Why this is better than rejecting.** The rejected form did exactly what it looked like; refusing to
compile it bought correctness we did not actually need. Working-but-slower beats not-working, and the
warning still points at the faster spelling.

**The fixer forks on accessibility, not on preference.** A *leading* literal is the command, so it gets a
different fix from a token in any other position - the same positional rule the writer applies at runtime,
so the fix and the behaviour cannot disagree:

| | offered |
| --- | --- |
| leading, `RedisCommand` reachable | `RedisCommand.SET` - no parse, and a typo is a compile error |
| leading, not reachable | a `static readonly RespCommand` field, `"SET".Command(preform: true)` |
| anywhere else | the existing `[Resp]` fragment fix |

`RedisCommand` is internal, so this library's own code takes the first row and everyone else takes the
second. The field uses `preform: true` **because it is a static**: a no-op for a command the library knows,
since the command map already holds its bytes, and a real saving for a module command, where the bytes are
then built once rather than per call.

That external half is what the code-fix tests actually exercise, since the harness compiles against the
public surface with no `InternalsVisibleTo` - so even `SET` gets the field there, which is exactly right.

**Splitting on whitespace gets container commands right for free.** `$"CONFIG GET {name}"` yields three
arguments, with `CONFIG` mapped and `GET` not - which is precisely how `CommandMap` behaves, since it maps
container verbs only. That was not designed for; it fell out.

**What it costs, and what it does not.**

- Each token is parsed and encoded **per call**, where a `[Resp]` fragment or a `RespCommand` resolves
  once. That is the whole content of the warning.
- `AppendLiteral` fast-paths empty and a single space before entering the tokenizer, so the recommended
  spelling pays nothing for the readable one existing. The tokenizer is `[MethodImpl(NoInlining)]`, the
  same split as `MessageWriter`'s fallbacks and for the same codegen reason.
- Nothing allocates: the split is index arithmetic over the literal, and the encode is pointer-based
  straight into the frame buffer - `Encoding.GetByteCount(ReadOnlySpan<char>)` does not exist on
  netstandard2.0 or net461, but the `char*` overloads do.
- **A literal token is never a key.** Key-ness comes from the hole type, so routing and invalidation are
  unaffected by any of this.
- `*N` is no longer derivable from `formattedCount` for this form, which is fine: the count was only ever
  an optimisation hint, since `Compose` plus `AppendFormatted` already defeat it.

---

#### Original reasoning: literals rejected, except a single space

Every part of the command must be a hole, with one exception: a **single space**, which is discarded.

```csharp
ctx.Execute(RedisCommand.SET, $"{key} {value}")   // ok - the space is discarded
ctx.Execute($"SET {key} {value}")                 // rejected - "SET " is not a separator
ctx.Execute($"{cmd}  {key}")                      // rejected - two spaces
```

The space earns its place on readability: `$"{RedisCommand.SET} {key} {value}"` mirrors how the command
is written everywhere else, for ~1.4 ns per space (measured below).

**Why reject the rest:** with no literal segments, the compiler-supplied `formattedCount` *is* the argument
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

**Enforcement is the analyzer's, exclusively. `AppendLiteral` is a no-op with no check at all**, so the
JIT eliminates the call. That is not laziness — a runtime check would buy nothing the analyzer does not,
because *discarding a literal is benign in the way that matters*: the frame stays **well-formed**, with
an argument missing. Literals never contributed to `*N`, so the header remains correct; the server sees
a wrong command and errors, or does the wrong thing, and the connection is unaffected.

Compare `RespFragment` (§9.1), where bad bytes desync the connection for every *subsequent* command. The
principle running through both: **guard strength proportional to blast radius** — an analyzer error here,
an analyzer error *plus* a generator-emitted `#error` there. Ignoring the analyzer here is user error with
local consequences; ignoring it there corrupts other people's commands.

An earlier revision marked
`AppendLiteral(string)` as `[Obsolete(..., error: true)]`, which made *any* literal a compile error —
strictly stronger, but incompatible with allowing the space. Two findings from that revision, recorded
because they bear on the alternative:

- *Omitting* `AppendLiteral` also rejects literals, but produces a confusing pair of diagnostics
  (`CS1061` plus a bogus `CS8941` "does not return void or bool") where `[Obsolete]` gives one error
  carrying our own message.
- The obsolete ban did **not** leak: with both an obsolete `AppendLiteral(string)` and a non-obsolete
  `AppendLiteral(ReadOnlySpan<char>)`, the obsolete one still won (`CS0619`), because the exact `string`
  match beats the span conversion and `[Obsolete]` applies after resolution.

Allowing the space trades that compile-time guarantee for readability, so the analyzer (§7) has to carry
the rule instead — in particular because **two spaces look exactly like one** on the page, and a runtime
throw arrives at the worst possible moment. It also has to reject leading and trailing spaces, which
satisfy "exactly one space" but are not separators.

#### Why the space costs nothing

**It does not cost the `*N` constant**, which is the main justification for rejecting literals. Spaces are literal
segments, not holes, so `formattedCount` is unchanged; only `literalLength` moves, which merely nudges
the buffer size hint. Measured:

```
tight  : args=3 literals=0 formattedCount=3 literalLength=0
spaced : args=3 literals=2 formattedCount=3 literalLength=2
```

**It costs about 1.4 ns per space** — less than expected, but not nothing, and notably *not* zero even
with an empty `AppendLiteral`. The `Separators` benchmark renders the same four-argument command spaced
and unspaced:

```
Separators_None     62.62 ns   1.00
Separators_Spaced   66.87 ns   1.07
```

Consistent across both jobs with low deviation, so the `ldstr` and the call are not being fully
eliminated despite the method body being empty. That is ~7% of the render, and a far smaller share of the
operation around it — but "the JIT will nuke it" turned out to be optimistic, and the figure is recorded
rather than assumed.

Both spellings render byte-identically, since the space is discarded, so cache identity (§6.2) is
unaffected.

The cost is the compile-time guarantee, discussed above: enforcement rests entirely on the analyzer.
Formatters are a third hazard alongside the two already noted — nothing stops a tool normalising
whitespace inside an interpolated string.

#### Inline tokens: rejected, with a fixer

`$"{key} nx {val} withsave"` reads well, and `nx`/`withsave` are arguments rather than separators — so the
question is whether literal runs should be split into tokens. **No.** They stay rejected, and the analyzer
carries the ergonomics instead: a diagnostic on the offending literal plus a **code fixer** that rewrites
it to the declared form.

```
$"{key} nx {val}"   ->   fix   ->   $"{key} {RespLiterals.Nx} {val}"
```

Two fixes, since the token may not be declared yet:

- *"Use `RespLiterals.Nx`"* when a matching `[Resp]` declaration exists.
- *"Declare `Nx` and use it"* when it does not — the fixer adds the partial property, and the generator
  fills in the body.

You type it the natural way and take the fix; the committed code is the strict form. The ergonomic gap
closes at authoring time, which is where it is actually felt.

**Why not make it work at runtime.** It is achievable: `AppendLiteral(" nx ")` could split on spaces and
resolve each token, and the resolution need not be a lazy dictionary — this repo already generates
precisely that lookup as a hash-dispatched switch, `[AsciiHash(CaseSensitive = false)] static partial bool
TryParseCI(...)`, which is allocation-free, lock-free, and case-insensitive (so `nx` in source would still
yield the canonical `NX` bytes). The cost is not the lookup:

- **`*N` stops being a compile-time constant.** Inline tokens are literal segments, not holes, so
  `formattedCount` is no longer the argument count — and the loss applies to every call site using the
  sugar, not only the complex ones.
- **It is a second mechanism** to document, analyze and explain, alongside `RespFragment`.

`{Nx}` costs a declaration; `nx` costs an invariant.

**The analyzer and the generator are one piece of work**, not two: both are driven by the set of `[Resp]`
declarations. The generator emits the fragment bodies from them; the analyzer validates literals against
the same set, and the fixer needs it to know which member to offer.

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

Re-verified on the current compiler against the real `RespCommandHandler`, and the third case below is
the one that settles it — it is not "an instance member wins", it is that **extension lookup never runs**:

| setup | `$"{x}"` |
|---|---|
| classic `this ref` extension, other instance members present | CS0315 against the *instance* generic |
| C# 14 `extension(ref H h)` block | CS0315 against the *instance* generic |
| handler whose only member is `AppendFormatted(int)`, extension takes `Geo` | **CS1503, "cannot convert from 'Geo' to 'int'"** |

The third row is the proof. Ordinary C# consults extensions when no instance method is applicable; here
none was applicable and the compiler still bound to the instance member and failed the conversion. (That
is also where the "CS1503 names an arbitrary overload" cost below comes from.)

**Positive control:** the very same extension methods, in the same file with the same usings, compile and
*run* as ordinary calls — `cmd.AppendFormatted(new Geo())`. So they are genuinely in scope and valid; the
lowering simply does not look at them.

So nobody — not a consumer, not another assembly here — can extend the hole vocabulary after the fact
**by adding a method**. `IRespArgument` (below) is the sanctioned way back in, and being an interface on
the *argument* rather than a method on the *handler* is exactly why it works.

**Consequence for layering (§9.5):** a `RedisCommand` hole can only ever be served by an instance member
of the handler type. `RedisCommand` is an `enum`, so it cannot implement `IRespArgument` either. Whatever
assembly declares the handler type must therefore know about `RedisCommand` — which is why the handler
cannot simply move to RESPite, and why the split is by *layer* (a RESPite `RespWriter` held by value
inside the SE.Redis handler) rather than by relocation.

Consequences:

- Prefer a **few correct funnels over an enumeration**. Adding overloads later is additive and safe;
  removing or retyping them is breaking (AGENTS.md). Ship the minimum set.
- The funnels: `RedisCommand`, `RedisKey`, `RedisValue`, `Resp.Raw`.
- **Do not define an *unconstrained* `AppendFormatted<T>`.** A generic catch-all is an exact match by
  inference, so it beats any overload needing a conversion — anything not explicitly declared silently
  falls into a `ToString()` path and goes on the wire wrong. Omitting it makes those compile errors
  instead.

**The constrained form is the exception, and is now implemented:**

```csharp
public void AppendFormatted<T>(T value) where T : IRespArgument
```

The constraint is the whole difference. A type that does not implement the interface is **not
applicable**, so the undeclared cases still fail to compile — and the diagnostic gets *better*, not
worse: `CS0315` naming `IRespArgument` and what to do about it, where the closed overload set produced a
`CS1503` naming an arbitrary member (the "analyzer candidate" this bullet used to end with is
consequently no longer needed).

Overload resolution measured on the three cases that decide whether it is safe:

| both applicable | winner | verdict |
|---|---|---|
| dedicated non-generic overload vs. the generic | **non-generic** | wanted; built-ins keep their own rendering |
| implicit conversion to `RedisValue` vs. the generic | **generic** | wanted; opting in beats an incidental conversion |
| type implementing nothing (`Guid`) | *neither* — CS0315 | wanted; the protection above survives |

A `struct` implementer is a constrained call, so **nothing boxes** — pinned by an allocation test
asserting exactly zero.

**An implementer cannot miscount.** It writes by calling the handler's own `AppendFormatted` methods,
which maintain `_args`/`_argIndex`, so there is no separately declared token count to drift from what was
actually written. Contrast `RespFragment.ArgCount`, which is an assertion taken on trust — a wrong one
corrupts the `*N` header and misframes the *next* command on the connection. Writing nothing is legal and
means "no argument".

This is the argument-level counterpart to §9.4: the context is the extension point for *commands*, and
`IRespArgument` is the extension point for *argument types*. Without it, `NRedisStack` could add commands
but could not add a type that appears in one.

#### Format specifiers: a second, unrelated interface

`IRespFormattableArgument.WriteTo(scoped ref RespCommandHandler, string? format)` handles `$"{x:fmt}"`,
behind its own `AppendFormatted<T>(T, string?)` overload.

It deliberately does **not** derive from `IRespArgument`, so the three combinations are three different
contracts, each enforced by the compiler — measured, both directions:

| implements | `$"{x}"` | `$"{x:fmt}"` |
|---|---|---|
| `IRespArgument` only | writes | **CS0315**, naming `IRespFormattableArgument` |
| `IRespFormattableArgument` only | **CS0315**, naming `IRespArgument` | writes |
| both | plain form | format form |

The middle row is the reason for the split rather than a single interface: it makes the format
**mandatory**, which is how a type with no safe default forces the caller to choose — the same move §2.3
wanted when a bare `$"{ttl}"` would have to guess between `EX` and `PX`. A single interface cannot
express it, and a default interface method cannot fake it: DIMs need runtime support that `net461` and
`netstandard2.0` do not have.

A type implementing both is unambiguous because the overloads differ in **arity** — the `:` in the hole
decides, not overload betterness, so none of the resolution subtleties above apply.

*Rough edge:* in the middle row the message reads "no boxing conversion from X to `IRespArgument`", which
is accurate but does not say *"you must supply a format"*. Analyzer candidate.

#### Alignment: never

There is no `int alignment` overload and there must not be one. RESP is length-prefixed binary, so
`$"{key,10}"` would pad the payload and send a **different key**, silently — the one failure mode where
the wire bytes change and nothing complains. It is `CS1739` ("does not have a parameter named
'alignment'") today, and pinned by a reflection test asserting no `AppendFormatted` parameter is named
`alignment`, because a compile error cannot be asserted directly and "add it for symmetry with the format
overload" is the plausible way it gets broken.
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

#### Authoring raw fragments: generated partial properties

Rather than hand-writing `u8` blobs, declare the fragment and let a generator emit it:

```csharp
// what the author writes
[Resp]                private static partial Resp Ex    { get; }   // token inferred: "EX"
[Resp("foo", "bar")]  private static partial Resp FooBar { get; }   // two tokens, ArgCount 2

// what the generator emits
private static partial Resp Ex     => new("$2\r\nEX\r\n"u8);
private static partial Resp FooBar => new("$3\r\nFOO\r\n$3\r\nBAR\r\n"u8, 2);
```

Partial properties are C# 13, and `LangVersion 14` is repo-wide; verified compiling on
`netstandard2.0`/`net472`/`net8.0`, since like everything else here they are pure compiler lowering.

**Multi-token fragments are not hypothetical — they are the dominant shape.** Container commands, whose
first argument is a fixed subcommand token, account for roughly 140 call sites in `src/`: `CONFIG` (22),
`CLIENT` (22), `SCRIPT` (16), `XGROUP` (14), `PUBSUB` (13), `OBJECT` (12), `LATENCY` (12), `CLUSTER`
(11), `SLOWLOG` (10), `MEMORY` (10), `XINFO` (8). `CLIENT SETINFO LIB-NAME` is three tokens; `MAXLEN ~`
in `XADD`/`XTRIM` is two. So `ArgCount` is load-bearing rather than defensive — without it the handler's
argument count silently disagrees with the frame.

**This is an existing pattern in the tree, not a new one.** `AsciiHashGenerator` already does it with
partial *classes*:

```csharp
[AsciiHash("__keyspace@")]
private static partial class KeyspaceChannelPrefix { }   // generator emits .HashCS and .U8
```

Partial properties are simply the tidier shape — one member rather than a nested type. Two conventions
from `AsciiHashAttribute` transfer directly: the token is **inferred from the member name** unless the
attribute overrides it, and the attribute is `[Conditional("DEBUG")]` so it evaporates from shipped
metadata while the generator still sees it in source.

##### Casing

**Default to upper-case; the attribute gives verbatim control.** Counting the tokens `RedisLiterals`
actually sends: **138 upper-case, 31 lower-case, 0 mixed**, out of 165. So an inferred token — one with
no attribute, taken from the member name — should be upper-cased, which is right ~84% of the time and
matches what `CommandMap` already does to command names for the canonicality reason in §6.3.

The lower-case minority is not arbitrary, which is why a single rule is not enough: those tokens are
**values rather than keywords**. `yes`/`no`, `lib-name`/`lib-ver`, `replica`/`slave`/`sentinel`/`pubsub`,
config parameter names such as `databases`/`timeout`, the geo units `km`/`mi`/`ft`/`m`, and the markers
`#`/`-`/`+`/`*`.

So: **a token given in the attribute is used verbatim.** One rule, no extra flag, and it handles the case
that forces the issue — a single fragment containing both:

```csharp
[Resp("SETINFO", "lib-name")]   // keyword upper, attribute name lower - as sent today
```

`CLIENT SETINFO lib-name` is the shape the library sends now; emitting `LIB-NAME` would be a gratuitous
change to the wire format. Worth noting only because a generator that upper-cased everything
unconditionally would make exactly that mistake silently.

**It largely removes the need for the raw-fragment analyzer rules (§7.1-3).** Those exist to validate
hand-written `u8`: framing, matching length prefixes, uppercase tokens. A generator emits all three
correctly *by construction* — there is nothing left to check. The rule becomes "don't hand-write these"
rather than "validate what you hand-wrote", which is both easier to enforce and impossible to get subtly
wrong. It also settles the canonicality requirement from §6.3 at the source: the generator uppercases,
as `AsciiHash` already does.

The optional `argCount` defaulting to 1 is fine here, despite the binary-compat rule against optional
parameters — that rule is about *shipped public* API, and if the constructor stays internal with
`.Resp()` as the public factory (§2.3), the generated call site is inside the assembly.

#### Format specifiers: wrong for "raw", right for units

Two different uses, with opposite answers.

**Dropped — `{blob:R}` to mark a hole as pre-framed.** This is an assertion about the argument's
*nature*, which a type expresses better. It was tried and works, but the specifier is only checked at
runtime (`{x:r}` or `{x:Raw}` compiles and falls through silently), it cannot carry `ArgCount`, and once
raw fragments are a distinct type (`RespFragment`) the marker is redundant.

**Kept — `{ttl:s}` to choose an encoding the type cannot determine.** `TimeSpan` has no single correct
RESP encoding: `EXPIRE`/`EX` want seconds, `PEXPIRE`/`PX` want milliseconds. Likewise `DateTime` for
`EXPIREAT` versus `PEXPIREAT`, and `bool` for `0`/`1` versus the `yes`/`no` that `CONFIG SET` takes
(`RedisLiterals.yes`/`no` already exist). This is what format specifiers are *for*.

**The specifier can be made mandatory, by the compiler.** Declare only
`AppendFormatted(TimeSpan, string format)` and omit the one-argument overload, and `$"{ttl}"` fails to
compile (`CS1503`). So for a type with no safe default the unit is *required*, enforced by overload
resolution rather than by the analyzer — which answers the objection that sank `:R`: the dangerous case
is not a mistyped specifier but an absent one, and absence is a build error. Types that do have a safe
default (`int`, `RedisValue`) simply keep their one-argument overload and are unaffected. Verified:

```
{ttl:s}  -> 300        {ttl:ms} -> 300000      {ttl} -> does not compile
{42}     -> 42         {true}   -> 1           {true:yn} -> yes
```

**The catch is that the unit is coupled to the command**, and the compiler cannot see that. Today's code
picks both together — `useSeconds = milliseconds % 1000 == 0`, then `HEXPIRE` versus `HPEXPIRE`
(`RedisDatabase.cs:447-449`). So `$"{RedisCommand.PEXPIRE} {key} {ttl:s}"` would compile and be wrong.
That is an analyzer rule, and a new *kind* of rule: relating a specifier to the value of another hole.
Tractable, because the command is normally a literal at the call site, but more involved than the
per-hole checks in §7.

A mistyped-but-present specifier still falls through to a runtime `FormatException`, so the analyzer
should also pin the valid set per type.

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

#### How big is it, and should `.Context` be a field?

Measured: **`RespContext` is 64 bytes**, which is past the point where the JIT keeps a struct in registers,
so a by-value copy is a real one. That prompted the question of whether the grouping structs should expose
their context as a public *field* rather than a property, to avoid a copy on `strings.Context`.

**No — but the measurement points at something better.** The 64 bytes break down as four references (32),
a `CancellationToken` (8), `Database` + `ServerType` (8), and **`RedisChannel ChannelPrefix` (16)**. A
quarter of every context copy is a channel prefix that only pub/sub uses and that the `Strings`, `Hashes`
and every other data-type group never touch.

So the fix is to **shrink the thing being copied**, not to dodge one copy of it. **Done:** `ChannelPrefix`
moved into the services slot that already existed for optional capabilities (§6.7), taking the context
from **64 bytes to 48** - a quarter off *every* copy, including the ones inside `Send` on the hot path,
rather than only the rare external `.Context` read. Resolving it now costs a type test, paid only by code
that actually writes a channel.

**The slot became a chain to make this work.** One service was enough while the cache was the only one;
two are not. A `ServiceLink` holds a service plus whatever was already there, and adding **prepends** - so
the most recent of a type wins by lookup order. That removes two pieces of code rather than adding them:
"replace" needs none, because a later add shadows an earlier one; and "remove" needs none, because setting
a prefix back to `default` shadows it with an empty one that reads as absent. An array would have to be
copied on every add; a link is one allocation, immutable, and shared by every context clone.

It is allocated per context *configuration* and never per command - and only from the second service
onwards, since a context with exactly one keeps the bare object and never sees the chain at all.

Against the public field specifically: the JIT inlines a trivial getter, so partial uses like
`strings.Context.Database` are usually forwarded anyway; and a public field locks the representation,
which is precisely what the paragraph above wants to change. (Style is not the objection -
`.editorconfig` sets SA1401 to `silent`, so public fields are allowed here.)

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

> **`cmd.Append($"…")`.** A conditional fragment is now written the same way as the command itself:
> ```csharp
> var cmd = ctx.Compose($"{RedisCommand.SET}{key}{value}");
> if (withTtl) cmd.Append($"{RespLiterals.EX}{ttl}");
> using var frame = ctx.Execute(ref cmd);
> ```
> rather than a sequence of `AppendFormatted` calls whose order is the caller's to keep straight.
>
> **It moves the command rather than referring to it.** The obvious design — a handler holding
> `ref RespCommandHandler` and forwarding each call — does not compile on **any** target: *CS9050, a ref
> field cannot refer to a ref struct*. That is a language rule, not a down-level runtime gap, so narrowing
> the target frameworks would not have helped. (netfx adds CS9064 on top, but it is not the blocker.)
>
> So the command is copied into the handler, appended to, and assigned back — **including when a growth
> inside the window swapped the array**, which is the case that distinguishes a move from a share, and has
> its own test.
>
> **The move is completed at both ends.** The constructor resets the source to `default` after copying it,
> and `Append` resets the handler after assigning it back, so exactly one copy owns the pooled array at
> any instant. Without the first reset the command spends the append window as a second owner, still
> pointing at an array that a growth may already have returned to the pool; the reset makes an escape from
> that window — an exception mid-fragment — leave an empty command rather than a live-looking one.
>
> `default` rather than merely clearing the buffer, because `_hasCommand` goes false with it: every path
> off a moved-from handler is then a clean throw naming the problem (`Complete`, every `AppendFormatted`)
> or a no-op (`Dispose`, so no double return to the pool), and none of them is a `NullReferenceException`.
> This is the ownership-transfer idiom `Complete` already used for handing the buffer to a frame.
>
> That reset is also the reason the constructor parameter is `ref` rather than `in`. `in` compiles — the
> constructor genuinely only reads — and it was briefly the signature on those grounds; writing the move
> down properly made `ref` the accurate one. Both mutants (dropping either reset) are caught by test.
>
> **There is only one handler type**, which is what makes this safe rather than merely neat. A separate
> proxy type would need its `AppendFormatted` overloads kept in step with the command handler's - and the
> failure would be quiet, since adding one there without adding it here just makes `cmd.Append($"{x}")`
> stop compiling, with nothing to say why it works in the command and not in the append. The handler for
> an append simply **is** the command handler, so an append accepts exactly what the command does by
> construction. (That guard was written, as a reflection test, before the single-type version replaced the
> need for it.)
>
> **Optional arguments did not need `Append` after all.** `Append` was built for `if (cond) cmd.Append(...)`,
> and it is still the right tool for a fragment whose *presence* is a branch in the caller's own logic. But
> an argument that knows it might be absent can just say so: `AppendFormatted(Expiration)` and
> `AppendFormatted(ValueCondition)` write between zero and three tokens, so the whole of SET is
>
> ```csharp
> $"{RedisCommand.SET}{key}{value}{when}{expiry}"
> ```
>
> with no branch at all. That is the first place the design pays for itself against the existing code rather
> than merely matching it: `RedisDatabase.GetStringSetMessage` is a ~17-branch decision tree, and most of
> those branches are not about Redis — they pick between fixed-arity `Message.Create` overloads, one branch
> per token count. Arity is free here, so they evaporate.
>
> Order is the documented grammar, `SET key value [NX|XX|IFEQ cmp] [GET] [EX s|...|KEEPTTL]`, i.e. condition
> before expiration. Redis parses the tail as an order-insensitive loop — which is how the legacy builder
> gets away with emitting `EX n XX` — but other RESP servers need not be as forgiving.
>
> **The new surface emits canonical `SET` only**, where the legacy builder also reaches for `SETNX`,
> `SETEX`, `PSETEX` and `DEL`. `SETEX`/`PSETEX` are pure arity relics with identical semantics and reply.
> `SETNX` is **not** a relic — it answers `:1`/`:0` where `SET ... NX` answers `+OK`/nil — so collapsing it
> is a real, deliberate divergence: `SET ... NX` has been available since 2.6.12, and one reply shape beats
> two.
>
> **The operand tokens have one home.** `Expiration.OperandResp` and `ValueCondition.KeywordResp` hold the
> mode/keyword selection, and each writer does only its own plumbing around them, so the `MessageWriter`
> path and the handler path cannot disagree about what an `Expiration` *means*. Pinned by a test that
> renders the same command through both writers and compares bytes, across the whole matrix.
>
> Two things make it legal, both worth knowing because the errors are opaque:
> `Append` is an **extension** with an explicit `ref` parameter rather than an instance method, because as
> an instance method the compiler must pass `ref this` into the handler's constructor and then refuses the
> call (CS8350/CS8352); and that parameter is **`scoped`**, which is how "this reference does not escape"
> is said. The call site is identical either way.



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

**~~Open seam~~ — resolved.** The framing above ("there aren't spare bits to carry both") is true of the
*frame*, which must stay 64 bits. It is not true of the *writer*: `RespCommandHandler` is a `ref struct` on
the stack with no size pressure, so it maintains **both** representations as it writes — the two offsets
and a full argument-index bitmap — and `Complete()` publishes whichever fits. Nothing needs re-deriving,
because nothing is discarded any more.

The old code overwrote the two offsets with a bare `OverflowFlag` on the third key, so keys 1–3 were
recorded in *neither* form and the frame could report nothing at all; the bits it then set for keys 4+ were
never read by anything. `TryGetKeys` returning −1 was the only honest answer available to it.

Now:

| Keys | Encoding | Recovery |
| --- | --- | --- |
| 0 | zero | — |
| 1–2 | two 31-bit byte offsets | O(1), no scan |
| 3+ | `OverflowFlag` \| bitmap of argument indices | walk the frame, mapping index → range |

The walk is length-prefixed skipping over `*N\r\n` + N bulk strings — no `RespReader`, no allocation.

**The limitation that remains, and is inherent to a 64-bit field:** bit 63 is the mode flag and argument 0
is always the command, leaving bits 1–62, so **a key at argument index above 62 cannot be recorded**. That
case sets bit 0 as a "truncated" marker and `KeyCount`/`TryGetKeys` report **−1** — deliberately *not* a
partial list, because a caller tracking keys for invalidation would believe a partial list was complete and
would cache something it could never invalidate. `RespClientCache.TryBeginFill` declines such frames.

Going beyond 62 would mean heap-allocating the key list per frame, which costs an allocation on every
multi-key command to serve a case that is rare and already enormous. Not worth it unless something real
turns up.

**Rejected:** falling back to "treat every argument as a key". Over-invalidation is safe by protocol — the
server does it deliberately when its tracking table overflows — but this would register *value* bytes as
tracked keys, polluting the key table and inviting spurious invalidation from unrelated keys that happen to
match a value. Safe, but it degrades the cache in a way that is hard to observe.

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

### 5.3 `RespCommand`: resolved once, usable in either position

`"FT.SEARCH".Command()` parses, validates and (optionally) frames a command name once. What it stores
depends on whether this library knows the name, and that split is a correctness requirement rather than an
optimisation:

| | stored | why |
| --- | --- | --- |
| known (`"GET"`) | the `RedisCommand` | `CommandMap` is per-context and may rename **or disable** it; the map already holds the bytes |
| unknown, casual | the `string` | inline use encodes straight into the frame buffer - preforming would allocate an array to copy from and discard |
| unknown, `preform: true` | framed `byte[]` | a `static readonly` field pays once, then every use is a `memcpy` |

**`preform` has no effect on a known command.** `CommandMap` stores every mapped name as a pre-framed RESP
fragment already — *"ready to throw directly into the stream"* — so the bytes are preformed per map, which
is the only place they can be: the map is what decides them.

**Preforming an unknown command is safe**, and the reason is worth knowing: `CommandMap` is built by
walking the `RedisCommand` enum, so an override keyed on a name that does not parse — `FT.SEARCH`,
`JSON.GET` — is **silently ignored**. Nothing could rename it, so there is nothing to defer to. (That is
also a gap: module commands cannot be renamed or disabled client-side at all, while a server-side
`rename-command` on one works fine and is undetectable. Orthogonal, but more visible once module commands
are first-class.)

A `u8` overload takes the name as bytes, so generated code and `static readonly` fields need no `string`:
`TryParseCI` matches on bytes directly, so even a known command needs no transcoding.

**Position decides the meaning, and the bytes are identical.** First, it is the command; later, it is an
argument that names one — `$"{command}{Info}{target.Command()}"`. That second case is not a curiosity: a
server knows a renamed command **only by its new name**, so `COMMAND INFO HGET` returns nothing where
`HGET` was renamed, and you must pass the mapped spelling. Taking it from the map is the only way to get
it right, which is exactly what appending a `RespCommand` does.

Validation happens at resolution, not on the wire: a name carrying CR, LF or a space would desynchronise
the connection for every subsequent command — the `SER011` hazard — so it is rejected once, where it is
free. The framing itself is ours, which is what distinguishes this from a hand-built fragment.

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

#### Implemented: `RespRequest` / `RespPayload` (see `InterpolatedWriterCacheKeyTests`)

Two corrections to the sketch above, both found by building it.

**Reference counting, not ownership transfer.** The neuterable-`Dispose`-plus-`TransferOwnership` design
makes every holder reason about whether ownership moved, and the answer is only known after dispatch. A
count gives every holder one rule — *whoever retains, releases*. The primitive already existed:
`RefCountedBuffer` (`src/RESPite/Buffers/RefCountedBuffer.cs`), which backs `RespResult`. It is a
`MemoryManager<byte>` specifically so every `Span`/`Memory` access routes through one liveness check, and
its `TryAddRef` is already increment-if-non-zero, with the same rationale this needs:

> a reservation racing the final release must fail rather than resurrect a buffer that has already gone
> back to the pool

So the read side is `TryGetValue(key, out payload) && payload.TryRetain()`, then `try`/`finally` with
`Release()` — success means *found* **and** *count incremented from non-zero*; a zero count is a miss, not
an error. (`MemoryTrackedPool` is the same idea but is behind `#if TRACK_MEMORY`, which is defined
nowhere — it is not a live facility.)

**A lookup must not need ownership.** `Detach()` transfers the frame's buffer into a lease, and that lease
is an object: **48 bytes per call, measured**. On a cache *hit* — the common case — the caller never wanted
the buffer, so that is a per-lookup allocation buying nothing, which is precisely the cost this design
exists to remove. Hence `AsLookupKey()`, which borrows the frame's array with no lease and no allocation;
`Detach()` is for the miss path, where ownership is actually wanted.

The two are the same struct, distinguished by `IsOwned`, and the safety property falls out: a borrowed key
**cannot be retained**, so the documented store idiom (retain, then add) cannot express "put a pooled array
into the cache and then hand it back to the pool". That is the §6.4 hazard made unreachable rather than
merely documented.

Measured: a steady-state cache hit — render, probe, retain, read, release — allocates **zero** bytes.

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

The same precedent settles a second wart in the `Compose` path: the handler cannot be held by `using`,
because a `using` variable cannot be passed by `ref` (CS1657), so a throwing window between `Compose` and
`Execute` needs try/finally. `DefaultInterpolatedStringHandler` has exactly this shape and exactly this
limitation; it is a property of the pattern rather than of this design.

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

### 6.6 Invalidation: two tables, not a cross-index

Verified against the protocol first: an invalidation message carries **an array of key names and nothing
else** — no timestamp, no version, no epoch. A `null` in its place means `FLUSHALL`/`FLUSHDB`. Two further
properties shape the design more than the missing time does:

- **False invalidations are normal.** The server's invalidation table is bounded; when it fills it evicts
  by *pretending a key was modified*. Over-invalidation is routine traffic, so invalidation must be cheap
  and correctness must never depend on it being precise.
- **Tracking ignores the database.** *"There is a single keys namespace, not divided by database numbers"* —
  writing `foo` in db 3 invalidates a cached `foo` in db 2.

**The structure.** Two independent lookups rather than one cross-indexed structure:

| | key | value |
| --- | --- | --- |
| Table 1 — `RespClientCache` | rendered frame **+ database** | payload + the generations its keys had at send time |
| Table 2 — `RespKeyTable` | Redis key bytes, **no database** | a generation |

A server invalidation touches *only* table 2: one hash, one stamp. It never enumerates cache entries, which
is the whole point — under `BCAST` we are told about every key touched on the server and almost none are
ours. The database asymmetry above is protocol-faithful and looks like a bug; it is commented as such.

**Generations are global monotonic tickets, not per-key counters.** This is what makes removal and reuse
safe. A per-key counter restarting at zero can collide with a ticket a cached entry recorded before the key
was invalidated, and that entry would then validate against a key that had in fact changed.

**Entries hold the key's node directly**, so validating a hit is a dereference and a `long` compare — table
2 is never re-hashed on the hot path. The price is one invariant: *a node that leaves table 2 must be
stamped invalid first*, or entries still pointing at it would never learn. Both that invariant and the
in-flight check below are pinned by mutation-tested cases.

**The fill race is the reason any of this needs ordering.** An invalidation can land between send and
reply, and the server will not repeat it — it dropped the key from its table when it fired. Caching that
reply leaves *permanently* stale data. `TryBeginFill` captures generations at **send** time and
`TryComplete` refuses if they moved, which is the documented "caching-in-progress placeholder" without a
placeholder.

Everything fails closed: an unresolvable key, a frame whose keys cannot be enumerated, a generation that
moved — all are misses. In particular a frame whose keys cannot be named is **refused outright**, since an entry that cannot be
invalidated must not be cached. Since §5.2's seam was closed that means only one thing: a key at argument
index above 62. `MGET` over three keys caches and invalidates on any of them; `MGET` over seventy does not
cache at all.

Measured (`ClientCacheBenchmarks`): `OnInvalidate` is **~5-6 ns, zero allocation, flat from 1 to 100,000
cached keys** — about 170M invalidations/sec on one thread, for both hits and misses.

### 6.7 Sending through the cache, and what kind of cache this is

The obvious hand-written shape is **wrong**, and not in a way a careful caller can fix:

```csharp
if (!cache.TryGet(req, out resp))
{
    resp = Execute(req);
    cache.Add(req, resp);     // an invalidation between these two lines is lost forever
}
```

By the time `Add` runs there is nothing left to compare against, so an invalidation that arrived during
`Execute` cannot be detected — and the server will not repeat it, having dropped the key from its table
when it fired. The result is a *permanently* stale entry. That is why the orchestration has to own the
call: it captures generations before it sends, so the completion can see that the world moved. It is not
sugar; **it is the only shape that is correct by construction**, and the explicit
`TryBeginFill`/`TryComplete` pair is for callers who need to interleave their own dispatch.

A response that arrives after an invalidation is still *returned* — it is a legitimate answer for a read
that raced a write, and the caller would have got it anyway without a cache — it is simply not stored.

**The cache is a participant in the send, not the entry point.** An earlier shape had the cache own the
call (`cache.GetOrExecute(...)`, with the command supplying its own `Execute`) — since removed. That was
backwards twice
over: a cache that calls the executor has to sit above dispatch and know how to send, and a *command* has
no business knowing how to send itself. Inverting it gives the shape the library already has — an executor
that sends, and a handler that is exactly the `ResultProcessor` role:

```csharp
executor.Send(ref request, handler);          // no cache
executor.Send(ref request, handler, cache);   // with cache
```

Caching becomes one extra argument rather than a different API, so turning it on does not mean rewriting
call sites, and "no cache" is an ordinary case rather than a missing one.

**Neither side of the executor is a span, and neither is a `byte[]`.** This is not a detail — it is what
makes the contract usable at all:

- A **span request** cannot cross an `await`, so it rules out async; and it cannot be parked in a backlog
  for a resend after a reconnect, so it rules out retries even when synchronous.
- A **`byte[]` reply** allocates on every call, which is the cost this design exists to remove.

So both sides are pooled and reference-counted: `RespRequest` in, `RespPayload` out. The executor takes its
own reference with `TryRetain` if it needs the bytes past the call; the caller releases theirs either way.
`IRespHandler.Parse` *does* take a span, correctly — parsing is synchronous and runs inside the retained
window.

This is also why `RespCacheKey` became **`RespRequest`**: the bytes about to be sent and the cache key are
the same object, and the request role is the primary one.

`SendAsync` is deliberately **not** an `async` method. `async` forbids `ref` parameters, and the frame must
be consumed by reference so a caller's copy cannot be disposed twice; so the probe and hand-off are
synchronous and only the awaiting tail is a separate `async` method. **A cache hit therefore completes
synchronously and allocates nothing** — no state machine, no `Task`.

`TryComplete` takes the payload rather than the bytes, so a cached reply is **shared with the caller, not
copied**: it is already in a pooled reference-counted buffer, and copying it to cache it would be waste.

**The whole pattern is three members.** `IRespExecutor.Send`/`SendAsync`,
`IRespHandler<TResult>.Parse(ReadOnlySpan<byte>)`, and the extension pair carrying all the orchestration —
so the ordering rule that makes caching safe lives in exactly one place we own, instead of being exposed to
every caller. (`IRespExecutor.Database` is data, not behaviour.)

One method rather than two overloads, because **the cached path *is* the uncached path plus a probe and a
commit**: a request that cannot be cached — or a caller with no cache — falls through to the same tail
rather than duplicating it. `TryBeginFill` deliberately leaves the frame owned when it declines, which is
what makes that fall-through work. The optional parameter is acceptable only because this is experimental;
adding one to a shipped method is a binary break, so a shipping version would want overloads for headroom.

That orchestration internalises three lifetimes, in descending order of how easy each is to get wrong:

| | Handled by |
| --- | --- |
| Generations captured **before** the send | `Send` sequences the capture and the send itself |
| Payload retained across the parse, released in a `finally` | `Parse` is called inside the window |
| The request frame consumed on **every** path | `ref RespFrame`, neutered whether it became a key or not |

**A command is one expression.** `SendAsync` takes the interpolated string directly - the `ref` is implied,
exactly as `Execute` already does - with **flags before the handler** so the handler can be omitted:

```csharp
public ValueTask<RedisValue> Get(RedisKey key, CommandFlags flags = CommandFlags.None)
    => ctx.SendAsync<RedisValue>($"{RedisCommand.GET}{key}", flags.WithRetryCategory(CommandRetryReadOnly));
```

An omitted handler is resolved from `TResult` (`RespHandlers.Inbuilt<T>`), with a throw naming the type if
there is none - at the call site, not when a reply arrives. `TResult` must be explicit, because C# infers
type arguments from arguments and never from a return type.

**Flags are cumulative, and that is a correctness point rather than a style one.** The category must come
from `WithRetryCategory` at the call site, *not* from the parameter's default value. With
`flags = CommandRetryReadOnly` as a default, a caller passing `CommandFlags.FireAndForget` would silently
**replace** the category with nothing - losing both the retry semantics and, now, cacheability. People
expect flags to add. `WithRetryCategory` is first-wins, so an explicitly named category still beats ours.
`CommandFlagsExtensions` became public for this: an external command surface cannot express a default
category without it. Pinned by tests, since the failure is silent.

None of the three is visible at the call site, which reduces to:

```csharp
var req = ctx.Execute($"{RedisCommand.GET}{key}");
return executor.Send(ref req, handler, cache);   // no using, nothing to release, nothing to order
```

Executor and handler are passed as interfaces, so hold **one instance of each and reuse them** — a `struct`
implementation would box per call. Reused instances allocate nothing per request.

**Read-through, and no write path at all.** `Send` with a cache makes the cache own the fetch, which is
read-through; the raw `TryGet` + `TryBeginFill` pair is cache-aside. Neither write-through nor write-behind
applies, because **writes never go through this cache**. Coherence comes from the server telling us what
changed, which puts this closer to hardware cache coherence than to the application-caching taxonomy: we
hold no dirty state and never write back.

Two consequences worth stating:

- **Updating a cached value on write is not an option**, even in principle. Entries are keyed by rendered
  frame and hold a response *frame*, so "write through" would mean synthesising what `GET foo` will return
  after a `SET foo bar` — possible only for trivial commands and wrong in general. Invalidation is the only
  sound answer.
- **`NOLOOP` reintroduces a write-side hook.** It suppresses invalidations for keys this connection
  modified, so under `NOLOOP` the write path *must* call `OnInvalidate` itself. That is a write-through
  concern in a design that otherwise has none, and it is the one place the "no write path" story breaks.

Pinning also **keeps the key offsets valid**: buffer-absolute offsets stay resolvable for the entry's
whole lifetime, so keys can be recovered lazily from a cached entry without re-rendering. Copying
would have forced rebasing them by the frame-start delta — the same off-by-a-few-bytes hazard as §5.2,
reintroduced at a second site.

### 6.8 Transition: reusing `Message` rather than rewriting the command surface

`RedisDatabase` builds a `Message` and pairs it with a `ResultProcessor<T>`. Those are **the same two
halves as the new API** — a request that renders itself, and something that turns a reply into a result —
so the existing command surface can feed the new pipeline without being rewritten. `RespFrameWriter` is a
working demonstration (`MessageToRespFrameTests`).

| New API | Existing equivalent |
| --- | --- |
| the rendered request | `Message` + `MessageWriter` |
| `IRespHandler<TResult>.Parse` | `ResultProcessor<T>.SetResultCore(..., ref RespReader)` |
| cluster slot | `Message.GetHashSlot` — already computed, so **nothing to fold during the write** |
| argument count | already in the `*N\r\n` header the writer emits |
| key prefixes, channel prefix, command map | already applied by `MessageWriter` |

**What bytes cannot supply is which arguments were keys**, which is why this is a writer and not a post-pass
over a rendered frame — §5.2's finding applies directly. The saving grace is that `MessageWriter` kept the
distinction at the call site: `Write(in RedisKey)` is a separate overload from `WriteBulkString(in
RedisValue)`. So the whole integration is **one hook** — `Write(in RedisKey)` reports the current offset —
plus an `IBufferWriter<byte>` that accumulates and packs the marks.

Notes from building it:

- `MessageWriter` is a `readonly ref struct`, so it cannot accumulate marks itself. The recorder is a
  reference to the target writer, resolved **once per message** in the constructor (`writer as
  RespFrameWriter`), so the per-key cost is a null check on an already-loaded field.
- **Cost: below the noise floor.** A/B on `SET key value`: 66.96 ns with the hook, 68.77 ns without — the
  hooked build measured *faster*, which is proof the difference is run-to-run variance rather than signal.
  So the cost is bounded below ~3%, not that it is zero.
- Offsets suffice for ≤2 keys; beyond that the frame's encoding is argument *indices*, which the recorder
  derives by walking the finished frame once — off any hot path, and the same walk `TryGetKeys` does in
  reverse.
- Both routes render **byte-identically**, pinned by a test. That is a correctness property, not tidiness:
  the frame is the cache key, so two routes that disagreed would cache the same logical command twice.

#### The other direction: a frame becomes a message

`RespMessageExecutor` sends a pre-rendered frame through the existing pipeline - connection selection,
backlog, multiplexing, failover, all untouched. Two small pieces: a `Message` whose `WriteImpl` is a blit,
and a `ResultProcessor` that captures the raw reply (overriding `SetResult` rather than `SetResultCore`, so
it runs before `MovePastBof()` consumes the prefix bytes the capture needs).

**One message type covers every pre-formatted command.** The library has **75 `WriteImpl` overrides across
20 files**, and they exist purely because each command shape writes itself differently; once the bytes
arrive already framed, there is one shape. That is the clearest single measure of what moving formatting
upstream buys - and it is scaffolding rather than a destination, since the `Message` machinery is expected
to go away entirely in favour of execution life-cycle state.

This is what made `RespEndToEndTests` possible: `target.Strings.Set/Get` against a real server, with the
legacy API cross-checking that the bytes landed.

**The minimal run now needs no wiring at all**, because `RedisDatabase.Context` is real:

```csharp
var db = conn.GetDatabase();
await db.Strings.Set(key, "marc");
var value = await db.Strings.Get(key);
```

No cast, no executor, no context construction. The context is built once per database and cached - the
executor is a per-database object, and minting one per property access would allocate on a path meant not
to. `RedisBase.Context` still throws, so `IServer` and `ISubscriber` are untouched; `RedisDatabase` hides
it with `new`, which means the **interface mapping** must land on the derived member - if it ever landed on
the base, every extension member would throw, since they all reach the context through `IRespTarget`. That
is asserted rather than assumed. Before it, everything was validated against fakes - which
proves the shape but never that a server accepts the bytes, since only framing was ever in question.

Still open for a real transition: a cacheability predicate (Redis excludes `FT.*`, probabilistic and
time-series types, and non-deterministic commands such as `HRANDFIELD`/`ZRANDMEMBER`/`HSCAN`), and running
a `ResultProcessor` against a cached payload — it takes `ref RespReader`, which `RespPayload.GetReader()`
supplies, but it also wants a `PhysicalConnection` and `Message` for error context.

### 6.9 Cacheability: gate on the retry category, fail closed

Cacheability cannot be a list of command names. `FT.*` is not in this library at all — it lives in
NRedisStack, reaching the server through `Execute`/`ExecuteAsync` — so any rule expressed as "these
commands are excluded" is unenforceable for exactly the commands most likely to be wrong.

The flags already model this. The retry category is a 5-bit severity ladder in `CommandFlags`
(`Message.MaskRetryCategory`, bits 13–17), and `Message.UserSelectableFlags` **already includes it**, so an
external surface can declare a category today with no new API. So the gate is:

```csharp
var category = flags & Message.MaskRetryCategory;
return category != 0 && category <= CommandFlags.CommandRetryReadOnly;
```

**Both halves matter.** Zero means "nobody declared one", and zero sorts *below* `CommandRetryReadOnly` on
the ladder — so a naive `<=` would read "nobody said" as "safe to cache", which is precisely backwards for
commands this library does not define. Undeclared must mean uncacheable. That is pinned by a test, because
it is the one that fails open if written carelessly.

`flags` is therefore **not optional** on `Send`/`SendAsync`. Every `IDatabase` method in this library
already carries flags; whether a command may be cached is a property of the command, and the caller has to
say.

**Read-only is necessary, not sufficient**, and this is a gate rather than the whole test. The exclusions
are *our* commands, so they belong in command metadata rather than in flags — a compile-time property of
our own enum should not be pushed onto every call site.

**The metadata table already exists.** `CommandFlags.Category.cs` has a per-command `switch` supplying the
default retry category; a cacheability answer wants to sit beside it, not in a new structure. Same kind of
fact about the same enum.

**The list is longer than first recorded** (found in review; every entry verified to return
`CommandRetryReadOnly` from that table, so all of them pass the gate today):

| | why caching is wrong |
| --- | --- |
| `SRANDMEMBER`, `HRANDFIELD`, `ZRANDMEMBER` | non-deterministic: a cached "random" answer stops being random |
| `SCAN`, `HSCAN`, `SSCAN`, `ZSCAN` | cursor state; a cached page is meaningless |
| **`TTL`, `PTTL`** | **time-dependent**: the answer changes with the clock, with no key write, so *nothing ever invalidates it*. The same failure class as a keyless command — permanently wrong, not briefly |
| **`TOUCH`** | **the side effect is the point**: it bumps LRU/LFU state, and a cache hit skips that entirely, so the command silently stops doing its job |
| **`PFCOUNT`** | **a read that writes**: it caches the computed cardinality back into the HLL header, so a cache hit skips a real mutation |

`TOUCH` is worth dwelling on, because the codebase already contains the evidence that the two axes
diverge. Its entry in the category table reads:

> `case RedisCommand.TOUCH: // technically bumps LRU/LFU state, but that's not a "real" side effect worth blocking retries over`

Correct for retry, and exactly wrong for caching. That comment is the clearest single argument that
cacheability cannot be read off the retry category.

**One I would question rather than accept.** `DUMP` was also flagged, but it looks *correctly*
invalidated: the payload is a deterministic function of the value, and the key is tracked, so a write
invalidates it properly. The case against is benefit rather than correctness — large payloads, rarely
re-read — which is what `NoClientCache` is for. Worth a second opinion before it joins a list of things
that are *unsafe*, since mixing "wrong" with "not worth it" makes the list harder to trust.

#### Opt-out, not opt-in

The caller-facing control is **`CommandFlags.NoClientCache`** (bit 19), and caching is otherwise on by
default for anything that clears the gates. Opt-in was considered and rejected: it would mean touching
every `IDatabase` method, and a single omission makes the feature silently do nothing.

The worry that argued for opt-in was an external command that is read-only, keyed, and *not* tracked by
the server — it would be cached and never invalidated. On inspection that population is close to empty:

- `FT.*` takes an **index name, not a keyspace key**, so it is keyless and the rule above already refuses
  it. (This was my counter-example, and it was simply wrong.)
- Probabilistic and time-series types (`BF.*`, `TS.*`) are keyed on *real* keyspace keys, so tracking and
  invalidation work normally. The Redis docs exclude them because *"these types are designed to be updated
  frequently, which means caching has little or no benefit"* — an efficiency argument, not a correctness
  one, and precisely what an opt-out is for.

What remains is a third party who writes their own module, enables client-side caching, declares a
read-only retry category, and whose module reads are not registered for invalidation by the server. Note
that doing *nothing* is already safe: an undeclared category is uncacheable, so the failure needs a
positive act of mis-declaration. And caching is globally opt-in in the first place. Treating that as caller
error is consistent with how this same enum already treats retry categories, where mis-declaring gets you
duplicate writes on a reconnect — a worse outcome that we already trust callers to avoid.

`NoClientCache` suppresses the **probe as well as the store**: opting out has to mean the caller does not
receive a cached answer either, not merely that this reply is not kept.

#### Why not a new rung on the retry ladder

Tempting — it is a numeric range with gaps — but no:

- **The caller wins on the ladder.** `WithCategory` is explicit: *"if the user has already specified a
  category, that wins."* So opting out of caching via the category would *replace* the retry category, and
  a caller suppressing caching on a churny value would silently change reconnect behaviour.
- **Inserting above `ReadOnly` breaks every `<=`.** A "read-only but uncacheable" rung reads as more
  severe, so retry policies testing `<= CommandRetryReadOnly` would stop retrying it: a caching annotation
  causing a retry regression. Inserting *below* avoids that but forces recategorising every read-only
  command and leaves `ReadOnly` meaning "not cacheable".
- **The codebase already decided this.** `CommandServerSpecific` sits outside the ladder because it is
  *"an orthogonal flag, not part of the `<=`-comparable severity ladder"*. Same shape, same answer. The
  ladder orders one axis — is it safe to send again; cacheability asks another — will invalidation tell me
  when this changes.

#### Scripts: cacheable by default, opt out explicitly

`EVAL_RO` and `EVALSHA_RO` also default to `CommandRetryReadOnly`, so they pass the gate today. They are
**not** simply another row above, because **cacheability is a property of the script, not of the command
name**. Two `EVAL_RO` calls can differ entirely: one deterministic and perfectly cacheable, the next
reading `TIME` or `RANDOMKEY`. The library cannot know, and a blanket "scripts are excluded" throws away
the cacheable majority to catch the minority.

**Resolved: cacheable by default, and opting out is the caller's job.** The reasoning is stronger than
"the caller knows best", which on its own would be a weak default:

- **`EVAL`/`EVALSHA` are excluded *by default*** — they default to `CommandRetryWriteAccumulating`, well
  above read-only. So the population defaulting to cacheable is not "scripts"; it is *only* scripts where
  the caller deliberately chose the `_RO` variant.
- **`_RO` is server-enforced**, not a convention: a script that attempts a write under it errors. So the
  caller has already made a declaration, and the server has already verified half of it. Defaulting those
  to cacheable is a much smaller step than defaulting *scripts* to cacheable.

**A caller can still make a plain `EVAL` cacheable, and that is intended.** `WithDefaultCategory` is a
no-op when a category was already supplied, and `MaskRetryCategory` is in `Message.UserSelectableFlags` -
so passing `CommandRetryReadOnly` with an `EVAL` is honoured. That is not a hole: it is the same single
rule the whole gate rests on, *the declared retry category, whoever declared it*, applied without a
special case for scripts. Someone who declares a writing script read-only has already broken retry
semantics more severely than caching.

**The risk worth documenting is not non-determinism — it is undeclared key access.** Invalidation tracks
the keys the script declares; a script that reads a key it did not declare in `KEYS[]` is not tracked
against that key, so it goes stale silently and stays stale. Declaring keys properly is already mandatory
in cluster, so the guidance aligns with existing good practice rather than adding a new rule. A
read-only script that also reads `TIME` or `RANDOMKEY` is possible but much rarer, and is the caller's to
notice.

**Rejected: detecting it by inspecting the script.** A regex - or anything short of a Lua parser - loses
to computed command names, `pcall`, and string concatenation. A detector that is right most of the time is
worse than a clear rule, because people trust it; and the failure it would miss is the silent, durable
kind.

#### Diagnosability

The failure this design can still produce is silent and durable: something wrongly cached serves stale data
forever, with no error and no log. So the fill path keeps four counters — `Stored`, `RefusedByFlags`,
`RefusedNoKeys`, `RefusedRaced`. They are incremented only on a miss, which has already paid for a round
trip, so a cache hit costs nothing. "Why is this stale?" and "why is nothing being cached?" should both be
answerable without a debugger.

**Keyless commands are never cached.** Found by building this: `AllValid` over an empty dependency list is
vacuously `true`, so a keyless entry was valid *for the life of the process* — not even a flush cleared it,
since `OnFlush` stamps key nodes and there were none. Server-assisted invalidation only ever reports keys,
so a command with no keys can never be invalidated by anything. `TIME`, `PING`, `RANDOMKEY`, `INFO` would
all have been permanently stale. This also removes a slice of the non-deterministic problem for free, since
several of those commands are keyless anyway.

---

### 6.11 Request combining: instrument first

`HybridCache` collapses concurrent misses onto one in-flight operation: the first caller installs a
placeholder carrying a `TaskCompletionSource`, later callers join it, and all complete or fail together.
It is the standard answer to a cache stampede. Whether it earns its complexity *here* is a different
question, because the economics are not the same.

**A miss costs far less here.** A `HybridCache` miss invokes an arbitrary factory — a database query, an
HTTP call, hundreds of milliseconds. A miss here is a Redis round trip on an already-multiplexed
connection: a thousand concurrent misses become a thousand pipelined commands and a thousand O(1) server
lookups. That is not a stampede in the damaging sense, so the default answer is "not needed".

**Two cases flip it, and the first is self-inflicted.** Redis requires the client to drop its whole cache
when a connection is lost (§6.6), so every hot key re-fetches simultaneously — precisely when the
connection has just been re-established. And for large values, a thousand concurrent 1MB misses is a
gigabyte of network and a thousand pooled buffers to produce one entry.

#### Cancellation makes this a now-decision, not a later one

v3 adds cancellation — `RespContext` carries a token and `SendAsync` takes one — which changes the shape.
The naive implementation is then *actively wrong*: passing the first caller's token to the shared send
means one caller's cancellation aborts everyone who joined. The shared send must use a **cache-owned**
token, with each waiter observing its own independently. So if cancellation is arriving anyway, this
wants deciding alongside it rather than retrofitted around it.

**Last-man-standing is not a correctness requirement here**, though — and not because cancellation is
absent, but because **the cache is a stakeholder independent of the callers**. In `HybridCache`, if every
caller cancels, the work is pointless; there is nobody left who wants it. Here, completing the fill
populates a shared cache that later callers will hit, so it has standalone value. Let the fill complete
and commit it, and let each waiter observe its own token. Withdrawing the command when the last waiter
leaves *and* it has not yet been sent is then an optimisation, not a requirement — which removes the part
that is genuinely awkward in `HybridCache`.

#### A tolerance to state, and a cheap mitigation

Combining can hand a joiner data **older than an independent read would have given it**. If the leader
sends at T0, a write lands at T0.5, and a joiner arrives at T1, the joiner receives pre-write data for a
request that began strictly *after* the write — where its own request would have seen the write. That is
transient rather than permanent, so it is tolerable, but it should be a stated tolerance rather than an
accident.

The mitigation is nearly free: **do not join a fill whose generations have already been stamped invalid.**
The leader's dependencies are right there, so a joiner arriving after an invalidation simply sends its
own request.

#### Constraints for whenever it is built

- `NoClientCache` callers must never join — they asked not to participate in cache machinery at all.
- The in-flight table is keyed by `(frame, database)`, like table 1.
- The `TaskCompletionSource` is allocated only on a miss, so the zero-allocation hit is unaffected.
- Per-waiter cancellation wants `Task.WaitAsync`, which does not exist on `netstandard2.0`/`net461`; that
  needs a linked-TCS polyfill on down-level targets.

#### The agreed cancellation model

Settled: **the request completes or fails by itself, including caching; a caller's cancellation applies
only to that caller's await.** No extra token, no waiter tracking, no last-man-standing. `HybridCache`
needs all of that because it fronts arbitrary external systems whose work has no value once nobody is
waiting; here the fill populates a shared cache, so it is worth finishing regardless of who is still
listening.

### 6.12 Errors are not cached; nulls are

`TryComplete` refuses a reply whose first **content element** is an error — simple (`-`) or, in RESP3,
bulk (`!`).

The invariant that makes this cache sound is that a reply is **a function of the keys it depends on**, and
that the server will tell us when those change. An error need not be: it can come from server
configuration, cluster topology, ACLs, memory pressure or a module's own state, none of which key
invalidation covers — so nothing would ever evict it. Caching one turns a **transient failure into a
permanent one**, which is the same class of bug as caching a keyless command (§6.9).

`-WRONGTYPE` genuinely *is* a function of the key and would be invalidated correctly, but separating those
cases needs per-code knowledge for something that should be rare — and if errors are not rare, caching
them hides the problem instead of solving it. Hence `RefusedError`: a non-trivial count is itself worth
investigating.

**A null is a value, not a failure**, in all three spellings — `$-1` (RESP2 null bulk), `*-1` (RESP2 null
array) and `_` (RESP3). Redis tracks every key *"mentioned in the context of a read-only command"*,
whether or not it exists, so creating the key invalidates the entry. **Negative caching therefore works,
and works correctly** — which is unusual enough to be worth stating.

#### Why this cannot be a first-byte test

The obvious implementation — look at `response[0]` — is **wrong**, and wrong in the way that survives
testing. RESP3 permits attribute metadata (`|`) ahead of a value, and **nothing in the specification
exempts errors or nulls from carrying it**. So `|1\r\n$6\r\nttl-ms\r\n:1000\r\n-ERR …` begins with
`|`, passes a leading-byte check, and gets cached as though it were data — a permanently cached error,
which is exactly the failure §6.12 exists to prevent.

No server is known to emit attributes today. That is precisely why it would go unnoticed: the bug is
latent until a server, a proxy, or a future protocol revision starts using a feature the protocol already
allows.

The fix is not to parse every reply, though. **Attributes are the only construct that can precede a
value**, so if the first byte is not `|` then it *is* the first content element's prefix, and the cheap
test is **exact, not approximate**:

```csharp
return (RespPrefix)response[0] switch
{
    RespPrefix.Attribute => IsCacheableBehindAttributes(response), // NoInlining
    RespPrefix.SimpleError or RespPrefix.BulkError => false,
    _ => true,
};
```

Protocol parsing is reserved for the branch that needs it, which — no server emitting attributes today —
is in practice never taken. The slow path is `[MethodImpl(NoInlining)]` for the same reason the
`MessageWriter` fallbacks are: `RespReader` is a sizeable `ref struct`, and constructing one in a cold
branch changes codegen for the whole method.

On that path, `RespReader.TryMoveNext(checkError: false)` skips attributes and lands on the first content
element, and `RespReader.IsError` classifies it. `checkError: false` matters — the default overload
*throws* on an error, which is the very thing being detected. A reply with no content element at all
(metadata only, or empty) is refused too: unclassifiable fails closed.

Both branches are pinned: the tests fail against a first-byte-only implementation, and they fail again if
the attribute path stops classifying.

#### Decision: measure first

Not built. `RespClientCache.RedundantFills` counts fills that completed only to find the same request
already cached — two or more callers missing on the same request concurrently, which is exactly what
combining would have collapsed. It costs one increment on an already-cold path and needs no in-flight
table, so it does not presuppose the design it is evaluating. Expect near zero for ordinary traffic and a
spike after a flush.

### 6.10 Decision log

What was chosen, what was rejected, and why. Several of these were reversed during implementation; the
reversals are the useful part.

| Decision | Rejected alternative | Why |
| --- | --- | --- |
| Reference counting (`RefCountedBuffer`) | Neuterable `Dispose` + `TransferOwnership`, as §6.4 originally sketched | Transfer makes every holder reason about whether ownership moved, and the answer is only known after dispatch. A count gives one rule: whoever retains, releases. |
| `AsLookupKey()` borrows for the probe | Always `Detach()` | `Detach` allocates a lease — **48 bytes, measured** — and on a cache *hit* the caller never wanted the buffer. The split also makes storing a borrowed key unexpressible, since a borrowed key cannot be retained. |
| Global monotonic generation tickets | Per-key counters | A per-key counter restarting at zero collides with a ticket an entry recorded before invalidation, so the entry validates against a key that *did* change. |
| Two independent tables | `key → set of entries` cross-index | The set must be maintained on every insert and eviction, an N-key entry lives in N sets, and a hot key's set can be a large fraction of the cache — so invalidation is O(entries), not O(1). |
| Entries hold the key's `Node` directly | Re-look-up table 2 per hit | Validation becomes a dereference and a compare, with no hashing on the hot path. Cost: a node leaving table 2 must be stamped invalid *first*, or entries pointing at it never learn. |
| Executor owns the send; cache is a participant | `cache.GetOrExecute(...)` | A cache that calls the executor must sit above dispatch and know how to send; and a *command* has no business knowing how to send itself. Splitting yields `IRespExecutor` + `IRespHandler`, which are `Message` + `ResultProcessor`. |
| One `Send` with an optional cache | Two overloads | The cached path *is* the uncached path plus a probe and a commit, so an uncacheable request falls through to the same tail instead of duplicating it. |
| `RespRequest` / `RespPayload` on both sides | `ReadOnlySpan<byte>` in, `byte[]` out | A span cannot cross an `await` **or be parked in a backlog for a resend** — so it rules out async *and* retries even synchronously. `byte[]` allocates per call. |
| `SendAsync` is not an `async` method | Plain `async` | `async` forbids `ref` parameters, and the frame must be consumed by reference. Keeping the probe synchronous also makes a cache hit complete with **no state machine and no `Task`**. |
| `TryComplete` takes the payload | `TryComplete` takes the bytes | The reply is already in a pooled reference-counted buffer; copying it to cache it is waste. |
| Caching is **opt-out** (`NoClientCache`) | Opt-in | Opt-in means touching every `IDatabase` method, and one omission makes the feature silently do nothing. The population that argued for opt-in turned out to be nearly empty — see below. |
| A separate flag bit | A new rung on the retry ladder | `WithCategory` says the caller's category wins, so opting out of caching would *replace* the retry category and change reconnect behaviour. A rung above `ReadOnly` also reads as more severe, so `<= ReadOnly` retry policies would stop retrying it. `CommandServerSpecific` sits outside the ladder for exactly this reason. |
| Non-determinism lives in command metadata | A `CommandFlags` bit | `SRANDMEMBER`/`SCAN`/`HRANDFIELD` are compile-time properties of our own enum; pushing them onto every call site is burden without benefit. |
| Keyless requests are never cached | Cache them | Invalidation only ever reports **keys**, so a keyless entry is vacuously valid for the life of the process — not even a flush clears it. Found by building it, not by reasoning. |
| Handler maintains **both** key-mark forms | Re-derive arg indices on promotion | §5.2 assumed there were no spare bits — true of the *frame*, false of the writer, which is a stack `ref struct` with no size pressure. |
| >62 arguments reports "cannot report keys" | Report the first 62 | A partial list is worse than none: a caller tracking keys for invalidation would believe it complete and cache something it can never invalidate. |
| Fast byte test, `RespReader` only behind an attribute | Parse every reply | Attributes are the only thing that can precede a value, so a non-`\|` first byte *is* the content prefix - the cheap test is exact, and parsing is reserved for a branch that is in practice never taken (§6.12). |
| Classify the reply with `RespReader` | Test `response[0]` | RESP3 attributes may precede any value, and nothing exempts errors from carrying them - so a first-byte test caches an error hidden behind metadata. Latent today because no server emits attributes, which is what makes it dangerous (§6.12). |
| `EVAL_RO`/`EVALSHA_RO` cacheable by default | Exclude all scripts; or detect by inspecting the script | `EVAL`/`EVALSHA` are excluded *by default*, so this applies only where the caller chose the `_RO` variant *and the server enforces it*. An explicit `CommandRetryReadOnly` on a plain `EVAL` is honoured, deliberately - one rule, no script special case. Inspecting the script loses to computed command names and `pcall` (§6.9). |
| Exclusions live in command metadata, beside the retry category | A `CommandFlags` bit | `CommandFlags.Category.cs` already classifies per enum value; cacheability is the same kind of fact about the same enum, and a flag would burden call sites with something we know (§6.9). |
| Errors never cached; nulls always | Cache errors too, or treat null as a miss | A cached reply must be a function of the tracked keys; an error need not be, so nothing would evict it and a transient failure becomes permanent. A null *is* a function of the key, and Redis tracks keys that do not exist, so negative caching is correct (§6.12). |
| Cancellation applies only to the caller's await | HybridCache's extra token + waiter tracking | The fill populates a shared cache, so it has value once nobody is waiting - unlike an arbitrary external system, where it does not (§6.11). |
| Request combining deferred, with a counter | Build it now | A miss is a round trip on a multiplexed connection, not an arbitrary factory call, so the stampede economics differ by orders of magnitude. `RedundantFills` measures whether it is real without presupposing the design (§6.11). |
| >62 arguments declines to cache | "Treat every argument as a key" | Over-invalidation is safe by protocol, but this registers *value* bytes as tracked keys, polluting the key table and inviting spurious invalidation from unrelated keys that happen to match a value. Safe, and invisibly degrading. |

**One reversal worth recording explicitly.** The case for opt-in rested on "an external command that is
read-only, keyed, and untracked" — with `FT.SEARCH` as the example. That was wrong: `FT.*` takes an *index
name*, not a keyspace key, so it is keyless and already refused. The keyed module commands (`JSON.GET`,
`TS.RANGE`, `BF.EXISTS`) operate on real keys the server does track, and the Redis docs exclude the
probabilistic and time-series families on **efficiency** grounds — *"designed to be updated frequently,
which means caching has little or no benefit"* — which is exactly what an opt-out is for. With the
counter-example gone, the argument went with it.

**A race that is not a defect.** Validation is not atomic across an entry's keys: validate A, an
invalidation for A lands, validate B, serve. The read could have completed a microsecond earlier and been
equally correct, so either outcome is a legitimate observation. It is bounded to reads that overlap the
invalidation, and everything after it is correct. Recorded as a deliberate tolerance rather than something
to fix.

### 6.13 `CLIENT TRACKING`: the mode, decided

Everything below was checked against a live Redis 8.9.241 rather than inferred; the error text is quoted
from the server.

**RESP3 only; no `REDIRECT`.** Not merely because two connections are more work — the redirected model has
a race the single-connection model cannot have. The invalidation can arrive *before* the reply it
invalidates, so a client caches a value it has already been told to drop, and the documented workaround is
to write a placeholder entry before sending and refuse the fill if it disappears. We happen to implement
exactly that already (`TryBeginFill` + `Dependency.AllValid` + stamp-before-drop, §6.6), so we would
survive it — but there is no reason to pay for a protocol that requires it. When RESP3 is unavailable,
client-side caching must **refuse loudly**, not silently degrade into a cache nothing invalidates.

**`BCAST`, with the empty prefix by default.**

```
CLIENT TRACKING on PREFIX foo                      → ERR PREFIX option requires BCAST mode to be enabled
CLIENT TRACKING on BCAST PREFIX foo PREFIX foob    → ERR Prefix 'foo' overlaps with another provided
                                                     prefix 'foob'. Prefixes for a single client must
                                                     not overlap.
```

Multiple prefixes are an OR; no prefix under `BCAST` means the empty prefix, i.e. every key.

**`PREFIX` does not map onto `WithKeyPrefix`, and this is the trap worth recording.** Prefixes are
connection-global, must not overlap, and cannot be removed individually ("to remove all prefixes, disable
and re-enable tracking"). Context key-prefixes routinely *nest* — `app:` and `app:users:` — which is
precisely the rejected case, and a context going out of scope has no way to deregister. So the prefix set
is an explicit connection-level tuning knob, never derived per-context. Registration is O(N²) and server
CPU scales with prefix count.

The honest cost of `BCAST` with the empty prefix is a push for every key modified by anyone. Client-side
that is cheap — `OnInvalidate` is ~5-6ns and allocation-free, which is exactly why it was measured that way
(§6.6) — but the network cost is real, and is the reason `PREFIX` exists at all.

**`OPTIN`/`OPTOUT` are therefore off the table.**

```
CLIENT TRACKING on BCAST OPTIN   → ERR OPTIN and OPTOUT are not compatible with BCAST
CLIENT TRACKING on; CLIENT CACHING yes → ERR CLIENT CACHING YES is only valid when tracking is enabled
                                         in OPTIN mode.
CLIENT TRACKING on; CLIENT CACHING no  → ERR CLIENT CACHING NO is only valid when tracking is enabled
                                         in OPTOUT mode.
```

Note the default mode is *not* `OPTOUT`: both track everything, but only `OPTOUT` unlocks per-command
exclusion, and the default mode has no escape hatch at all. Choosing `BCAST` removes the question. We lose
little: `CommandFlags.NoClientCache` already opts out client-side at zero protocol cost, and the only thing
a server-side opt-out buys is invalidation-table memory — which under `BCAST` is zero.

*If we ever went default-mode:* `OPTIN` needs a positive flag (`CommandFlags.ClientCache`) and a
`CLIENT CACHING yes` pipelined immediately ahead of each command, with two traps — it applies to **all**
commands in a following `MULTI`, and to **all** commands executed by a following Lua script.

**Invalidate locally on every write. Do not enable `NOLOOP`.** Two separate decisions that look like one.

Local invalidation of the keys a write touches is a strict improvement, independent of `NOLOOP`:
over-invalidating is always safe (§6.6 accepts false invalidations by design), the frame already carries
key marks so it costs almost nothing, and it closes the window between our write landing and the push
coming back. Keyless flushes map to `OnFlush()`.

`NOLOOP` is a different matter, and the server documentation is unusually blunt about why:

> "With tracking in the default mode, the server removes the key from the invalidation table when the key
> is modified. If the connection that modified the key is using `NOLOOP`, Redis suppresses the invalidation
> message to that connection, **but the key is still no longer tracked for that connection after the
> write.**"

So in default mode, `NOLOOP` without exact local invalidation is not "briefly stale" — it is
**permanently** stale: we keep the entry, the server has stopped tracking it, and a *third party's* later
write produces no message for us either. And "exact" is the problem: any command whose key set we
under-declare — `EVAL`/`EVALSHA` with computed keys, anything whose key spec we do not model — lands in
that case. Under `BCAST` there is no invalidation table and the hazard does not arise, which is another
point in `BCAST`'s favour, but it stays a later optimisation rather than part of the first cut.

### 6.14 Global cache, contextual TTL

**The cache is global.** Two facts force it. Tracking is per-*connection* (by client id), and the server
keeps *"a single keys namespace, not divided by database numbers"* — a change to `foo` in db 3 invalidates
`foo` cached from db 2, which §6.6 already handles (`InvalidationCrossesDatabases`).

- In **default mode**, only the connection that *read* a key is told about it. So every connection serving
  cacheable reads needs tracking on, and a push arriving on one connection must evict entries populated via
  another — entries are keyed by (frame, database), not by connection.
- In **`BCAST` mode**, invalidations reach every client subscribed to the prefix regardless of who read, so
  **one** tracking connection serves the whole multiplexer.

Either way the cache is global; `BCAST` merely makes it clean — one subscription, one push stream, no
per-connection bookkeeping, no duplicate invalidations. So `WithCache` means "participate, or not" (plus
substitution in tests), **not** "bring your own": two different caches over one multiplexer is not
supportable, because the invalidation stream has exactly one destination.

**The TTL is not global.** How stale a caller will tolerate is a per-caller policy, not a property of the
connection — and it is the one piece of cache configuration that *cannot* be added to the existing
surface, because `IDatabase.StringGet` cannot grow a parameter without a binary break (AGENTS.md). On the
context it is free and reaches every command without touching a signature, which is §9.4 paying off again.

**It must be applied on read, not stamped on store.** The entry is shared, so the fill timestamp goes with
the entry and `TryGet` takes a maximum age from the *reading* context. One entry serves any number of
contexts with different tolerances; stamping at store time would force identical replies to be cached once
per distinct TTL.

Two things still to settle:

- **Where it sits on the context.** A `TimeSpan` field pushes `RespContext` past its 48 bytes; the service
  slot keeps it there at the cost of a chain walk per cache read. That is the same trade `ChannelPrefix`
  was measured for (§3.3), so measure rather than guess — noting this one is on the *hit* path, where the
  alternative is a network round trip.
- **There should be a default, not only an override.** The server documentation recommends a maximum TTL on
  every entry as a backstop against exactly the staleness bugs above. So: a default on the cache, overridable
  per context.

**Still unwired, and both come straight from the same documentation:** losing the connection must flush the
cache (`OnFlush()` exists; nothing calls it on disconnect), and there is no TTL of any kind today.


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
7. **Inline literal tokens** — reject, with a fixer offering `{RespLiterals.Nx}`. See §2.1. **Implemented**
   as `SER309` (`RespInterpolationAnalyzer` + `RespLiteralCodeFixProvider`).
8. Possibly: a better diagnostic than `CS1503` for an unsupported hole type.

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

## 9. The spike in this repo

A working spike. The surface is public but gated behind `SER010`/`SER011` — see §9.1.

| File | What it is |
| --- | --- |
| `src/StackExchange.Redis/FrameworkShims.InterpolatedStringHandler.cs` | the attribute polyfill (§1), same shape as the `IsExternalInit` shim |
| `src/StackExchange.Redis/Interpolated/RespContext.cs` | CommandMap, KeyPrefix, ChannelPrefix, Database, ServerType, CancellationToken; `With*` clones; `Execute` |
| `src/StackExchange.Redis/Interpolated/RespCommandHandler.cs` | renders the frame, folds the slot, marks keys |
| `src/StackExchange.Redis/Interpolated/RespFrame.cs` | rendered frame + slot + key marks + `KeyRange` |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterUnitTests.cs` | 41 tests |
| `src/StackExchange.Redis/Interpolated/RespFragment.cs` | pre-framed token runs + the `[Resp]` marker |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterDemo.cs` | 7 worked examples, each asserting the exact frame |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterFragmentTests.cs` | 16 tests; declarations only - the generator supplies the bodies |
| `src/StackExchange.Redis/Interpolated/RespRequest.cs` | the rendered request; also the cache key (§6.4) |
| `src/StackExchange.Redis/Interpolated/RespPayload.cs` | a reply as a pooled, reference-counted blob (§6.4) |
| `src/StackExchange.Redis/Interpolated/RespClientCache.cs` | table 1: `(request, db)` to payload + generations (§6.6) |
| `src/StackExchange.Redis/Interpolated/RespKeyTable.cs` | table 2: key bytes to a generation; the only thing invalidation touches (§6.6) |
| `src/StackExchange.Redis/Interpolated/RespExecutor.cs` | `IRespExecutor`, `IRespHandler<T>`, and the `Send` orchestration (§6.7) |
| `src/StackExchange.Redis/Interpolated/RespFrameWriter.cs` | renders an existing `Message` into a `RespFrame` (§6.8) |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterCacheKeyTests.cs` | 12 tests: zero-alloc hits, use-after-release, concurrent readers vs eviction |
| `tests/StackExchange.Redis.Tests/RespClientCacheTests.cs` | 44 tests: invalidation, the in-flight race, flag gates, counters |
| `tests/StackExchange.Redis.Tests/MessageToRespFrameTests.cs` | 7 tests: existing `Message` objects through the new pipeline |
| `tests/StackExchange.Redis.Tests/InterpolatedWriterCapacityTests.cs` | buffer arithmetic asserted directly, where pool slack cannot mask it |
| `tests/StackExchange.Redis.Benchmarks/ClientCacheBenchmarks.cs` | `OnInvalidate` under a broadcasting flood |

Green on net10.0 and net8.0 (58 tests); net481 compiles; `-c Release /p:CI=true /p:RunAnalyzers=true`
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

- **Literals (§2.1)** — `SingleSpacesAreAllowedAndDiscarded` (spaced and unspaced render identical
  bytes), `OtherLiteralsAreRejected` (two spaces, a hyphen, a leading command name).
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

### 9.1 Public API, and the `RedisCommand` problem

The surface is public but **experimental**, under two diagnostic IDs:

| | |
| --- | --- |
| `SER010` | the feature as a whole; in the repo-wide `NoWarn`, so internal use is quiet |
| `SER011` | **hand-constructing a `RespFragment`**; deliberately NOT in `NoWarn` |

`SER011` exists because nothing validates the bytes handed to `new RespFragment(...)`: a length prefix
that disagrees with its payload, a missing CRLF, or an `ArgCount` that does not match the `$` runs will
corrupt the connection for every command that follows, with the first symptom appearing somewhere
unrelated. So the ctor is gated, and *generated* code suppresses it at the emit site and nowhere wider:

```csharp
#pragma warning disable SER011 // this half stands in for the generator
internal static partial RespFragment EX => new("$2\r\nEX\r\n"u8);
#pragma warning restore SER011
```

Verified that it fires unsuppressed, as an error carrying the docs link
(`https://seredis.dev/exp/SER011`).

**Making it unsuppressible is possible; the question is whether it should be.** Two mechanisms: an
analyzer rule tagged `WellKnownDiagnosticTags.NotConfigurable`, or a generator that detects a
hand-written construction and emits `#error`. The second is the stronger, and is genuinely absolute —
verified that `#error` survives both a blanket `#pragma warning disable` and a targeted
`#pragma warning disable CS1029`:

```
error CS1029: #error: 'RespFragment constructed by hand at Foo.cs(12,34); see https://seredis.dev/exp/SER011'
```

Two objections that look like blockers are not:

- *"It points at generated source rather than the call site."* The **message** carries whatever text the
  generator puts in it, including the offending file and position — as above.
- *"The generated-code exemption is spoofable."* It is not: the generator knows precisely which
  constructions are its own, because it emitted them. That is identity, not an `<auto-generated>` header
  check.

**Do both.** They are not alternatives — they do different jobs, and the weaknesses cancel:

| | Job | Suppressible |
| --- | --- | --- |
| `[Experimental(SER011)]` | code fix — "convert to a `[Resp]` partial property" | yes |
| generator emits `#error` | the build fails regardless | no |

The generator does not need to know whether the diagnostic was suppressed; it emits `#error` for every
construction it did not emit itself. So suppressing `SER011` removes the squiggle and changes nothing
about the outcome, which is honest — the suppression stops claiming to achieve something it does not.

**`#error` is not stuck at "right here":** `#line` redirects it, so the generator can report *at the
offending call site*, in a file it never wrote. Both forms verified, reporting into a `Consumer.cs` that
does not exist:

```csharp
#line 42 "Consumer.cs"                      // -> Consumer.cs(42,8)
#line (12, 34) - (12, 58) 1 "Consumer.cs"   // -> Consumer.cs(12,40), the C# 10 span form
#error RespFragment constructed by hand; see https://seredis.dev/exp/SER011
#line default
```

The span form is the one added in C# 10 for generators. Note the mapped column derives from the physical
column in the *generated* file adjusted by the `charOffset` argument — asking for column 34 gave 40 — so
landing it exactly means laying the emitted line out deliberately, not merely stating the offsets.

Since generators run in the IDE, this gives a live squiggle in the right place, which leaves the code fix
as the analyzer's only unique contribution.

**This only works with a sanctioned escape hatch, and it has to exist first.** A
`RespFragment.CreateValidated(bytes, argCount)` that checks framing at runtime, which the generator
ignores. Then "you cannot hand-roll a fragment" is a true statement with a supported answer for the
startup-built case from §2.3, rather than a dead end. Without it, the absolute block is the hostile
version.

The one remaining tension is with this repo's own bar for an error. `Diagnostics.cs` reserves
`DiagnosticSeverity.Error` for code that *cannot work*, and a hand-built fragment with correct bytes
works. The counter-argument, which carries here: **the blast radius is not the caller's own code.**
Malformed RESP desyncs the connection for every *subsequent* command, so the damage is unbounded and
lands somewhere unrelated. That is a different class of hazard from "you get a wrong answer", and it is
what justifies the asymmetry.
- **It forecloses the legitimate case** in §2.3 unless `CreateValidated` ships alongside it — which is
  why that is a precondition rather than a nicety.

Not yet built: the generator does not exist (§2.3 spikes the pattern with both halves hand-written), so
the `#error` half is a design, not a demonstration. What *was* verified is the part it depends on — that
a `#error` anywhere in the compilation cannot be suppressed.

Until then, keeping `SER011` out of `NoWarn` is the proportionate friction: blanket-disabling means
adding it to the csproj where review sees it, and per-site suppression means a pragma naming `SER011`. Blanket-disabling it means
adding it to the csproj, where review sees it; per-site suppression means a pragma naming `SER011`, which
makes `git grep SER011` an exact inventory of every hand-rolled fragment in a codebase. Auditable beats
absent, for a failure mode whose whole problem is invisibility. If more teeth are wanted, the
proportionate lever is a rule that flags a *project-wide* suppression while leaving per-site pragmas
alone.

One portability note: `ExperimentalAttribute.Message` is .NET 9+, so a custom message cannot be used
while this targets net461 through net10.0. The explanation lives in `docs/exp/SER011.md`, which
`UrlFormat` links to — which is the existing convention here anyway.

**The blocker was `RedisCommand`, which is `internal`.** The whole design routes commands through it for
`CommandMap` aliasing, but the public surface deliberately exposes commands as typed methods or
`Execute(string command, ...)`; making that enum public would be a large, permanent commitment to an
implementation detail.

Resolved by giving public callers a **string overload that speculatively parses**, which is exactly what
`Execute(string)` already does (`RedisDatabase.cs:6149-6155`):

```csharp
if (!RedisCommandMetadata.TryParseCI(adhocCommand, out knownCommand))
    knownCommand = RedisCommand.UNKNOWN;
```

So a recognised name still gets command-map aliasing and disabling; anything unrecognised is framed
verbatim. `RedisCommand` stays internal, and those overloads stay internal alongside it. Behaviour is
consistent with the existing ad-hoc command path rather than a second set of rules.

---

### 9.2 Measured against the existing writer

`InterpolatedWriterBenchmarks` compares rendering the same command three ways, formatting only — no
server, no dispatch — all writing into the same pre-allocated `IBufferWriter`:

| Method | What it is | Mean | Allocated | Ratio |
| --- | --- | ---: | ---: | ---: |
| `KeyValue_Message` | `Message.Create` + `WriteTo` — the typed path `db.StringSet` uses | 63.97 ns | 136 B | 1.00 |
| `KeyValue_Adhoc` | `ExecuteMessage` over `object[]` — what `Execute(string, ...)` does | 89.69 ns | 152 B | 1.40 |
| `KeyValue_Interpolated` | this | **41.72 ns** | **0 B** | **0.65** |
| `Expiry_Message` | four arguments, typed path | 90.91 ns | 168 B | 1.00 |
| `Expiry_Interpolated` | four arguments, this | **69.83 ns** | **0 B** | **0.77** |

So roughly **a third faster than the typed path and twice as fast as the ad-hoc string path**, with no
managed allocation where both existing paths allocate 136-168 bytes per command. The ad-hoc row is the
relevant comparison for the public string overload (§9.1), since that is what it competes with.

Two honest qualifications:

- **The comparison is conservative on time.** The interpolated path renders into a rented buffer and then
  copies into the target; `Message` writes straight through. Removing that copy would widen the gap.
- **The 0 B will not survive dispatch.** `Message` allocates partly because it is *retained* for the
  response. A dispatching implementation needs per-command state too, so the end-to-end delta will not
  stay 136 B → 0. What the zero does establish is that *formatting itself* need not allocate, which is
  the half this replaces.

---

### 9.3 The generator, the analyzer and the fixer

Built, so the authoring story is no longer hand-waved:

| | |
| --- | --- |
| `RespFragmentGenerator` | implements `[Resp]` partial properties — framing, length prefixes, casing and `ArgCount` by construction |
| `RespInterpolationAnalyzer` | `SER309`: literal text in a RESP command is discarded, not sent. **Error** |
| `RespLiteralCodeFixProvider` | rewrites `$"{key} nx"` to `$"{key} {RespLiterals.Nx}"` |
| `RespFragment.CreateValidated` | the sanctioned runtime route: checks framing and the argument count |
| `SER351` | a `[Resp]` declaration the generator cannot implement, rather than skipping it in silence |

The fragment tests now declare only the properties; the generator supplies the bodies, and the exact-frame
assertions pass unchanged — which is the real check, since it means `EX` was inferred and upper-cased,
`lib-name` came through verbatim, and `SETINFO lib-name` counted as two arguments.

The emitted file suppresses `SER010` and `SER011` at source and nowhere wider, which is the pattern §9.1
describes: the generator is the sanctioned construction site.

`CreateValidated` is the precondition §9.1 names for ever making hand-construction impossible: it walks the
bytes, checks every `$len\r\n…\r\n` and that the count matches, and throws otherwise. Not gated, because the
check is the point — the cost is irrelevant when it runs once at startup, and it is the difference between a
mistake that throws at the call and one that desyncs the connection somewhere unrelated. Eight malformed
shapes are covered by tests, each of which would otherwise have corrupted the stream.

**The `#error` half is deliberately NOT built.** The generator could detect a hand-written
`new RespFragment(...)` and emit `#error`, and §9.1 records how — but that forecloses the escape hatch, so
`CreateValidated` had to exist first. Whether to take the next step is a judgement about how hostile to be,
which is worth making deliberately rather than as a side effect of me being on a roll.

**Both fixes exist.** When a matching declaration is in source, use it; when none is, declare it in the type
containing the call site. That is not an obviously right home, but it is the only one that needs no guessing,
and moving it afterwards is trivial — so the strict form costs a keystroke rather than a lookup. The fix adds
`partial` to the host type when it is missing, and includes the attribute argument only when inference would
not reproduce the token (`nx` needs none; `lib-ver` does).

Word boundaries can only come from separators, so `withsave` becomes `Withsave`, not `WithSave`. Guessing
where words divide would need a dictionary and would be wrong often enough to be worse.

A run of several tokens is still left alone, having no single answer.

#### `using static` closes most of the remaining gap

`using static` imports the fragments, so the declared form reads very close to the inline one it replaces:

```csharp
using static RespLiterals;
...
ctx.Execute(RedisCommand.SET, $"{key} {value} {Nx} {Ex} {300}");   // vs. "... nx ex 300"
```

Verified. The difference is braces and a capital letter — which is a much weaker case for ever supporting
inline tokens than it looked when §2.1 weighed it.

Notes from building it, in case they bite again:

- The analyzer project targets `netstandard2.0` against Roslyn 4.3, so: no records (no `IsExternalInit`),
  and `LanguageVersion.CSharp11` has to come from the existing `LanguageVersions` shim.
- Detection is by **converted type** on the interpolated string rather than
  `OperationKind.InterpolatedStringHandlerCreation`, which keeps it working against that Roslyn floor.
- `ToMinimalDisplayString` on a *property* includes its type, yielding `RespFragment RespLiterals.Nx`; build
  the name from the containing type instead.
- The code-fix test harness runs analyzers, not generators, so its sources spell out both halves — and a fix
  that *declares* a fragment leaves the fixed code legitimately reporting `CS9248`, which needed a verifier
  overload carrying fixed-state diagnostics. Note `StateInheritanceMode.Explicit` is the wrong tool there: it
  drops the inherited references too, and the fixed state stops seeing the library at all.
- **Generator diagnostics have no test harness here.** `SER350` never had one either; the project references
  `Analyzer.Testing` and `CodeFix.Testing` but not `SourceGenerators.Testing`. `SER351` was verified by
  compiling a deliberately-bad declaration and reading the output, which is weaker than the other rules'
  coverage and is worth closing if more generator diagnostics arrive.

---

### 9.4 The context as the extension point

The intended shape is a context (`db`, key prefix, cancellation, executor) reached from `IDatabase`/
`IServer` via one new member, with the command surface hanging off it as **extension members**:

```csharp
ctx.Strings.Set(key, value)   =>   ctx.Execute(RedisCommands.Set, $"{key}{value}")
```

Three reasons, in the order different audiences feel them.

**1. Discoverability.** `IDatabase` is a flat surface of several hundred methods; IntelliSense on `db.` is
not a navigable list, it is a wall. `ctx.Strings.`, `ctx.Hashes.`, `ctx.Sets.`, `ctx.Streams.` groups the
surface the way Redis documents itself — by data type — so the shape of the API teaches it. This is the
benefit an ordinary caller notices first, and on its own it would probably justify the change.

**2. Module libraries become first class.** NRedisStack today reaches the server through
`db.Execute("FT.SEARCH", …)` or a parallel set of its own interfaces. Extension members over a shared
context mean `ctx.Search.Query(...)` composes exactly like `ctx.Strings.Set(...)` — same cancellation,
same key prefix, same cache participation, no wrapper interface and no forked surface. Module commands
also then arrive through the same `Send`, so they inherit the §6.9 cacheability gates automatically
instead of needing a parallel opt-out story.

**3. It is the last break.** Adding the member is a breaking change, and adding to `IDatabase` has been
standard practice here, so the cost is familiar rather than novel. The difference is that this one ends
the sequence: once a context exists, every subsequent addition is an extension member and breaks nobody.
The one break buys the end of breaks.

**That guarantee rests on a discipline, not on the type system.** The first "just this once" method added
to `IDatabase` after the context exists spends the break for nothing. Worth writing down, and eventually
worth an analyzer — this repo already gates hand-built fragments behind `SER011` on the same reasoning,
that the blast radius is not the author's own code.

#### What the prototype found

Built as `IRespTarget` + `RespStrings` + `RespSurface` (`RespSurfaceTests`), with a fake executor
underneath. `target.Strings.Set(key, value)` and `.Get(key)` work end to end, through the cache, with
`WithKeyPrefix` as a context clone and no per-method forwarding.

- **Extension members compile on every target**, `net461` and `netstandard2.0` included. They are compiler
  lowering, like the interpolated handler itself, so the down-level story that made §1 work holds here too.
  This was the main risk and it is gone.
- **Each extension member costs TWO `PublicAPI` entries** — the `extension(...)` form *and* the lowered
  static (`RespSurface.get_Strings(IRespTarget)`). So "extension members are free to add" is true for
  source and binary compatibility, but not for API tracking: the surface still grows, and the lowered
  names are part of it. Worth knowing before the surface is hundreds of commands.
- **A plain wrapper is enough.** `RespStrings` holds one `RespContext` field, which makes it
  layout-identical by construction — the wrapper *is* the pun, enforced by the compiler, with no `Unsafe`
  and no `ref readonly`. Both entry points work: `target.Strings` and `context.Strings`.
- **`Context` returning by value costs nothing visible** and keeps every command `async`-usable, settling
  item (1) below.
- **A missing executor throws rather than silently doing nothing** — worth pinning early, because a
  `default(RespContext)` is valid by design (§3.5) and would otherwise render a frame and drop it.

**And one bug the prototype exposed.** `RefusedByFlags` was unreachable in real use: the orchestration
skips the cache entirely when flags forbid caching — it does not probe and then decline — so
`TryBeginFill` was never reached and never counted. A diagnostic that reads zero because nothing asks it
looks like evidence, which is worse than not having it. The flag decision now goes through
`cache.PermitsCaching(flags)`, so the cache observes every refusal without probing anything it has been
told to leave alone.

#### Plugged in

`IRedis` now inherits `IRespTarget`, so `IDatabase`, `IServer` and `ISubscriber` all carry `.Context` from
**one** interface edit. The blast radius inside the library was four types, which is smaller than it
sounds:

| | |
| --- | --- |
| `RedisBase` | throws - covers `RedisDatabase`, `RedisServer`, `RedisSubscriber` |
| `MultiGroupDatabase`, `MultiGroupSubscriber` | throw |
| `KeyPrefixedDatabase` | **implemented**: `Inner.Context.WithKeyPrefix(Prefix)` |
| `RespDatabase` (new) | the minimal one that actually works |

`KeyPrefixedDatabase` is worth calling out: that single line is the whole write half of what the class
otherwise does by forwarding ~2600 lines of overrides. It throws today only because its inner target does.

**`IRespExecutor` is now internal.** Dispatch is an implementation concern; the public surface is the
context plus extension members. That keeps the executor chain - retry, and whatever follows - reshapeable
without it being a breaking change, and it is why `RespContext.Executor` and `WithExecutor` are internal
too.

**`RespDatabase` has no command methods**, which is the point rather than an omission: `Set`, `Get` and
everything after are extension members over the context, so the type does not grow as the surface does.

Connection-backed types throw for now. Wiring a rendered frame through the existing message pipeline is
separate work, and nothing here needs to wait for it.

#### Four things to settle before building it

1. ~~**`ref readonly` and `async` do not mix.**~~ **Settled: by value.** A `ref readonly` local cannot cross an `await`, and the
   command surface is `ValueTask`-first — so the context is copied into the state machine anyway and the
   `ref` buys nothing on the only path that matters. At roughly four registers, copy it: return by value,
   take `in` on parameters. And keep `RespContext` a `readonly struct`; making it a `ref struct` to "make
   it cheap" would make it unusable in the very methods it exists for.

2. ~~**`RespRequest` must carry its own metadata first.**~~ **Done.** `Detach(flags)` and
   `AsLookupKey(flags)` now carry the key marks, slot, argument count and flags, and the request exposes
   `KeyCount`/`TryGetKeys`/`GetKey`/`Slot`/`ArgCount`/`Flags`. The mark-resolution logic moved to statics
   on `RespFrame` so both types answer identically rather than by duplicated code.

   Note the division of labour this exposes: **routing needs the slot and nothing else** — one `int`,
   already folded during the write and gated on `ServerType == Cluster`, since CRC16 over every key is the
   expensive part. Only *caching* needs the key marks, which are a few field writes and so are not gated.
   The cheap thing is unconditional, the expensive thing is conditional; that asymmetry is deliberate.

   The fold still covers **every** key rather than just the first, because it is not only producing a
   routing value: it detects cross-slot, which is a correctness check in cluster. First-key-only would
   yield a plausible slot for a command that must be rejected outright.

   Identity deliberately ignores all of it: `Equals`/`GetHashCode` remain the rendered bytes alone, so two
   callers issuing the same command with different `CommandFlags` share a cache entry. Pinned by a test.

3. ~~**A retry executor needs the flags.**~~ **Done, with (2)** — `RespRequest.Flags` carries the retry
   category. Retry otherwise fits well: it must hold the preformed payload across attempts, which is
   exactly what `RespRequest.TryRetain` is for.

4. **Decorator order is silent and load-bearing.** `cache(retry(raw))`: a hit must not traverse retry
   logic, and a retry must not re-probe a cache it already missed. Nothing in the type system says so, so
   it wants a test.

#### Why the cache is not an executor decorator

Tempting, because `Send`'s `cache` parameter and `RespContext.Cache` would both vanish. Two reasons not to:

- **The executor contract deals in OWNED requests** — it has to, because a backlog or resend may need the
  bytes past the call, which is what the reference count is for. A cache decorator therefore receives an
  already-detached request, so **every call pays for ownership, including hits** — the 48 bytes that
  `AsLookupKey` exists to avoid, and the zero-allocation hit with it. Lazy upgrade does not rescue it: a
  borrowed request points at the frame's pooled array, and minting a lease from it would give two owners
  that both return it to the pool.
- **It would pin the executor to returning raw bytes forever.** The cache stores blobs, so a caching
  decorator in the chain forecloses any later move toward executors that return processed results.

So the layering is deliberate: the **frame level** decides whether to form and send at all — probing,
generation capture, ownership transfer — and the **executor chain** operates on a formed, owned request.
Retry belongs in the chain because it resends the same bytes; caching belongs above it because it decides
whether bytes are needed.

#### Services, not fields

The orchestration takes **`in RespContext`** rather than a cache and a cancellation token. A cache is then
just a service the context happens to carry, and `WithCache` is sugar over `WithServices`.

The slot follows `RespReader`'s: one `object?` that either *is* the requested service — the common case, a
type test — or is an `IServiceProvider` for things the context knows nothing about. That buys
**extensibility with no new fields**, so a capability arriving later costs no API change and no growth in
the struct. Given the whole point of §9.4 is to stop adding members, adding a member per capability would
have been a poor start.

The executor stays a real field: it is required on every call, where the cache is optional.

**Cache and retry are still not an either/or between "executor" and "context".** A retry decorator *is* an
executor; installing it is a `With` on the context. Behaviour composes in the chain, configuration on the
context.

**`GetDatabase()` becomes the secondary API.** Long term the primary entry point returns the new root
interface rather than `IDatabase`; for now it can simply be `NewThing() => GetDatabase()`, since
`IDatabase` implements it. That keeps the transition a rename rather than a fork, and means the "last
break" is spent once at the interface rather than again at the entry point.

**Keep `IRespExecutor.Send` (the synchronous member).** Driving sync as
`AsTask().GetAwaiter().GetResult()` blocks a pool thread for a whole round trip, which today's sync path
deliberately avoids and which a large constituency depends on. Fine for a spike; but keeping the sync
member means a real sync path can arrive later without reshaping the API, and it costs nothing now.

### 9.5 Layering: why this stays in SE.Redis for now

**Decision: it stays. Not moving `RespCommandHandler` to RESPite.**

The prize would be real — RESPite already owns `RespReader`, and a matching writer would let anything
build RESP commands with pooled buffers, key marks and slot folding, with no Redis semantics attached.
The route looked available too: give the writer an `AppendKey(ReadOnlySpan<byte>)` primitive, let
`RedisKey` reach it through `IRespArgument`, and key reporting flows down a layer while `RedisKey` stays
up here.

**What stops it is the command, not the key.** A `RedisCommand` hole can only ever be served by an
instance member of the handler type (§2.2 — extension lookup never runs for the handler pattern, and the
CS1503 case proves it), and `RedisCommand` is an `enum`, so `IRespArgument` is closed to it as well.
Whatever assembly declares the handler must therefore know about `RedisCommand`. `RespCommand` does not
rescue it either: it *holds* a `RedisCommand`, and resolves through `RespContext.ResolveCommand`, i.e.
`CommandMap` — renames, disabled commands, per-server-type maps. That is policy, not protocol.

A key is *bytes*; a command is *a lookup*. Bytes hand down a layer cleanly. A lookup drags its policy
with it.

**The shape that would work, when it is worth doing:** split the type, do not relocate it. RESPite owns a
`RespWriter` — buffer rental, bulk framing, the `*N` back-fill, argument counters, key marks, slot folding
— exposing primitives only (`AppendBulk`, `AppendKey(prefix, body)`, a pre-framed form, `Complete`).
SE.Redis keeps `RespCommandHandler` as the `[InterpolatedStringHandler]`, holding a `RespWriter` **by
value** and owning the whole hole vocabulary. Verified to compile and run: a `ref struct` may contain
another `ref struct` by value, and the outer type's `AppendFormatted` members bind normally while
delegating the writing inward. (CS9050 bars a ref *field* to a ref struct; by-value containment is fine.)

Two things move with it whenever that happens:

- `AppendKey` needs a **two-span** form, `(prefix, body)`. Today the context prefix and any prefix the key
  already carries from a `KeyPrefixed*` decorator are written straight into the frame rather than
  concatenated, specifically to avoid the allocation `RedisKey.WithPrefix` would cost.
- `FoldSlot` calls `ServerSelectionStrategy.GetClusterSlot` — CRC16 over the key bytes. Standard Redis
  Cluster, so it belongs in the lower layer anyway.

**Why not now:** it is a pure refactor with no behavioural change, across a spike that is still growing;
moving files today churns everything in flight for nothing. Revisit when the surface stops moving, or
when something outside this repo actually wants to write RESP commands — whichever comes first.

**Rejected along the way:** "never put a command in a hole, always `Compose(RedisCommand.SET, $"...")`".
That form is already preferred (§6.5 — resolution happens before the buffer is rented, so a disabled
command drops nothing on the floor), but it does not rescue the move: `COMMAND INFO <name>`,
`COMMAND DOCS` and `ACL` rules need a command *as an argument*, which is a hole by definition.


## 10. Open questions

- **Should a `RedisChannel` fold into the same slot as keys?** The spike folds it unconditionally, which
  suits sharded pub/sub (`SPUBLISH`) but is meaningless for plain `PUBLISH`, where the channel does not
  route by slot. `RedisChannel` carries a `KeyRouted` option (`Subscription.cs:83`) that presumably ought
  to gate it, and sharing one `_slot` field between keys and channels conflates two different things.
  Visible in the worked example as `ChannelPrefix` reporting `slot=5631` for a plain `PUBLISH`.
- **`Raw` multi-arg and the bit cursor.** A fragment with `ArgCount > 1` advances `_argIndex` by its arg
  count, which keeps the key-mark bitmap aligned — but `Raw` still cannot itself contain a key. Either
  forbid that (rule 5) or have `Raw` carry its own bitmap to shift and OR in.
- **Single-arg vs multi-arg `Raw`.** Restricting `Raw` to exactly one bulk string keeps `*N` a
  compile-time constant; allowing multi-arg costs runtime counting. Possibly two types.
- **A runtime-validating `Raw` factory** for fragments assembled once at startup from config — the one
  legitimate case the literal-only rule closes off.
- **Public API commitment.** A public method taking the handler forces the handler type public, putting
  every `AppendFormatted` overload into `PublicAPI.Unshipped.txt` permanently. Can the interpolated
  surface start internal (RESPite-side, used by SE.Redis) to buy room to iterate? §9.4 argues the opposite
  direction for the *command* surface - one member on `IDatabase`, everything else extension members -
  so these want reconciling.
- **Should the handler be a `ref struct`?** It holds only a `byte[]`. Ref struct prevents capture,
  copying and double-dispose, which is why it is right — but it also blocks `using var` + `ref` (§4)
  and any async retention.
Added while building the cache (§6.6-6.9):

- **Command metadata for cacheability.** Read-only and keyed, so the flag gates pass them today:
  non-deterministic (`SRANDMEMBER`, `HRANDFIELD`, `ZRANDMEMBER`), cursor-based
  (`SCAN`/`HSCAN`/`SSCAN`/`ZSCAN`), time-dependent (`TTL`, `PTTL`), side-effecting (`TOUCH`, `PFCOUNT`).
  Wants a per-command fact beside the retry category in `CommandFlags.Category.cs`, *not* a `CommandFlags`
  bit — see §6.9. `DUMP` was also proposed; I would challenge it, since it looks correctly invalidated, so
  that is a benefit call rather than a safety one.
- ~~**Scripts (`EVAL_RO`/`EVALSHA_RO`) are unresolved.**~~ **Settled:** cacheable by default, caller opts
  out with `NoClientCache`. `EVAL`/`EVALSHA` are excluded by default, `_RO` is server-enforced,
  so the default applies to a narrow, self-declared population. The risk to document is *undeclared key
  access*, not non-determinism — see §6.9.
- **Do module reads register for invalidation?** If the server tracks keys only for core command
  dispatch, a keyed module read would be cached and never invalidated. Unresolved by the docs and worth
  five minutes against a real server with a module loaded; it decides whether §6.9's opt-out story needs
  a caveat for module authors.
- **Nothing turns tracking on.** There is no `CLIENT TRACKING` support, and the RESP3 `invalidate` push is
  actively dropped — `PushKind` has no member for it, *and* `OnOutOfBand` requires the second element to
  be an inline string, which an invalidate push's key array is not. Both need changing. RESP2 `REDIRECT`
  already delivers invalidations via pub/sub today (`Issue2507`).
- **Replies are copied into the cache.** `RespPayload.Create` copies; the real executor should share the
  reply frame's own lease via a reservation, as `RespResult` already does.
- **Running a `ResultProcessor` over a cached payload.** It takes `ref RespReader`, which
  `RespPayload.GetReader()` supplies, but also wants a `PhysicalConnection` and `Message` for error
  context — so it needs a synthetic context or a narrower interface (§6.8).
- **Bounding the cache.** Invalidated entries linger until `Sweep`, and the key table grows with distinct
  keys seen. Both need a size bound; both fail closed, so bounding is safe (§6.6).

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
