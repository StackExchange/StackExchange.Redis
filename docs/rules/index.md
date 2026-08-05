# Analyzer rules

StackExchange.Redis ships a Roslyn analyzer inside the package, so these rules are reported in your own build
with no extra reference. Each diagnostic links here from its message.

The `SER3xx` range belongs to this analyzer, and is split so that the two kinds of report can be configured
separately:

| Range | Meaning |
|---|---|
| `SER300`-`SER349` | usage guidance about your code |
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

## Usage

- [SER300](SER300) - transaction can be replaced by a conditional argument (any server version)
- [SER301](SER301) - transaction can be replaced by a single atomic operation (needs a newer server)
- [SER302](SER302) - condition is redundant; the command already reports whether it acted (any server version)
- [SER303](SER303) - two queued operations are a single compound command (varies by pair)
- [SER304](SER304) - the same operation queued repeatedly can use the variadic overload (mostly any server)

## Build

- [SER350](SER350) - language version too low for generated code

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

These are **warnings** by default. The code they flag is correct - it works, and it will keep working - so a
warning is arguably strong; they are warnings anyway because information-level diagnostics are not printed by
`dotnet build`, which means outside an IDE they are invisible, and a suggestion nobody ever sees is not worth
shipping.

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
