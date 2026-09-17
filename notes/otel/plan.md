# OpenTelemetry: the plan

Decisions and sequencing for in-box telemetry. The evidence behind these is in
[`findings.md`](findings.md); this file is what we intend to do about it.

Status: **proposed**. Nothing below has shipped.

## The shape

Emit `System.Diagnostics.ActivitySource` spans and `System.Diagnostics.Metrics.Meter` instruments
directly from StackExchange.Redis. No OpenTelemetry dependency, no vendor dependency — these types
are the .NET tracing and metrics API, and every collector (OpenTelemetry, Datadog, Application
Insights, Elastic, New Relic) already listens to them.

Consumer side, in full:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("StackExchange.Redis"))
    .WithMetrics(m => m.AddMeter("StackExchange.Redis"));
```

No registration call, no connection handed to a builder, no `RegisterProfiler`. That is the point:
**[#1044](https://github.com/StackExchange/StackExchange.Redis/issues/1044) — "allow a global
profiler" — stops existing**, because there is nothing left to register. Same for the seven
`AddRedisInstrumentation` overloads in the contrib package and its DI deferral dance.

> Throughout these notes, **"the contrib package"** means
> [`OpenTelemetry.Instrumentation.StackExchangeRedis`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.StackExchangeRedis),
> which lives in `opentelemetry-dotnet-contrib` — the OpenTelemetry .NET SIG's repository for
> instrumenting libraries outside the core distribution. It is the package that instruments us
> today, from the outside, via `RegisterProfiler` and reflection. Where the *people* are meant, they
> are named as its maintainers.

### What is *not* public

No `public static class RedisTelemetry { public const string ActivitySourceName = ... }`. The name
is a stability contract whether or not a `const` points at it, and a constant is just a second
spelling of a documented string plus a public API entry to maintain forever. Neither ASP.NET Core,
HttpClient, EF Core nor Npgsql ships one; their public surface is either a documented string or a
convenience extension in a *separate* package (`Npgsql.OpenTelemetry`'s `AddNpgsql()`).

Ours is a documented string in `docs/`, and the convenience extension — if anyone wants one — can
stay in the contrib package, which already owns the name `AddRedisInstrumentation` and already has
a maintainer.

### Names

Both are `StackExchange.Redis`, versioned with the package version. One source, one meter, until
we have a concrete case for splitting (pub/sub is the likely first candidate — see phase 5).

### Configuration

`ConfigurationOptions`, using the existing `OptionFlags` + `DefaultOptionsProvider` fallback
pattern (`IncludeDetailInExceptions` is the model). Per-multiplexer, not static — the whole
complaint in #1044 is about a hook that is awkward *because* it is per-connection-and-imperative,
not because it is per-connection.

Initial knob, defaulting off:

- `EmitQueryText` — whether to populate `db.query.text` / `db.statement`. Off by default because
  keys can carry PII. Command **and key**; never argument values. Matches contrib's
  `SetVerboseDatabaseStatements` and Lettuce's `includeCommandArgsInSpanTags`.

## Subsuming the contrib package

The intent is to **replace** `OpenTelemetry.Instrumentation.StackExchangeRedis`, not to sit
alongside it. That needs agreement from its owners — code owner is @matt-hensley, with
@martincostello active on it — and mgravell is raising it with them.

It should not be especially controversial. In 48 releases since 2020 the package has **never
shipped a stable version**; every one is `-beta` or `-rc`. Their README says why, and the reason
matters to us:

> This component is based on the OpenTelemetry semantic conventions for traces. These conventions
> are Experimental, and hence, this package is a pre-release. Until a stable version is released,
> there can be breaking changes.

So the reason it is not GA is semconv instability, not immaturity — which is precisely the thing we
inherit by taking it over. See open question 1.

### The compatibility contract

**Default to matching their output byte-for-byte; deviate only deliberately, and write the
deviation down here.** People have dashboards, alerts and saved queries built on these spans, and
"we rewrote it and your error rate graph went flat" is not an acceptable upgrade story.

Concretely, from findings §8:

- **Honour `OTEL_SEMCONV_STABILITY_OPT_IN`**, with the same three modes and the same default. This
  is the single most important item, and the easiest to get wrong: the default today is `Old`, so
  unopted users are seeing `db.system` and `db.statement`, *not* `db.system.name` and
  `db.query.text`. Emitting only the current names would look like the modern, correct choice and
  would silently break every existing consumer. Honouring the env var is also simply the correct
  behaviour for any .NET library emitting database telemetry, and it gives a clean exit: when the
  conventions go stable, our default flips in lockstep with the rest of the ecosystem rather than
  on our own schedule. Honouring it does not mean it has to be the *only* way to choose — see open
  question 5 on making the selection an explicit enum as well.
- **Keep `db.redis.database_index`** in old mode. It is contrib-specific, not semconv, and it is in
  people's dashboards.
- **Keep the timing events** — `Enqueued`, `Sent`, `ResponseReceived` — and keep them on by
  default, as `EnrichActivityWithTimingEvents` is. This has a design consequence: a
  compatibility-preserving implementation must capture **all five** profiling timestamps when data
  is requested, not just create-and-complete. Cheaper span construction is not worth losing data
  people already have.
- **Keep the span name rule**: the bare command string.
- **Set `TelemetrySchemaUrl`** on the `ActivitySource` from the semconv version, as they do.

### Where we intend to be better

Additive is always fine. Changing the value or meaning of something that already exists is not.

- Failure attribution — `error.type`, `db.response.status_code`, and an actual
  `ActivityStatusCode.Error`. Contrib sets **none** of these; failures are currently invisible.
  This is a deliberate deviation: error rates that were flat will stop being flat. Call it out in
  release notes.
- `-MOVED` / `-ASK` retransmission, which they have a `// TODO` for.
- Batch and transaction shape (`db.operation.batch.size`).
- Connection lifecycle: establishment, failover, sentinel, discovery.
- Metrics at all — they ship traces only.
- Statement text without reflection, and correct for every message type rather than the handful
  the reflection knows about.

### Two things that cannot be preserved

1. **The `ActivitySource` name changes.** Theirs is the assembly name,
   `OpenTelemetry.Instrumentation.StackExchangeRedis`; ours will be `StackExchange.Redis`. We are
   not squatting their name. Anyone calling `AddRedisInstrumentation()` sees nothing — the
   extension just changes which source it adds — but anyone who hand-wrote
   `AddSource("OpenTelemetry.Instrumentation.StackExchangeRedis")` has to edit one string. Document
   it prominently; consider asking its maintainers to add *both* names for a transition window.
2. **`Filter` and `Enrich`.** Callbacks on their options object with no obvious in-box equivalent.
   `ActivityListener.Sample` is the better home for filtering — see open question 4.

### On starting from their implementation

Their code is Apache-2.0; this repo is MIT. Apache-2.0 is permissive and can be incorporated, but
§4 has real obligations (retain notices, state changes, carry the license text for the derived
portions) — so this is a decision to make on purpose, with their blessing, not a quiet copy-paste.

The practical recommendation is to **use it as a specification rather than a source**: most of the
implementation is machinery for the drain thread, the session cache and the baggage restoration,
all of which we are deleting, and the rest is written against internals we will not have. What is
genuinely worth having is (a) the exact attribute/event/default inventory, which is behaviour and
is captured in findings §8, and (b) their **test suite**, which is the compatibility oracle and is
the part where the licensing question actually bites. Ask about the tests explicitly.

## Public API impact

Modelled on Npgsql, whose full telemetry surface is inventoried in findings §9: 14 API lines in the
core package, all knobs, with the `ActivitySource` itself kept internal and no public name
constants. Ours is smaller, because `ConfigurationOptions` + `DefaultOptionsProvider` is a cheaper
place to hang a setting than a data-source builder.

In `PublicAPI.Unshipped.txt` terms, phase by phase:

**Phase 1 — statement text** (2 lines)

```text
StackExchange.Redis.Profiling.ProfilingExtensions
static StackExchange.Redis.Profiling.ProfilingExtensions.GetStatement(this StackExchange.Redis.Profiling.IProfiledCommand! command) -> string?
```

**Phase 2 — metrics** (0 lines)

Nothing. The meter name is a documented string; there is no knob worth having on day one. If we
ever need one, Npgsql's placeholder `NpgsqlMetricsOptions` is the shape — and the fact that theirs
is still an empty class with a default constructor suggests waiting.

**Phase 3 — traces** (3 lines)

```text
StackExchange.Redis.ConfigurationOptions.EmitQueryText.get -> bool
StackExchange.Redis.ConfigurationOptions.EmitQueryText.set -> void
virtual StackExchange.Redis.Configuration.DefaultOptionsProvider.EmitQueryText.get -> bool
```

The connection-string keyword is free: `OptionKeys` is a `private static class` inside
`ConfigurationOptions`, so parsing support adds no public API.

**Deliberately not mirrored onto `IConnectionMultiplexer`.** `IncludeDetailInExceptions` is the
cautionary example — it costs six API lines because it appears on `ConfigurationOptions`,
`ConnectionMultiplexer` *and* `IConnectionMultiplexer`, and that last one means the property can
never be added to without breaking implementers. Telemetry settings are read from `RawConfig` on
the command path; nothing needs them on the multiplexer interface.

**Phase 4 — connection tracing** (3 lines, same shape)

```text
StackExchange.Redis.ConfigurationOptions.EmitConnectionTracing.get -> bool
StackExchange.Redis.ConfigurationOptions.EmitConnectionTracing.set -> void
virtual StackExchange.Redis.Configuration.DefaultOptionsProvider.EmitConnectionTracing.get -> bool
```

Opt-in and defaulting off, following Npgsql's `EnablePhysicalOpenTracing`.

**Running total: eight lines**, no new public types beyond one static extension class, no new
interfaces, no change to any existing interface. That is the whole cost of the in-box option and it
is worth weighing against the alternative below.

**Not in the total, because it is a separate decision:** `Filter` / `Enrich` / span-name-provider
equivalents. Npgsql shipped all three, for commands, batches *and* copy operations — nine methods
plus a builder type, i.e. the bulk of their surface — which is real counter-evidence to the
"`ActivityListener.Sample` is enough" position (open question 4). If we follow them, the surface
roughly triples.

There is a genuine design problem underneath it, which is why it is not sketched here: *what do we
hand the callback?* Npgsql passes the live `NpgsqlCommand`. Our equivalent would be
`IProfiledCommand` — but the whole point of the deferred-creation design is that we do **not**
build a `ProfiledCommand` unless someone registered a profiler. Handing one to a filter means
allocating the thing we were avoiding. Likely answers are a `readonly ref struct` view over the
`Message`, or accepting the allocation only when a callback is configured. Needs deciding before
any of this is sketched as API.

## One assembly, or hooks plus an IVT sink?

Recommendation: **one assembly.** Not strongly held on the packaging, but the technical argument is
fairly one-sided.

1. **The hook sites have to be in core either way.** The data is produced on the reader thread
   inside `Message.Complete`, and the parent context is captured on the caller thread in
   `ConnectionMultiplexer`. A second assembly cannot reach those. It can only be *called* — which
   means an indirection in core (a per-command virtual dispatch, plus a registration mechanism that
   is itself near-public API) — or it can *poll*, which is exactly the `ProfilingSession` design we
   are trying to delete.
2. **`ActivitySource` and `Meter` already are the indirection.** That is the entire point of them
   living in the BCL. A second assembly binding over internals would be re-implementing
   `ActivityListener`, worse and privately.
3. **Cross-package IVT is lockstep with a runtime failure mode.** `StackExchange.Redis` 3.5 plus
   `StackExchange.Redis.Telemetry` 3.2 is a perfectly resolvable NuGet graph that throws
   `MissingMethodException` on first use. That is the same failure class as the reflection this
   whole exercise is meant to retire — moved behind a compiler check, but not removed.
4. **The reference is free where it matters.** `System.Diagnostics.DiagnosticSource` is in-box on
   `net8.0` and `net10.0`; only `net472` and `netstandard2.0` gain an actual package reference, and
   `net461` gains nothing because it compiles out.
5. **Npgsql — the model — put everything in core.** Their separate package holds only the two
   OpenTelemetry-typed extension methods, because *those* need `OpenTelemetry` types and the driver
   must not. It contains no instrumentation: four lines total.

The honest case for the split is narrow: it buys a `netstandard2.0`/`net472` build with no new
package reference, and independent versioning of the telemetry. If we decide that reference is
unacceptable, the split is what buys it — and nothing else.

And on the OpenTelemetry-typed wrapper specifically: Npgsql needed a second package because nobody
else was going to write `AddNpgsql()` for them. **We do not have that problem** — the contrib
package already owns `AddRedisInstrumentation`, already has a maintainer, and would be reduced to
precisely those four lines. That is the conversation to have with them.

## Target frameworks

`System.Diagnostics.DiagnosticSource` reaches all of our targets except `net461` — the package
dropped its `net461` asset after 6.0.1 and now bottoms out at `net462` (see findings §2). So:

| TFM | instrumentation | `PackageReference` |
| --- | --- | --- |
| `net10.0` | yes | none — in-box in `Microsoft.NETCore.App` |
| `net8.0` | yes | none — in-box |
| `net472` | yes | `System.Diagnostics.DiagnosticSource` (`lib/net462`) |
| `netstandard2.0` | yes | `System.Diagnostics.DiagnosticSource` (`lib/netstandard2.0`) |
| `net461` | **compiled out** | none |

Guarded by a `TELEMETRY` compile symbol defined for everything but `net461`, following the existing
`VECTOR_SAFE` / `UNIX_SOCKET` idiom in `StackExchange.Redis.csproj`. Version pinned in
`Directory.Packages.props` like every other reference.

net461 users lose nothing they have today; the existing profiling API stays where it is on every
target.

## Pay-for-play

The cost when nobody is listening must be a predictable, tiny constant:

- one static `ActivitySource.HasListeners()` check per command on the caller thread;
- one reference field on `Message`, null unless telemetry is active;
- nothing allocated, no `AsyncLocal` written, no `Activity` created — `StartActivity` is never
  reached.

When a listener *is* attached, anything expensive (statement text above all) goes behind
`Activity.IsAllDataRequested`, and the statement itself additionally behind `EmitQueryText`.

Benchmarks are part of the work, not a follow-up: `tests/StackExchange.Redis.Benchmarks` needs a
listener-off case, a metrics-only case, and a fully-sampled case, because "metrics always on,
traces sampled" is the configuration that actually matters at scale.

## Where the code goes

All five sites already exist and are already the right ones:

| site | role |
| --- | --- |
| `ConnectionMultiplexer.cs:~2397` | caller thread, post server-selection — capture parent context |
| `Message.CreatedTimestamp` | start time (already recorded) |
| `Message.Complete(PhysicalConnection?)` | reader thread — create, tag, stop; `resultBox.Fault` is in hand |
| `Message.SetExceptionAndComplete` | failure path |
| `Message.PrepareToResend` | `-MOVED` / `-ASK` retransmission |

**Deferred activity creation** (findings §5): capture `Activity.Current?.Context` on the caller
thread — a struct copy, no `AsyncLocal` write — and call `StartActivity(..., parentContext,
startTime)` → `SetEndTime` → `Stop()` on the completion path. Starting the activity on the caller
thread would assign `Activity.Current` with no scope to dispose, leaking our span into the
caller's async flow.

## Sequencing

Phases are ordered by risk and by how much of the design they commit us to. Each is independently
shippable.

### Phase 1 — unblock the reflection, now

Expose the statement string so the contrib package can stop emitting `DynamicMethod` field getters
against our privates, on 3.x, today. This is worth doing *whatever* we decide about the rest:
the contrib package has to support 3.x for years regardless.

`IProfiledCommand` is a public interface, so adding a member to it is a break for implementers
(realistically only test doubles — `ProfiledCommand` is internal sealed — but a break). Preferred
form is therefore an extension method, which is additive and honest about the fact that only our
own implementation can answer:

```csharp
namespace StackExchange.Redis.Profiling;

public static class ProfilingExtensions
{
    // null for any IProfiledCommand we did not create
    public static string? GetStatement(this IProfiledCommand command);
}
```

Returns command + key + script text — the exact payload the reflection reconstructs. Needs a
decision on whether it is gated by `EmitQueryText` or always available to a caller who asks (it is
already opt-in by virtue of being an explicit call).

*Open:* interface member vs. extension method.

### Phase 2 — metrics

Lowest risk of the real work: no `Activity.Current` semantics, no public API surface beyond a
documented meter name, and bounded memory regardless of throughput. This is the half that can run
permanently at Stack Overflow scale, and connection-level telemetry is the part
[Nick already agreed to in 2022](https://github.com/StackExchange/StackExchange.Redis/issues/1044#issuecomment-1069746402):

> For things like connections, disconnects, reconnects, errors: yeah sure, that's a lot lower
> volume and reasonable.

- `db.client.operation.duration` — histogram, seconds, semconv **stable**, buckets
  `[0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10]`. Tags: `db.system.name`, `db.operation.name`,
  `db.namespace`, `server.address`, `server.port`, `error.type` on failure.
- connection instruments from the events and counters we already have —
  `ConnectionFailed`, `ConnectionRestored`, `ErrorMessage`, and observable gauges over
  `GetCounters()` for backlog depth, queue lengths and timeouts. The semconv
  `db.client.connection.*` family is still *development* status, so we follow it where it fits and
  use our own names where it does not, rather than bending our model to a moving target.

### Phase 3 — traces

`ActivitySource` with the deferred-creation pattern. Span kind `CLIENT`, span name = bare command
name (`db.namespace` deliberately excluded from the name per semconv; bare command name is also
what go-redis and Lettuce do). Attribute set per findings §3, honouring
`OTEL_SEMCONV_STABILITY_OPT_IN` exactly as contrib does — see "Subsuming the contrib package"
below, which is what fixes the attribute names, the defaults, and the timing events.

New material that the profiling API cannot express today and that comes free here:
`db.response.status_code` (Redis error prefix — `WRONGTYPE`, `MOVED`, `NOSCRIPT`) and `error.type`.
**Failures are currently invisible to telemetry**, which is the single largest gap.

### Phase 4 — the things contrib gave up on

- **Retransmission.** `-MOVED` / `-ASK` resends, which the contrib converter carries a literal
  `// TODO: deal with the re-transmission` for. `PrepareToResend` is ours; a child span or an
  `ActivityLink` back to the original is straightforward from the inside.
- **Batches and transactions.** One span with `db.operation.batch.size`, name prefixed `MULTI` or
  `PIPELINE` per semconv. Both go-redis (`redis.pipeline`, `db.redis.num_cmd`) and redis-py
  (`db.redis.pipeline_length`) collapse a pipeline to one span; we should too.
- **Connection establishment.** go-redis traces `redis.dial`; we have far richer material —
  handshake, discovery, failover, sentinel — and it is low-volume enough to trace unconditionally.

### Phase 5 — pub/sub

Producer/consumer spans with `ActivityLink`, not client spans; message-bus semconv, not database
semconv. Almost certainly wants its own `ActivitySource` so it can be enabled independently. Out of
scope until the above lands.

## Open questions

Worth putting to @martincostello directly, since he offered to collaborate:

1. **Who owns semconv churn?** Contrib ships three `ActivitySource`s to straddle 1.23 vs 1.42, and
   has never shipped a stable version in six years *because* the conventions are experimental. If
   we emit natively, that becomes our problem — and StackExchange.Redis does not have the option of
   shipping perpetual betas. Do we mark the telemetry `[Experimental]` (we already have the
   `SER00x` machinery in `src/RESPite/Shared/Experiments.cs`), or do we accept that our stable
   package emits attributes that may be renamed under us? This is the strongest argument for the
   middle ground and we should hear it argued before dismissing it. **See question 5 for a way to
   make this much less binary** — gating individual enum members rather than the whole feature, so
   only the callers who opted into unstable conventions carry the diagnostic.
2. **Do its maintainers want the package to become a one-liner, or to keep producing spans?** If we
   go native, does it retire, or keep an `AddRedisInstrumentation()` that calls `AddSource` plus the old
   `Filter`/`Enrich` knobs? Those two callbacks are the only features that do not obviously survive
   the transition. Either way, ask them to add our source name — and ideally to keep adding their
   own for a window, so existing hand-written `AddSource` calls do not break.
3. **Can we have the tests?** Their test suite is the compatibility oracle, and it is Apache-2.0
   going into an MIT repo. Explicit blessing, an agreed attribution form, or a clean-room rewrite
   from the behaviour inventory — but decided up front, not discovered in review.
4. **`Filter` and `Enrich` equivalents.** The case against is that `ActivityListener.Sample` already
   does this at the collector — which answers Nick's 2022 objection that *deciding not to profile*
   costs something per command. The case for is that both existing implementations shipped them
   anyway: Npgsql has filters, enrichment callbacks and span-name providers for commands, batches
   and copy operations, which is the bulk of their public surface (findings §9), and the contrib
   package has `Filter`/`Enrich`. If we adopt them, what gets handed to the callback has to be
   settled first — see "Public API impact".
5. **Should the semconv choice be an explicit enum rather than only an environment variable?**
   `OTEL_SEMCONV_STABILITY_OPT_IN` is ambient, process-wide, and read once at construction — a
   library silently changing the attribute names it emits based on an environment variable is
   exactly the sort of action-at-a-distance that produces a baffled bug report. An explicit setting
   means nobody is surprised:

   ```csharp
   // sketch only
   public enum RedisSemanticConventions { Default, Old, New, Both }

   options.SemanticConventions = RedisSemanticConventions.New;
   ```

   `Default` would mean "follow `OTEL_SEMCONV_STABILITY_OPT_IN`, and its `Old` default if unset", so
   an operator who sets the variable once still gets every instrumentation in the service agreeing
   — which is the whole point of the variable, and a strong reason *not* to simply ignore it.
   Anything else is an explicit override that wins.

   Costs three or four more public API lines than the sketch above (the enum, the property pair, the
   `DefaultOptionsProvider` virtual). Probably worth it: this is the single setting most likely to
   produce "why did my dashboard go blank", and the one place where being explicit is cheap.

   Two details to settle if we do it: whether `Default` resolves at multiplexer construction (as the
   contrib package does) or per command, and whether it is connection-string-parsable like the rest
   of `ConfigurationOptions`.

   **This is also a better answer to open question 1 than gating the whole feature.**
   `ExperimentalAttribute` includes `AttributeTargets.Field` — in the runtime's version *and* in the
   down-level polyfill in `src/RESPite/Shared/Experiments.cs`, so it works on our netfx targets too
   — and enum members are fields. So individual members can be gated:

   ```csharp
   public enum RedisSemanticConventions
   {
       Default,
       Old,
       [Experimental(Experiments.SemanticConventions, UrlFormat = Experiments.UrlFormat)] New,
       [Experimental(Experiments.SemanticConventions, UrlFormat = Experiments.UrlFormat)] Both,
   }
   ```

   The stable half — `Default` and `Old`, which is what everyone gets today — stays plain stable
   API. Only the members that actually track still-experimental conventions carry the gate, so the
   churn risk is scoped to the people who opted into churn. Marking the *whole* telemetry feature
   `[Experimental]` is all-or-nothing and puts a diagnostic in front of people who only ever wanted
   the default behaviour.

   And the exit is clean: when the database conventions go stable, delete the attribute from the
   member. That is neither a source nor a binary break — the diagnostic simply stops firing.

   Repo convention is `[Experimental(Experiments.X, UrlFormat = Experiments.UrlFormat)]` with a
   `docs/exp/SERxxx.md` page; `SER001`–`SER009` are taken (retired IDs stay reserved), so this would
   be `SER010`.
6. **Version/schema pinning.** Should the `ActivitySource` version track the package version, or a
   semconv schema version (contrib uses the latter via `ActivitySourceFactory.Create<T>(version)`)?
7. **Does this need to wait for v4?** The IO core rewrite moves all five hook sites. Hooks placed
   now survive as *concepts* but not as code. Landing metrics on 3.x and traces on 4.x is a
   defensible split; so is landing both on 3.x and accepting the port.

## Things we are deliberately not doing

- **Not depending on any OpenTelemetry package.** Library authors depend on
  `System.Diagnostics.DiagnosticSource` only; this is both Microsoft's and OpenTelemetry's own
  guidance.
- **Not picking our own attribute names or our own default.** We follow
  `OTEL_SEMCONV_STABILITY_OPT_IN` like every other .NET database instrumentation, including its
  current `Old` default, however much we might prefer the new names.
- **Not putting command argument values in spans.** Command and key only, and that opt-in.
- **Not removing or changing the profiling API.** It stays, unchanged, on every target including
  `net461`. Native telemetry is additive.
