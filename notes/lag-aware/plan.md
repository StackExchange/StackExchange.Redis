# Lag-aware availability checks: the plan

What we intend to do about [`findings.md`](findings.md). That file is the evidence; this one is the
sequence.

Status: **proposed, and blocked on step 0.** Nothing below has shipped.

> **This branch is not only a planning branch.** `marc/lag-aware-availability` carries these notes
> now, and becomes the feature branch once step 0 clears — the implementation lands on top of the
> same branch and the same PR, rather than the notes being merged separately and the work starting
> again elsewhere.
>
> When #3191 lands, **rebase this branch onto the updated `main`** (`git rebase origin/main`); do not
> merge `main` in. #3191 squash-merges, so merging it back would move the merge base without giving
> us its ancestry, and the commit list here would start carrying phantoms.

## Step 0 — wait for #3191

[#3191](https://github.com/StackExchange/StackExchange.Redis/pull/3191) ("Server-native maintenance
notifications") is open, 107 files, +13,160. It carries
`tests/StackExchange.Redis.FaultInjector.Tests`, and with it the three things this work would
otherwise have to build from scratch:

| what | why it matters here |
| --- | --- |
| `FaultInjector/FaultInjectorClient.cs` | `POST /action` + `GET /action/{id}`, plus `GetValidTriggersAsync(scenario, effect, …)` — lets us ask a deployment which lag-inducing effects it supports instead of guessing |
| `Environment/ClusterRestClient.cs` | already a Redis Enterprise REST client on 9443: Basic auth, CA pinning, `System.Text.Json`, `GET /v1/bdbs?fields=…` |
| the tier itself | environment fixtures, provisioning, gating (`SER_FI_CONFIG_DIR`, `E2E_SCENARIO_TESTS`), and the README describing how to run it |

Building any of that again on this branch would mean writing a second copy of a REST client against
the same cluster, and then reconciling them at merge. Not worth it for a feature whose entire
implementation is smaller than the client that tests it.

**Nothing in steps 1–6 starts before this lands**, with one exception: step 1 is manual
investigation against a deployment and can begin whenever a cluster is available, because it produces
notes rather than code.

## Step 1 — validate the API against a real deployment

Before any code. Everything in findings §1 is read off documentation, and #3191's own summary is the
warning: *"the specifications are prose, the payloads were never published, and nearly every
assumption I started with was wrong in some way that mattered."* Same vendor, same class of API.

The checklist, each item currently an assumption:

- [ ] does the deployed version support `extend_check=lag` at all?
- [ ] does `availability_lag_tolerance_ms` as a query parameter genuinely override the cluster
      default, and what happens if it is absurd (0, negative, enormous)?
- [ ] what is actually in a failure body — is `error_code` reliably present, and are the composed
      forms (`bdb_unavailable_shard_unreachable_port_unbound`) real?
- [ ] is `Host: cnm.cluster.fqdn` required on the availability route? (`ClusterRestClient` sets no
      such header for `/v1/bdbs` and works.)
- [ ] `/v1/bdbs/{uid}/availability` versus `/v1/local/bdbs/{uid}/endpoint/availability` — what does
      each actually report on a multi-node deployment, and is RS155734 (miscalculated endpoint
      metrics) visible?
- [ ] what does `GET /v1/bdbs?fields=uid,endpoints` return, in the shape Lettuce matches hosts
      against?
- [ ] **what lag do real geo-replicated links show under load?** The empirical way to settle 100 ms
      (server default, docs, redis-py) versus 5000 ms (Lettuce) — see findings §3.
- [ ] which fault-injector effects can drive lag past the tolerance — ask `GetValidTriggersAsync`,
      do not assume `network_latency`.

Output: the answers written into `findings.md`, and **captured response bodies saved as fixtures**, so
the stubbed tests in step 5 replay real payloads instead of guessing at the wire format.

## Step 2 — decide where it lives

Findings §6 sets out three options; step 1 informs the choice, so this is deliberately not decided
here. The honest current state:

- **The dependency argument is weaker than it first looked.** `System.Net.Http` needs no package
  reference on any target we ship — it is in-box on .NET Framework, in `netstandard2.0`, and in the
  shared framework on `net8.0`/`net10.0`. The genuinely new dependency would be `System.Text.Json` on
  `net472`/`netstandard2.0`, and findings §5 shows JSON is avoidable if the bdb uid is configured
  rather than discovered.
- **`src/` may need no changes at all.** `HealthCheckProbe` is externally subclassable (no internal
  abstract members), and `IConnectionMultiplexer.RawConfig` and `ConfigurationOptions.Tunnel` are both
  public, so a probe can reach everything it needs from the context it already gets.
- So the question is not really "can we" but "should a general-purpose Redis client carry a
  management-plane HTTP client for one commercial deployment target" — which is a judgement call, not
  a technical constraint.

Leaning: **a separate package** (option B), `net8.0`+ only. It keeps Enterprise-specific surface out
of core, versions independently, and `SocketsHttpHandler.ConnectCallback` — needed for step 4 — is
net5+ anyway, so a down-level build would be degraded regardless. But a new shipping package is a real
ownership cost for a small feature, and that is mgravell's call, not this document's.

## Step 3 — the probe

```csharp
// shape only; names and home package per step 2
public sealed class LagAwareHealthCheckProbe : HealthCheckProbe
{
    public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context);
}
```

Behaviour, with the deliberate deviations called out:

- `GET /v1/bdbs/{uid}/availability`, adding `extend_check=lag` and `availability_lag_tolerance_ms`
  when lag-awareness is on.
- 200 → `Healthy`. A definite negative (503 with a recognised `error_code`) → `Unhealthy`.
- **A failed REST call → `Inconclusive`, not `Unhealthy`.** This is the deviation from every other
  client (findings §4): Lettuce's two-state `HealthStatus` cannot distinguish "the database is down"
  from "I could not ask", so a management-plane outage can fail over a database that is serving
  traffic perfectly. We have `Inconclusive`; we should use it. Document it as a difference.
- Configuration: REST endpoint, credentials **via a callback so they can rotate** (Lettuce takes a
  `Supplier`), CA/certificate options — step 1 confirmed the management certificate is self-signed per
  environment, so ambient trust is not enough — bdb uid, tolerance, and a lag-aware on/off switch.
- Defaults deferred to step 1's measurements rather than copied from either existing client.

Explicit uid first; host-based discovery is a later, optional extra (findings §5).

## Step 4 — the transport seam, if we want it

Only if we want the management plane to honour tunnels. mgravell has offered a `Tunnel` addition;
findings §8 has the shape — a **stream-returning** member, composed into
`SocketsHttpHandler.ConnectCallback` on the consuming side, so no `System.Net.Http` type enters
StackExchange.Redis's public API.

Two reasons it is worth doing beyond testing: a CONNECT proxy configured for Redis almost certainly
applies to the Enterprise REST API too, and `InProcessTestServer` already assigns `Tunnel`, so one
assignment would spoof both planes. Two things it forces: `HttpProxyTunnel` and `LoggingTunnel` each
need an opinion about whether they apply to the management plane.

Separable from step 3 — the probe works without it, just without tunnel support.

## Step 5 — tests

Three tiers, cheapest first:

1. **Stubbed transport.** Hijack the HTTP transport completely, the way `Tunnel` does for RESP, and
   replay the step-1 fixtures: 200, each 503 `error_code`, 404, 401/403, malformed bodies, and
   slow-but-successful responses for probe-timeout behaviour. No listener, no port, runs in CI.
2. **`toys/KestrelRedisServer`.** It already serves HTTP on 5000 over the same `RedisServer`
   singleton that serves RESP, so a fake `/v1/bdbs/{uid}/availability` can lie *coherently with* the
   data plane. Real sockets on both planes; the level where the whole thing is exercised.
3. **Fault-injector scenario test**, in the tier from step 0: drive real lag past the tolerance and
   assert the lag-aware check flips while the plain check stays green — then that the group fails
   over, and fails back only once lag recovers.

Tier 3 is the authoritative one and the slowest; tier 1 is what runs on every push.

## Step 6 — documentation

`docs/` needs the probe, the tolerance decision and its reasoning, the credential/CA requirements,
and — prominently — the `Inconclusive` deviation, because anyone comparing us against Lettuce or
redis-py will otherwise read it as a bug.

## Not doing

- **Not writing a second Redis Enterprise REST client.** Step 0 exists so we extend
  `ClusterRestClient` instead.
- **Not copying a tolerance default.** 100 ms and 5000 ms cannot both be right; step 1 measures.
- **Not implementing host-based discovery first.** Explicit uid keeps JSON out of the required path.
- **Not changing the profiling or health-check abstractions.** They already fit; that is the finding.
