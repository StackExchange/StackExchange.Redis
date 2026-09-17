# OpenTelemetry: where things stand

Research notes, September 2026. Background reading for [`plan.md`](plan.md); this file is the
evidence, that one is the decision.

The conversation this came out of is [#1044](https://github.com/StackExchange/StackExchange.Redis/issues/1044)
("Allow to register global profiler", open since 2019, revived by @martincostello on 2026-09-17).

## 1. What the OpenTelemetry bridge does today, and what it costs

`OpenTelemetry.Instrumentation.StackExchangeRedis` is a community package in
[opentelemetry-dotnet-contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.StackExchangeRedis),
the OpenTelemetry .NET SIG's repository for instrumentation of libraries outside the core
distribution. **These notes call that package "the contrib package" throughout**; where the
*people* are meant rather than the package, they are named as its maintainers.

It has exactly one hook into us: `IConnectionMultiplexer.RegisterProfiler`.

Everything else it does is machinery to reconstruct, after the fact, causality that we already
have in hand at the moment the command completes. From
`StackExchangeRedisConnectionInstrumentation.cs`:

- a **dedicated background thread per multiplexer**, draining on a timer (`FlushInterval`,
  default 10s);
- a `ConcurrentDictionary<(ActivityTraceId, ActivitySpanId), (Activity, ProfilingSession, Baggage)>`,
  allocating **one `ProfilingSession` per parent span** so that drained commands can be attributed
  back to the right caller;
- `Baggage.Current` captured at session creation and manually restored on the drain thread around
  each `StartActivity`, because the thread doing the draining is not the thread that issued the
  command;
- `ExecutionContext.SuppressFlow()` around `Thread.Start()`, so the drain thread does not
  permanently inherit the baggage of whichever request happened to create the multiplexer;
- spans created **retroactively**, with explicit `startTime` and `SetEndTime`, so they surface up
  to `FlushInterval` late and the sampler sees them out of band;
- commands issued with no ambient `Activity` all funnel into a single shared `defaultSession`.

The last year of their changelog is mostly this machinery being debugged — a `ProfilingSession`
race (contrib #5117), empty `Baggage.Current` on command activities (#4927), draining performance
(#4398), `FlushInterval` validation (#4860), `Enrich` callbacks throwing (#4900), disposal
idempotency (#4905).

None of that is their fault. It is the shape you are forced into when the only available hook is a
session object that you have to poll.

### The reflection, precisely

There is one reflection site: `RedisProfilerEntryToActivityConverter.MessageDataGetter`, and it
only runs when the caller opts into `SetVerboseDatabaseStatements` (default `false`). It:

1. resolves `StackExchange.Redis.Profiling.ProfiledCommand` by name and emits a `DynamicMethod`
   getter for its private `Message` field;
2. uses `PropertyFetcher<string>("CommandAndKey")` against that `Message`;
3. resolves `RedisDatabase+ScriptEvalMessage` and reads its private `script` field, for the
   EVAL/EVALSHA text.

#3211 renamed `ScriptEvalMessage` to `ScriptEvaluateMessage` and reused the old name for the new
pooled-buffer type whose field is `_script`, so (3) silently started returning null in 3.2.0. Their
fix (contrib 1.18.0-beta.3) probes both type names and both field names. The blast radius was
script text on verbose statements only.

[#3119](https://github.com/StackExchange/StackExchange.Redis/issues/3119) ("Telemetry is gone") was
a separate consumer — Datadog's App Service extension — with the same root cause.

**The only thing we do not already expose publicly is that statement string.** Everything else the
converter reads comes off `IProfiledCommand`.

## 2. The .NET state of the art

`System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` (both in
`System.Diagnostics.DiagnosticSource`) *are* the .NET tracing and metrics API. OpenTelemetry .NET
does not define its own; `AddSource(...)` / `AddMeter(...)` attach an `ActivityListener` /
`MeterListener` to what the runtime already provides.

Microsoft's guidance to library authors is explicit
([docs](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)):

> .NET library authors can exclusively rely on APIs in System.Diagnostics.DiagnosticSource, which
> is part of .NET runtime. This ensures that libraries will run in a wide range of .NET apps,
> regardless of the app developer's preferences about which library or vendor to use for
> collecting telemetry.

OpenTelemetry's own
[library guidelines](https://opentelemetry.io/docs/specs/otel/library-guidelines/) agree that an
instrumented library must remain fully usable with no telemetry SDK present.

This settles the "what if library X goes out of fashion" worry. `Activity` predates the
OpenTelemetry specification, is owned by the runtime, and Datadog, Application Insights, Elastic
and New Relic already listen to it. Emitting `Activity`/`Meter` couples us to the BCL, not to a
vendor.

Relevant mechanics:

- `ActivitySource.StartActivity` returns `null` when nothing is listening, and
  `ActivitySource.HasListeners()` is a cheap pre-check.
- `Activity.IsAllDataRequested` says whether any listener intends to read tags — the gate for
  building anything expensive, such as the statement text.
- `StartActivity` **sets `Activity.Current`**. That matters for us; see §5.

### Does it reach our whole TFM base?

We target `net461; netstandard2.0; net472; net8.0; net10.0`. Package assets, checked against
nuget.org:

| SE.Redis TFM | how it would resolve | verdict |
| --- | --- | --- |
| `net10.0` | in-box — `System.Diagnostics.DiagnosticSource.dll` is in the `Microsoft.NETCore.App` ref pack | no `PackageReference` needed |
| `net8.0` | in-box, same | no `PackageReference` needed |
| `net472` | package, `lib/net462` asset | fine |
| `netstandard2.0` | package, `lib/netstandard2.0` asset | fine |
| `net461` | package, falls back to `lib/netstandard2.0` | **don't** — see below |

`System.Diagnostics.DiagnosticSource` dropped its `net461` asset after 6.0.1 — 8.0.x, 9.0.x and
10.0.x ship `net462` as the lowest .NET Framework target. That leaves the `netstandard2.0` asset as
the only candidate for a net461 project, and **net461 consuming netstandard2.0 is a NuGet
resolution rule rather than a support statement.** The
[.NET Standard table](https://learn.microsoft.com/en-us/dotnet/standard/net-standard) does list
.NET Framework 4.6.1 under .NET Standard 2.0, but with this footnote attached:

> The versions listed here represent the rules that NuGet uses to determine whether a given .NET
> Standard library is applicable. While NuGet considers .NET Framework 4.6.1 as supporting .NET
> Standard 1.5 through 2.0, there are several issues with consuming .NET Standard libraries that
> were built for those versions from .NET Framework 4.6.1 projects. For .NET Framework projects
> that need to use such libraries, we recommend that you upgrade the project to target .NET
> Framework 4.7.2 or higher.

So the restore succeeds and the build may well succeed; whether it *works* depends on the
`netstandard.dll` facade and binding redirects being right. 4.7.2 is the floor Microsoft actually
stands behind — which is also why `net472` is in our target list and `net461` is the one that
cannot be made comfortable here.

Two independent reasons to leave net461 out, then: the package has no net461 asset, and the only
fallback is the path Microsoft explicitly advises against.

So: telemetry everywhere except `net461`, which compiles the instrumentation out. That matches the
existing `VECTOR_SAFE` / `UNIX_SOCKET` idiom in `StackExchange.Redis.csproj` and costs net461 users
nothing they have today.

## 3. Semantic conventions

Three documents apply, all in
[open-telemetry/semantic-conventions](https://github.com/open-telemetry/semantic-conventions). The
first two are the ones @martincostello pointed at in
[#1044](https://github.com/StackExchange/StackExchange.Redis/issues/1044#issuecomment-5713538582)
as what the contrib package is implementing:

- [`docs/db/redis.md`](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/db/redis.md)
  — Redis client spans
- [`docs/db/database-spans.md`](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/db/database-spans.md)
  — the general database span conventions the Redis one builds on
- [`docs/db/database-metrics.md`](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/db/database-metrics.md)
  — database client metrics

**Spans.** Span kind `CLIENT`; span name follows the general database convention **except** that
`db.namespace` is deliberately left out of the name, because for Redis it is a bare integer and
reads as noise.

| attribute | requirement level | where we get it |
| --- | --- | --- |
| `db.system.name` = `redis` | required | constant |
| `db.operation.name` | required, un-normalised case | `Message.CommandString` |
| `db.namespace` | conditionally required | `Message.Db` |
| `db.response.status_code` | conditionally required (on failure) | Redis error prefix — **we do not expose this today** |
| `error.type` | conditionally required (on failure) | fault type — **we do not expose this today** |
| `server.address`, `server.port` | recommended / cond. required | `ServerEndPoint.EndPoint` |
| `network.peer.address`, `network.peer.port` | recommended | ditto |
| `db.query.text` | recommended | `Message.CommandAndKey` (+ script) — the reflection target |
| `db.operation.batch.size` | recommended | batch / transaction size — **not exposed today** |

For batches the convention is to prepend `MULTI` or `PIPELINE` to the span name when the
constituent operations share a command.

**Metrics** (`database-metrics.md` above). `db.client.operation.duration` is a histogram in
seconds and is **stable**, with recommended buckets
`[0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10]`; required attribute `db.system.name`, plus
`db.operation.name`, `db.namespace`, `server.address`/`server.port`, `error.type` on failure. The
`db.client.connection.*` family (count, idle.max/min, limit, pending_requests, timeouts,
create_time, wait_time, use_time) is still *development* status.

Note the shape of that: duration is a **histogram**, so it is O(1) memory regardless of
throughput. Metrics are the half of this that can be left on permanently at Stack Overflow scale;
spans are the half that needs sampling.

### The migration tax is real

The contrib package currently declares **three** `ActivitySource`s — one at semconv 1.23.0 emitting
`db.system`/`db.statement`, one at 1.42.0 emitting `db.system.name`/`db.operation.name`/
`db.query.text`, and one emitting both — driven by `EmitOldAttributes`/`EmitNewAttributes`. If we
own the source, we own that kind of migration. The counter-argument is that the new names are now
the stable ones and the dual-emit window is closing; but it is the strongest single argument for
leaving span production to someone whose job is tracking semconv.

## 4. What other Redis clients do

Worth knowing because it tells us (a) whether a client shipping its own telemetry is normal, and
(b) whether there is a cross-language vocabulary we would be breaking by inventing our own.

**go-redis** — closest to the "utopian" end. `redis.Hook` is a first-class public interface on the
client (`DialHook`, `ProcessHook`, `ProcessPipelineHook`), and `redisotel` is an official module in
the go-redis repo itself. Notable choices: span name is `cmd.FullName()`; connection establishment
gets its own `redis.dial` span; a pipeline is **one** span named `redis.pipeline {summary}` with
`db.redis.num_cmd`. Attributes are still old-semconv — `db.statement`, `db.connection_string` —
alongside `server.address`/`server.port`.

**Lettuce (Java)** — the "middle ground" in its purest form. Lettuce defines a vendor-neutral
tracing SPI in the client (`io.lettuce.core.tracing.Tracing`: `getTracerProvider()`,
`initialTraceContextProvider()`, `isEnabled()`, `includeCommandArgsInSpanTags()`,
`createEndpoint(SocketAddress)`), explicitly so that tracing libraries are not a mandatory
dependency, and ships `BraveTracing` and `MicrometerTracing` adapters against it. Configured via
`ClientResources`, with endpoint and span customisers and an include/exclude switch for command
arguments.

**redis-py** — furthest from it. `opentelemetry-instrumentation-redis` monkeypatches
`Redis.execute_command`, `Pipeline.execute`, cluster and asyncio variants via
`wrapt.wrap_function_wrapper`. Carries its own `db.redis.args_length` and
`db.redis.pipeline_length` attributes, and has a semconv opt-in mode for old-vs-new names, same as
.NET.

**Us** — a contrib package reaching through a profiling API with reflection.

Takeaways:

1. A client library shipping or blessing its own telemetry is normal, not novel. go-redis does it
   in-repo; Lettuce does it via an SPI plus adapters.
2. **There is no agreed cross-language vocabulary beyond the
   [semantic conventions](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/db/redis.md)
   themselves**, and adoption of those lags badly and unevenly — go-redis is still on `db.statement`, Python has a
   dual-mode switch, .NET has a triple-source switch. Following current semconv puts us *ahead* of
   the field, not out of step with it.
3. Where the clients *do* informally agree, it is on things semconv does not mandate: span name is
   the bare command name, pipelines/batches collapse to one span with a count attribute, and
   argument capture is opt-in because of PII. We should match those three.
4. Both of the good implementations trace **connection establishment** as well as commands
   (go-redis `redis.dial`; Lettuce via endpoint resolution). We have much richer material there
   than either.

## 5. Where we would hook, and the one non-obvious wrinkle

Existing sites, all of which are already the right ones:

- `ConnectionMultiplexer.cs:~2397` — after server selection, still on the caller's thread. This is
  where `ProfiledCommand.NewWithContext` is attached today.
- `Message.SetProfileStorage` / `Message.CreatedTimestamp` — per-message state and start time.
- `Message.Complete(PhysicalConnection?)` — on the reader thread; already has `currBox.Fault` in
  hand, which is where `error.type` and `db.response.status_code` come from.
- `Message.SetExceptionAndComplete` — the failure path.
- `Message.PrepareToResend` — `-MOVED` / `-ASK` retransmission, which the contrib converter has a
  literal `// TODO: deal with the re-transmission` for.

**The wrinkle:** do not start the `Activity` on the caller's thread. `ActivitySource.StartActivity`
assigns `Activity.Current`, and for a multiplexed client whose command completes on a different
thread there is no scope to dispose — so the caller's async flow would inherit our span and we
would have to save and restore an `AsyncLocal` on the hot path.

Instead:

- **caller thread**, only when `HasListeners()`: capture `Activity.Current?.Context` — an
  `ActivityContext` struct, no `AsyncLocal` write — plus the start timestamp we already record.
- **reader thread, at completion**: `StartActivity(name, ActivityKind.Client, parentContext, tags,
  startTime)` → `SetEndTime` → `Stop()`.

Because the parent is supplied as a *context* rather than a parent `Activity`, the started activity
has no in-process parent, so `Stop()` restores `Activity.Current` to null — which is what the
reader thread had. It cleans up after itself.

This is the same deferred-creation shape the contrib drain thread uses, minus the thread, the
dictionary, the 10s latency and the baggage restoration — because we are already on the completion
path with the caller's context in hand.

## 6. The two asks on the table

From [#1044](https://github.com/StackExchange/StackExchange.Redis/issues/1044#issuecomment-5713538582),
@martincostello:

> In the *ideal* case the OpenTelemetry instrumentation would actually become obsolete because
> it's entirely built-in to the library here. A middle ground where we have the hooks to wrap
> things and produce the telemetry ourselves is also fine (just not the *utopian ideal*).

So, named:

- **Utopian** — we emit `Activity` and `Meter` ourselves. Consumers write
  `AddSource("StackExchange.Redis")`. The contrib package collapses to a one-line convenience
  extension or is retired. #1044 stops existing, because there is nothing to register.
- **Middle ground** — we expose a supported, documented extensibility surface (profiling done
  properly: statement text, failure detail, batch size, retransmission, connection events) and
  contrib keeps producing the spans, against a contract instead of against `DynamicMethod`. This is
  Lettuce's model, and it keeps semconv churn on their side of the line.

They are not exclusive, and the second is a subset of the work for the first: the data that has to
become reachable is the same data either way. What differs is who calls `StartActivity`.

## 7. Prior art for the packaging question

**Npgsql** is the closest analogue and the one to copy. Native `ActivitySource` inside the driver;
a separate, trivial `Npgsql.OpenTelemetry` package whose entire job is an `AddNpgsql()` extension
that calls `AddSource("Npgsql")`. The driver itself has no OpenTelemetry dependency. Their docs
still label the tracing support experimental, tracking the semconv churn above.

That is the template: **the telemetry is in-box; the OpenTelemetry-shaped convenience wrapper is a
separate, near-empty package** — and in our case that package already exists and already has a
maintainer, so the wrapper need not be ours at all.

## 8. What the contrib package emits today — the compatibility baseline

If we intend to replace the package (see `plan.md`), this is the inventory we have to match. Read
off contrib `main` as of 2026-09-17, package version 1.18.0-beta.3.

**Release history:** 48 versions since 0.3.0-beta.1, and **not one stable release**. Their README
attributes this to the database semantic conventions still being Experimental, not to the code
being immature.

**Activity identity**

| | value |
| --- | --- |
| `ActivitySource.Name` | `OpenTelemetry.Instrumentation.StackExchangeRedis` (the assembly name, via `ActivitySourceFactory`) |
| `ActivitySource.Version` | the package version |
| `TelemetrySchemaUrl` | derived from the semconv version — 1.23.0 for the old source, 1.42.0 for the new, unset when emitting both |
| span name | `command.Command`, i.e. the bare command string; falls back to `"{ActivitySource.Name}.Execute"` if empty |
| span kind | `Client` |
| start / end | `command.CommandCreated` and `+ command.ElapsedTime` — explicit, because the span is built after the fact |

**The old/new attribute switch.** `EmitOldAttributes` / `EmitNewAttributes` are *internal*, not
user-facing knobs; they are set in the options constructor from the standard
`OTEL_SEMCONV_STABILITY_OPT_IN` environment variable via OTel's shared
`DatabaseSemanticConventionHelper`:

- `database/dup` → both
- `database` → new only
- **anything else, including unset → `Old`**

That default is the important part. **Unless a user has opted in, what they are seeing in
production today is the 1.23-era names.**

| mode | attributes emitted |
| --- | --- |
| Old (default) | `db.system` = `redis`; `db.statement`; `db.redis.database_index` |
| New | `db.system.name` = `redis`; `db.operation.name`; `db.namespace`; `db.query.text` |
| Dupe | both sets |

Note `db.redis.database_index` — contrib's own invention, not in any semantic convention, and
present only in old mode.

**Always emitted, in every mode**, from `command.EndPoint`:

- `IPEndPoint` → `server.address`, `server.port`, `network.peer.address`, `network.peer.port`
- `DnsEndPoint` → `server.address`, `server.port`
- `UnixDomainSocketEndPoint` (net only) → `server.address`, `network.peer.address`

**Activity events** — `EnrichActivityWithTimingEvents`, default **`true`**: `Enqueued`, `Sent`,
`ResponseReceived`, timestamped by accumulating `CreationToEnqueued`, `EnqueuedToSending`,
`SentToResponse` onto `CommandCreated`.

This matters for implementation, not just for parity: **it consumes all five profiling timestamps**,
so any replacement that captures only start-and-complete silently drops data that people have
today.

**`db.statement` / `db.query.text` content** — the command string by default; with
`SetVerboseDatabaseStatements` (default `false`), `CommandAndKey` plus, for EVAL/EVALSHA, a space
and the script text. This is the reflection payload from §1.

**Not emitted, at all:**

- any error or failure information — no `error.type`, no `db.response.status_code`, and
  `ActivityStatusCode` is never set. A Redis command that fails produces a span indistinguishable
  from one that succeeded.
- `db.operation.batch.size`
- anything about retransmission (`// TODO: deal with the re-transmission`)
- metrics of any kind — the package is traces only

**User-facing options:** `FlushInterval` (default 10s), `SetVerboseDatabaseStatements` (false),
`EnrichActivityWithTimingEvents` (true), `Filter`, `Enrich`.

**Licensing:** the contrib repository is Apache-2.0 (SPDX headers on every file); this repo is MIT. Code owner is
@matt-hensley.

## 9. Npgsql's public surface, in full

Since Npgsql is the model (§7), it is worth being exact about what "native telemetry" cost them in
public API. Read off `npgsql/main`; they use the same `PublicApiAnalyzers` we do, so this is their
own `PublicAPI.Shipped.txt`, not a reading of the source.

**In the core `Npgsql` package** — 14 API lines, all of them knobs:

```text
Npgsql.NpgsqlTracingOptionsBuilder
Npgsql.NpgsqlTracingOptionsBuilder.ConfigureCommandFilter(System.Func<Npgsql.NpgsqlCommand!, bool>? commandFilter) -> Npgsql.NpgsqlTracingOptionsBuilder!
Npgsql.NpgsqlTracingOptionsBuilder.ConfigureCommandEnrichmentCallback(System.Action<System.Diagnostics.Activity!, Npgsql.NpgsqlCommand!>? commandEnrichmentCallback) -> Npgsql.NpgsqlTracingOptionsBuilder!
Npgsql.NpgsqlTracingOptionsBuilder.ConfigureCommandSpanNameProvider(System.Func<Npgsql.NpgsqlCommand!, string?>? commandSpanNameProvider) -> Npgsql.NpgsqlTracingOptionsBuilder!
   ... the same three again for Batch, and again for CopyOperation ...
Npgsql.NpgsqlTracingOptionsBuilder.EnableFirstResponseEvent(bool enable = true) -> Npgsql.NpgsqlTracingOptionsBuilder!
Npgsql.NpgsqlTracingOptionsBuilder.EnablePhysicalOpenTracing(bool enable = true) -> Npgsql.NpgsqlTracingOptionsBuilder!
Npgsql.NpgsqlDataSourceBuilder.ConfigureTracing(System.Action<Npgsql.NpgsqlTracingOptionsBuilder!>! configureAction) -> Npgsql.NpgsqlDataSourceBuilder!
Npgsql.NpgsqlSlimDataSourceBuilder.ConfigureTracing(...) -> Npgsql.NpgsqlSlimDataSourceBuilder!
Npgsql.NpgsqlMetricsOptions
Npgsql.NpgsqlMetricsOptions.NpgsqlMetricsOptions() -> void
```

What is *not* there: `NpgsqlActivitySource` is **internal**. No public source-name constant, no
public meter-name constant, no public telemetry types beyond the options. `NpgsqlMetricsOptions` is
a bare class with a default constructor — a placeholder so the metrics extension has something to
take.

**All the instrumentation lives in the core assembly.** There is no IVT-ed sink library.

**The `Npgsql.OpenTelemetry` package is four lines of code**, in two files:

```csharp
public static TracerProviderBuilder AddNpgsql(this TracerProviderBuilder builder)
    => builder.AddSource("Npgsql");

public static MeterProviderBuilder AddNpgsqlInstrumentation(
    this MeterProviderBuilder builder, Action<NpgsqlMetricsOptions>? options = null)
    => builder.AddMeter("Npgsql");
```

That is the entire package. Note the `options` parameter on the metrics one is accepted and
ignored. The package exists for exactly one reason: those two extension methods need types from
`OpenTelemetry`, and the core driver must not depend on `OpenTelemetry`.

Two further observations worth carrying into our own design:

- **They shipped `Filter` and `Enrich` in-box after all** — filters, enrichment callbacks *and*
  span-name providers, for each of commands, batches and copy operations. That is direct
  counter-evidence to the position that `ActivityListener.Sample` makes in-box filtering
  unnecessary. Someone who has run this in production for years concluded otherwise.
- **`EnablePhysicalOpenTracing` and `EnableFirstResponseEvent` are opt-in, defaulting off** —
  connection-establishment spans and extra timing events are not free enough to be on by default.
  Contrib, by contrast, has its timing events **on** by default, which is a compatibility
  constraint for us (§8) and not an endorsement.
