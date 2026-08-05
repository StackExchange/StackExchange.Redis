# Analyzer rules

StackExchange.Redis ships a Roslyn analyzer inside the package, so these rules are reported in your own build
with no extra reference. Each diagnostic links here from its message.

The `SER3xx` range belongs to this analyzer, and is split so that the two kinds of report can be configured
separately:

| Range | Meaning |
|---|---|
| `SER300`-`SER349` | usage guidance about your code (reported as *information*; never fails a build) |
| `SER350`-`SER399` | build-level problems from the source generators |

Note that `SER0xx` is a different thing entirely: those are the [`[Experimental]` API gates](../exp/SER004),
which mean "this API is preview", not "consider changing this code".

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

## Why these are only information

The code these rules flag is correct - it works, and it will keep working. They point at a form that is a single
round-trip instead of two and cannot abort under contention. Shipping them as warnings would break every
consumer building with `TreatWarningsAsErrors`, so they are informational by default; raise the severity in
`.editorconfig` if you want them enforced:

```ini
dotnet_diagnostic.SER300.severity = warning
```
