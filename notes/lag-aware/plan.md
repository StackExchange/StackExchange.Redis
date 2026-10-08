# Lag-aware availability checks: the plan

What we intend to do about [`findings.md`](findings.md). That file is the evidence; this one is the
sequence.

Status: **proposed. Step 0 is cleared; step 1 is answered (findings §10): the lag check flips under
real Active-Active lag, on the stale member only, and the tolerance is honoured on a running link.
Cross-region lag magnitudes remain unmeasured (both test clusters share a region).** The core change
(failback health check, member-scoped probes, selection rules) is implemented behind `SER011`; the REST
probe itself is next. See [`api-sketch.md`](api-sketch.md).

> **This branch is not only a planning branch.** `marc/lag-aware-availability` carries these notes
> now, and is the feature branch from here — the implementation lands on top of the same branch and
> the same PR, rather than the notes being merged separately and the work starting again elsewhere.
>
> It has been rebased onto `main` post-#3191 (`git rebase origin/main`), which is what step 0 asked
> for; do not merge `main` in. #3191 squash-merged, so merging it back would move the merge base
> without giving us its ancestry, and the commit list here would start carrying phantoms.

> **Where this can be worked on.** Step 1 needs a real Redis Enterprise deployment and the fault
> injector, which live on a machine holding the environment directory (`~/aws` on the box that has
> it — the AWS multi-cluster template, so `env_output.json` nests its outputs under
> `.clusters.value[0]`, which `FaultInjectorEnvironment` already handles). Nothing in step 1 can be
> done or verified without it, so the work continues there rather than on whichever box happens to
> have the repo. Steps 2 onward are ordinary code and are not tied to a machine.

## Step 0 — wait for #3191 ✅ cleared

**Done.** #3191 merged as `dc915bbc` on 2026-09-18, and this branch has been rebased onto it, so
`tests/StackExchange.Redis.FaultInjector.Tests` is present here now. The rest of this section records
why the wait was worth it.

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

It has landed, so nothing here is gated on it any more. Step 1 is the next thing, and its only
prerequisite is a cluster to point at.

## Step 1 — validate the API against a real deployment

Before any code. Everything in findings §1 is read off documentation, and #3191's own summary is the
warning: *"the specifications are prose, the payloads were never published, and nearly every
assumption I started with was wrong in some way that mattered."* Same vendor, same class of API.

The checklist, each item currently an assumption:

- [x] does the deployed version support `extend_check=lag` at all? **Yes (RS 8.0.22), but only
      meaningfully on Active-Active members; everything else gets `503 bdb_unavailable`
      unconditionally.** Findings §10.
- [ ] does `availability_lag_tolerance_ms` as a query parameter genuinely override the cluster
      default, and what happens if it is absurd (0, negative, enormous)? **Honoured on a running
      link:** under a write burst 100 ms failed while 600000 ms passed. No effect off Active-Active or
      with sync paused (both always fail). Negative not tried.
- [ ] what is actually in a failure body — is `error_code` reliably present, and are the composed
      forms (`bdb_unavailable_shard_unreachable_port_unbound`) real? *Partly:* `error_code` and
      `description` present on every failure seen; lag failure is plain `bdb_unavailable`, the same
      as "not Active-Active"; composed suffixes not yet observed.
- [x] does the lag check flip while the plain check stays green? **Yes, on the stale member only,
      within seconds, clearing when data catches up** (findings §10, `crdt_sync=paused`).
- [x] is `Host: cnm.cluster.fqdn` required on the availability route? **No.**
- [x] `/v1/bdbs/{uid}/availability` versus `/v1/local/bdbs/{uid}/endpoint/availability`? **The local
      form reports for whichever node answered the REST call; unusable through the cluster FQDN.** Use
      the database form. RS155734 not assessed.
- [ ] what does `GET /v1/bdbs?fields=uid,endpoints` return, in the shape Lettuce matches hosts
      against? Not yet asked with those fields; full `/v1/bdbs` bodies captured (contain passwords,
      not committed).
- [ ] **what lag do real geo-replicated links show under load?** The empirical way to settle 100 ms
      (server default, docs, redis-py) versus 5000 ms (Lettuce) — see findings §3. Needs a two-cluster
      environment, which now exists.
- [ ] which fault-injector effects can drive lag past the tolerance — ask `GetValidTriggersAsync`,
      do not assume `network_latency`. **`network_latency` is ruled out** (node-wide, and its cleanup
      fails; findings §10). The `/action` enum also lists `network_failure`, `execute_rladmin_command`
      and `update_cluster_config`; which of these can hold back Active-Active sync safely is the open
      question.

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
- **`src/` may need no changes at all** *(no longer true - see the `Inconclusive` point in step 3)*. `HealthCheckProbe` is externally subclassable (no internal
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
- **Lag-awareness is per member and only for Active-Active members.** Off Active-Active the server
  answers `503 bdb_unavailable` to every lag check (findings §10), so enabling it there would fail a
  healthy database forever. Either refuse the combination at configuration time, or check `crdt` once
  via `/v1/bdbs/{uid}` and fall back to the plain check - the latter needs JSON, the former does not.
- Addressing is REST endpoint **plus** uid per member; uids are not stable across the clusters of one
  Active-Active database.
- **The probe is called per endpoint, but the answer is per database.** `HealthCheck.CheckHealthAsync
  (IConnectionMultiplexer)` fans out one probe call per server (`HealthCheck.Execute.cs:25-40`), so an
  OSS-cluster member with six endpoints would make six identical REST calls per pass. Either cache
  within a pass, or hook in at member level instead.
- **`Inconclusive` is right for failover and wrong for failback.** `ConnectionGroupMember.UpdateState`
  (`MultiGroupMultiplexer.cs:315`) treats anything but `Unhealthy` as connected, and once
  `FailbackDelay` has passed that makes the member eligible again. So "the REST API could not be
  asked" would let the group fail back onto a member that may be stale - the exact case the lag check
  exists to prevent. Keeping an active member while refusing to *return* to an unverified one needs a
  distinction `UpdateState` does not currently make, which means a small `src/` change after all (step 2
  assumed none).
- **A failed REST call → `Inconclusive`, not `Unhealthy`.** This is the deviation from every other
  client (findings §4): Lettuce's two-state `HealthStatus` cannot distinguish "the database is down"
  from "I could not ask", so a management-plane outage can fail over a database that is serving
  traffic perfectly. We have `Inconclusive`; we should use it. Document it as a difference.
- Configuration: REST endpoint, credentials **via a callback so they can rotate** (Lettuce takes a
  `Supplier`), certificate options (step 1 confirmed the management certificate is self-signed by the
  cluster itself, so neither ambient trust nor a separate CA works; the option has to accept a pinned
  certificate or fingerprint), bdb uid, tolerance, and a lag-aware on/off switch.
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
   over, and fails back only once lag recovers. Produce the lag with `crdt_sync=paused` on the target
   member via its cluster's REST API (findings §10), and resume in a `finally`; not with
   `network_latency`.

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
