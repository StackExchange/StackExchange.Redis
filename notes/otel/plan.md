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
contrib has to support 3.x for years regardless.

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

*Open:* interface member vs. extension method. Raising rather than assuming, per AGENTS.md.

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
what go-redis and Lettuce do). Attribute set per findings §3, emitting **current** semconv names
only — `db.system.name`, `db.operation.name`, `db.query.text` — not the 1.23-era
`db.system`/`db.statement`. Contrib can keep its dual-emit shim for people who need the old names;
we should not carry that migration into a library that has not shipped any of it yet.

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

1. **Who owns semconv churn?** Contrib currently ships three `ActivitySource`s to straddle 1.23 vs
   1.42. If we emit natively, that migration becomes ours. Is the dual-emit window genuinely
   closing, or are we signing up for it permanently? This is the strongest argument for the middle
   ground and we should hear it argued before dismissing it.
2. **Does contrib want to become a one-liner, or keep producing spans?** If we go native, does the
   package retire, or keep an `AddRedisInstrumentation()` that calls `AddSource` plus the old
   `Filter`/`Enrich` knobs? Those two callbacks are the only features that do not obviously survive
   the transition.
3. **`Filter` and `Enrich` equivalents.** Do we need them in-box, or is per-command filtering
   better done by the listener? Nick's 2022 objection was specifically that *deciding not to
   profile* costs something per command; with `ActivityListener.Sample` that decision moves to the
   collector, which is where it belongs.
4. **Version/schema pinning.** Should the `ActivitySource` version track the package version, or a
   semconv schema version (contrib uses the latter via `ActivitySourceFactory.Create<T>(version)`)?
5. **Does this need to wait for v4?** The IO core rewrite moves all five hook sites. Hooks placed
   now survive as *concepts* but not as code. Landing metrics on 3.x and traces on 4.x is a
   defensible split; so is landing both on 3.x and accepting the port.

## Things we are deliberately not doing

- **Not depending on any OpenTelemetry package.** Library authors depend on
  `System.Diagnostics.DiagnosticSource` only; this is both Microsoft's and OpenTelemetry's own
  guidance.
- **Not emitting 1.23-era attribute names.** New code, current conventions.
- **Not putting command argument values in spans.** Command and key only, and that opt-in.
- **Not removing or changing the profiling API.** It stays, unchanged, on every target including
  `net461`. Native telemetry is additive.
