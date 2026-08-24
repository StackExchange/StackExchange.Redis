# Analyzer rules

StackExchange.Redis ships a Roslyn analyzer inside the package, so these rules are reported in your own build
with no extra reference. Each diagnostic links here from its message.

The `SER3xx` range belongs to this analyzer, and is split so that the two kinds of report can be configured
separately:

| Range | Meaning |
|---|---|
| `SER300`-`SER349` | rules about your code - mostly guidance, but see the correctness rules below |
| `SER350`-`SER399` | build-level problems from the source generators |

Note that `SER0xx` is a different thing entirely: those are the [`[Experimental]` API gates](../exp/SER004),
which mean "this API is preview", not "consider changing this code".

## Reading the suggestions

Messages name the replacement as `StringSet[Async](...)`, following the convention used elsewhere in these docs:
there is a `StringSet` and a `StringSetAsync`, and you want whichever matches the code around it. The `[Async]`
is not something to type.

Which one that is depends on how you were finishing the transaction, not on the call being replaced - commands
queued on an `ITransaction` are always the `...Async` ones, because that is the only surface it offers. If you
were writing `await tran.ExecuteAsync()`, you want `StringSetAsync`; if you were writing `tran.Execute()`, you
want `StringSet`. Reach for the async form in new code.

Argument names in the suggestion (`key`, `value`, `entries`) are a sketch of the shape, not literal text -
substitute your own expressions.

## Correctness

Unlike everything under [Usage](#usage), these describe code that does not do what it looks like it does.

- [SER305](SER305) - **error**: waiting for a queued command before `Execute[Async]` never completes
- [SER306](SER306) - waiting for a fire-and-forget result, which is always the default value
- [SER307](../SyncOverAsync) - blocking on a redis call instead of awaiting it ("sync over async")
- [SER308](../SyncOverAsync) - the same, via the library's own `Wait`/`WaitAll`/`TryWait` helpers

## Usage

- [SER300](SER300) - transaction may be replaceable by a conditional argument (any server version)
- [SER301](SER301) - transaction may be replaceable by a single atomic operation (needs a newer server)
- [SER302](SER302) - condition may be redundant; the command already reports whether it acted (any server version)
- [SER303](SER303) - two queued operations may be a single compound command (varies by pair)
- [SER304](SER304) - the same operation queued repeatedly may suit the variadic overload (mostly any server)

## Build

- [SER350](SER350) - language version too low for generated code

## When these rules stay quiet

Every rule here is deliberately conservative. It ships to every consumer of the package, and a wrong suggestion
on correct code is worse than no suggestion at all, so the following apply across the whole family - on top of
whatever each rule's own page lists.

- **Anything else queued on the same transaction.** These rules describe a whole transaction, not a fragment of
  one. A third queued command means the transaction is doing more than the rule accounts for - and that includes
  a raw `tran.ExecuteAsync("SOMECMD", ...)` for something the library has no wrapper for.
- **Commands that do not always queue together.** A command inside an `if`, `switch` or `try` is only collapsible
  with commands inside the *same* one; opposite arms of an `if`/`else` never queue together at all. A whole
  transaction inside a conditional is ordinary code and is still flagged.
- **Commands that might queue more than once**: inside a loop, or inside a lambda or local function that queues
  onto a transaction from outside itself, where one call site is any number of queued commands. A transaction
  created *and* completed within the lambda or local function is one per invocation, so it is still flagged.
- **Arguments the single command cannot express.** The suggestions are sketches, but only ever of a rewrite that
  keeps what you wrote. N x `StringSet(key, value, expiry)` is *not* `MSET` - MSET takes one expiry for the whole
  batch, not one per key - so that stays quiet rather than quietly making your keys permanent. Likewise a `When`
  on a command whose variadic form has none, an `ExpireWhen` where GETEX has no NX/XX, and your own `when:`
  argument where the suggestion *is* a `when:` argument. `CommandFlags` is the exception: it is on everything, no
  suggestion mentions it, and you carry it over verbatim.
- **A transaction passed to another method, stored in a field, or otherwise captured** - what it queues elsewhere
  is not visible from here.
- **A key or member local reassigned anywhere in the method.** Keys are compared as source text, which is only
  sound while the locals hold the same value throughout.

These are heuristics, and the list above is where the effort has gone - but it is meant to make a false positive
rare, not impossible. If one of these rules flags something it should not have, that is a bug in the rule rather
than something to work around: please
[report it](https://github.com/StackExchange/StackExchange.Redis/issues/new) with the transaction as written.

Four rules are not in this family, because they are not suggesting an improvement to working code: `SER350`
reports a build problem, and [SER305](SER305)/[SER306](SER306)/[SER307](../SyncOverAsync) report code that does
not do what it looks like. Those have their own, much narrower quiet-lists on their own pages - they are flow-insensitive by design, so most of the
caveats above (a loop, an `if`, a transaction passed elsewhere) simply do not arise.

## Declaring your server version

Some suggestions need a recent server, and an analyzer cannot see the server you will connect to. Declare your
floor and you will only be shown suggestions you can act on:

```xml
<PropertyGroup>
  <RedisMinServerVersion>7.4</RedisMinServerVersion>
</PropertyGroup>
```

or, taking precedence, in `.editorconfig` / `.globalconfig`:

```ini
redis.min_server_version = 7.4
```

Unset shows everything, which is the default: a suggestion you cannot use yet is still worth knowing about. Each
rule's message names the version it needs, so you can tell at a glance whether it applies to you.

## Severity, and turning it down

These are **warnings** by default, with one exception: [SER305](SER305) is an **error**, because the code it
flags cannot work rather than merely being improvable. It is the only one here that is not a matter of taste,
and it is worth reading before suppressing.

For the rest, the code they flag is correct - it works, and it will keep working - so a warning is arguably
strong; they are warnings anyway because information-level diagnostics are not printed by `dotnet build`, which
means outside an IDE they are invisible, and a suggestion nobody ever sees is not worth shipping.

The consequence worth knowing before you upgrade: if you build with `TreatWarningsAsErrors`, these **will fail
your build** on code that previously compiled. Nothing is broken - you have a choice of acting on them or
turning them down.

Per rule, in `.editorconfig`:

```ini
dotnet_diagnostic.SER300.severity = suggestion   # or none, silent, warning, error
```

Or for the whole family, in your project file:

```xml
<NoWarn>$(NoWarn);SER300;SER301;SER302;SER303;SER304</NoWarn>
```

Or at a single site, where the transaction is deliberate:

```c#
#pragma warning disable SER301 // deliberate fallback for older servers
```

If you want the old behaviour everywhere, `suggestion` is the severity that matches what these shipped as
before: visible in the IDE, absent from the build log.
