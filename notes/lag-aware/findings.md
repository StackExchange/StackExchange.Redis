# Lag-aware availability checks

Investigation, September 2026. Does Redis Enterprise's lag-aware database-availability API belong in
our health checks for geo-redundant failover, and if so, where? The sequence that follows from this
is in [`plan.md`](plan.md).

Short answer: **yes, it is the same feature every other Redis client has already shipped, and our
abstractions already fit it — but it needs HTTP and JSON, which `src/` has neither of, and it does
not need to live in `StackExchange.Redis` at all.**

## 1. What the feature is

Redis Enterprise (Redis Software) exposes a
[database availability API](https://redis.io/docs/latest/operate/rs/monitoring/db-availability/) for
load balancers and monitoring:

```text
GET /v1/bdbs/{uid}/availability                       # whole database
GET /v1/local/bdbs/{uid}/endpoint/availability        # this node's local endpoint, no redirect
```

`200 OK` means available. An endpoint counts as available only if the database's primary shards are
reachable *and* the endpoint's listener port is bound. With the OSS Cluster API enabled, the
database form verifies **all** endpoints; otherwise at least one.

The **lag-aware** extension is what makes this interesting for failover, as opposed to a liveness
probe:

```text
GET /v1/bdbs/{uid}/availability?extend_check=lag
GET /v1/bdbs/{uid}/availability?extend_check=lag&availability_lag_tolerance_ms=100
```

`extend_check=lag` additionally asks whether a replica is *sufficiently synchronised with the
primary* to be a safe failover/failback target. The threshold is cluster-wide, default **100 ms**,
changed with:

```text
PUT /v1/cluster  { "availability_lag_tolerance_ms": 100 }
```

and overridable per request via the query parameter. The REST docs say "Recommended value: 100
milliseconds".

The purpose is explicitly disaster recovery: *"reduce the risk of data inconsistencies during
disaster recovery by … ensuring failover-failback flows only occur when databases are accessible and
sufficiently synchronized."* This is the check that stops you failing **back** onto a region that is
reachable but stale.

> **Corrected by measurement (§10).** On RS 8.0.22 the lag is Active-Active (CRDB) sync lag, not
> intra-cluster replica lag. On any database that is not an Active-Active member, `extend_check=lag`
> answers `503 bdb_unavailable` unconditionally - with or without `replication`, and whatever
> `availability_lag_tolerance_ms` says - while the plain check answers 200. The "replica" wording
> above is the documentation's, and it is misleading.

### Response detail

| status | meaning |
| --- | --- |
| 200 | available |
| 400 | invalid schema |
| 404 | database not found |
| 503 | unavailable, or no quorum |

Non-200 returns a JSON body with `error_code` and `description`:

| form | `error_code` values |
| --- | --- |
| database | `no_quorum`, `db_not_found`, `bdb_unavailable` — suffixed `_shard_unreachable` and/or `_port_unbound`, e.g. `bdb_unavailable_shard_unreachable_port_unbound` |
| endpoint | `no_quorum`, `db_not_found`, `bdb_endpoint_unavailable` |

Authentication is HTTP Basic against the **cluster REST API** (default port 9443), requiring the
`view_bdb_info` permission — roles `admin`, `cluster_member`, `cluster_viewer`, `db_member`,
`db_viewer`, `user_manager`. Note these are *cluster management* credentials, unrelated to the Redis
credentials in `ConfigurationOptions`.

Documented known issue: **RS155734** — endpoint availability metrics are miscalculated.

## 2. The reference implementation

[`LagAwareStrategy`](https://github.com/redis/lettuce/blob/main/src/main/java/io/lettuce/core/failover/health/LagAwareStrategy.java)
in Lettuce implements `HealthCheckStrategy`, whose surface is `getInterval()`, `getTimeout()`,
`getNumProbes()`, `getPolicy()`, `getDelayInBetweenProbes()`, `doHealthCheck(RedisURI)` and
`close()`.

`doHealthCheck` is short:

1. If no cached bdb id: `GET /v1/bdbs?fields=uid,endpoints`, find the first bdb whose endpoints match
   the connection's host, cache its `uid`. No match → log and throw.
2. `GET /v1/bdbs/{uid}/availability`, with `extend_check=lag` and
   `availability_lag_tolerance_ms` when extended checking is on.
3. 200 → `HEALTHY`, anything else → `UNHEALTHY`.
4. On REST failure → **invalidate the cached bdb id** and return `UNHEALTHY`.

Config (`LagAwareStrategy.Config`): `restEndpoint` (URI), `credentialsSupplier`
(`Supplier<RedisCredentials>` — so credentials can rotate), `sslOptions`, `availabilityLagTolerance`,
`extendedCheckEnabled`. Convenience factories `databaseAvailability(...)` (plain),
`lagAware(...)`, `lagAwareWithTolerance(...)`.

Two defaults worth noting: `EXTENDED_CHECK_DEFAULT = true` (lag-aware is on by default), and
`AVAILABILITY_LAG_TOLERANCE_DEFAULT = Duration.ofMillis(5000)`.

## 3. Every client has this, and the defaults disagree

This is not a Java curiosity — it is the client-side geographic failover feature family that Redis
has been rolling out across clients:

| client | type | lag tolerance default |
| --- | --- | --- |
| Lettuce | `LagAwareStrategy` | **5000 ms** |
| Jedis | `LagAwareStrategy` | **5000 ms** |
| redis-py | `LagAwareHealthCheck` | **5000 ms** (*corrected*: its docstring says 100) |
| Redis Enterprise cluster | `availability_lag_tolerance_ms` | **100 ms** |
| REST API docs | "Recommended value" | **100 ms** |

> **Corrected 2026-10-06, read from source rather than docs** (heads as of that date; `gh` search of
> issues/PRs was blocked by the org's SSO, so discussion that did not reach code is not covered):
>
> - Lettuce `LagAwareStrategy.java:163-165`: `EXTENDED_CHECK_DEFAULT = true`, tolerance 5000 ms.
>   Unchanged since it was introduced (#3576, 2026-02-06); #3914 (2026-09-15) promoted it to GA without
>   touching either value.
> - Jedis `mcf/LagAwareStrategy.java:96-97`: the same `true` / 5000.
> - redis-py `asyncio/multidb/healthcheck.py:73`: `DEFAULT_LAG_AWARE_TOLERANCE = 5000`, while line 502's
>   docstring says "(default: 100)" - both from the same commit, `f3806fad`, 2025-10-07. It always sends
>   `extend_check=lag` with the tolerance; there is no plain mode.
> - None checks `crdt`, so given §10, *inferred*: all three report every non-Active-Active database as
>   permanently unhealthy under default settings.
> - All three map any REST failure to unhealthy; none has a third state (§4).
> - TLS is truststore/CA based in all three; none supports pinning, so with the cluster's self-signed 9443
>   certificate a user must import that leaf as a trust anchor.
>
> So the server and docs say 100 ms and every client says 5000 ms. Our plan sidesteps it: send no
> tolerance unless configured, and let the cluster's own setting apply.

**Lettuce is fifty times more permissive than everyone else, including the server's own default and
its own documentation's recommendation.** That is either a deliberate call about real-world WAN
replication lag or an oversight; either way we should not copy a number without deciding it, and it
is worth asking Redis which is intended. 100 ms across a geo-replicated WAN link looks optimistic;
5 s looks like it would permit a failback that loses seconds of writes.

redis-py's other defaults are useful reference points: `rest_api_port` 9443, `verify_tls` True, with
`auth_basic`, `ca_file`, `client_cert_file` and `client_key_file` — i.e. mTLS is supported, not just
Basic.

## 4. It fits what we already have — almost exactly

`src/StackExchange.Redis/Availability/` already implements this shape, gated behind
`[Experimental(Experiments.GeoRedundantFailover)]` (`SER007`). The correspondence with redis-py's
model is close to one-to-one:

| concept | StackExchange.Redis | redis-py | Lettuce |
| --- | --- | --- | --- |
| probe abstraction | `HealthCheckProbe.CheckHealthAsync(HealthCheckContext)` | `AbstractHealthCheck.check_health()` | `HealthCheckStrategy.doHealthCheck(RedisURI)` |
| default probe | `HealthCheckProbe.Ping` | `PingHealthCheck` | ping strategy |
| aggregation policy | `HealthCheckProbePolicy.AllSuccess` / `AnySuccess` / `MajoritySuccess` | `HEALTHY_ALL` / `HEALTHY_ANY` / `HEALTHY_MAJORITY` | `ProbingPolicy` |
| probe count / timeout / interval | `HealthCheck.ProbeCount` / `.ProbeTimeout` / `.ProbeInterval` | `MultiDbConfig` | `getNumProbes()` / `getTimeout()` / `getInterval()` |
| failure latch | `CircuitBreaker` | circuit breaker OPEN | — |
| failover unit | `ConnectionGroupMember` | database in multi-db config | — |
| failback damping | `MultiGroupOptions.FailbackDelay` | weighted selection | — |

Two details that make a lag-aware probe land cleanly:

- **`HealthCheckProbe` is externally subclassable today.** It is a `public abstract partial class`
  with one `public abstract` method and *no* internal or `private protected` abstract members —
  checked. Anyone can implement a probe outside this assembly. The only in-assembly conveniences are
  the memoised `protected internal` result tasks, which an external probe can trivially do without.
- **Health checks can already be configured per member.** `ConnectionGroupMember` carries nullable
  per-member overrides of the group-wide `MultiGroupOptions`, health check included
  (`ResolveHealthCheck(options) => HealthCheck ?? options.HealthCheck`). That matters, because each
  geo member is a *different* Redis Enterprise cluster with its own REST endpoint, its own
  credentials and its own bdb uid — so per-member probe configuration is exactly the shape needed,
  and it already exists.

### Where we are better placed than Lettuce

`HealthCheckResult` has three states — `Healthy`, `Unhealthy`, **`Inconclusive`** — where Lettuce's
`HealthStatus` has two.

That difference is more than cosmetic here. Lettuce maps *"the REST call failed"* to `UNHEALTHY`,
which means a Redis Enterprise **management-plane** outage (REST API down, credentials expired,
9443 firewalled) is indistinguishable from the **data plane** being unavailable — and can therefore
trigger a failover of a database that is serving traffic perfectly well. With `Inconclusive` we can
say "I could not determine this" and let the policy decide, which is the honest answer and the safer
one. `KeyWriteHealthCheckProbe` already uses `Inconclusive` this way for replicas.

This is the single most valuable thing we could do differently, and it should be a deliberate,
documented deviation rather than an accident.

## 5. The awkward part: HTTP and JSON

`src/` contains **no HTTP client and no JSON parser** — checked across `StackExchange.Redis` and
`RESPite`; the only matches are in `obj/` transitive restore graphs. A lag-aware probe needs both:

- **HTTP.** `System.Net.Http` is in-box on `net8.0`/`net10.0` and available on `net472` /
  `netstandard2.0`. Manageable, but it is a new dependency class for this library, and it drags in
  TLS configuration, proxy behaviour, timeouts and connection pooling as things we would own.
- **JSON.** Needed only for `GET /v1/bdbs?fields=uid,endpoints` discovery and for parsing
  `error_code` out of failure bodies. `System.Text.Json` would be a new package reference on
  `net472`/`netstandard2.0`.

**JSON is avoidable on the hot path.** The availability call itself is a *status code* check — 200
versus not-200 — with no body parsing required for the healthy case. If the bdb uid is supplied by
configuration rather than discovered, the required path needs no JSON at all, and `error_code` can be
surfaced as an opaque string for diagnostics. Discovery-by-host (Lettuce's step 1) is the only part
that genuinely needs a parser, and it is optional convenience: anyone configuring a REST endpoint,
credentials and a tolerance already knows their database.

That suggests an explicit-uid-first design, with discovery as a later, optional extra.

## 6. Options

**A — in `StackExchange.Redis`.** Ships a `HealthCheckProbe.LagAware(...)` alongside `Ping` and
`StringSet`. Most discoverable; matches what Lettuce/Jedis/redis-py do (all in-box). Costs the core
package an `HttpClient` dependency, plus TLS/proxy/credential surface, for a feature that only
applies to one commercial deployment target.

**B — a separate package.** `StackExchange.Redis.Enterprise` (or similar) subclassing the *already
public* `HealthCheckProbe`. **This requires no change to `StackExchange.Redis` whatsoever** — the
extension point exists and is sufficient. Keeps HTTP, JSON, TLS and cluster credentials out of the
core package and lets the Enterprise-specific bits version on their own cadence.

**C — do nothing, and document.** The extension point is public; users targeting Redis Enterprise
can write a ~50-line probe. Cheapest, and the least good discovery story for the people who most
need it.

B looks strongest on the evidence: the feature is deployment-specific, the dependency is real, and —
unlike the OpenTelemetry case, where a second assembly would have needed internals it could not have
— the hook here is *already public and already sufficient*. Worth confirming that nothing in
`HealthCheckContext` is missing for a real implementation before committing to it.

## 7. Testing: the toy server can spoof this, and already has the HTTP half

`toys/KestrelRedisServer` **already listens on HTTP**. From `Program.cs`, one Kestrel host serves
both planes over the same `RedisServer` singleton:

```csharp
options.ListenLocalhost(5000);                                  // "HTTP 5000 (test/debug API only)"
options.ListenLocalhost(ip.Port, b => b.UseConnectionHandler<RedisConnectionHandler>());  // RESP 6379
```

with a single catch-all route today (`app.Run(ctx => ctx.Response.WriteAsync(server.GetStats()))`).
Adding `/v1/bdbs/{uid}/availability` beside that is a handful of lines, and — because the route
closes over the *same* `RedisServer` instance the client is talking RESP to — the fake management
plane can be made to lie **coherently with** the data plane rather than independently of it.

Spoofing infrastructure for test purposes is also already the established intent of this toy; the
file carries a commented-out block headed *"demonstrate cluster spoofing"* that flips
`ServerType` to `Cluster`, adds an empty node and migrates a slot. A fake Redis Enterprise
management plane is the same idea applied to a different API.

What that buys is the ability to script the scenarios that decide whether the failover logic is
correct, none of which can be produced on demand against a real cluster:

- healthy (200)
- `503 bdb_unavailable_shard_unreachable`, `503 no_quorum`, `404 db_not_found`
- **200 for the plain check, 503 for `extend_check=lag`** — reachable but stale. *This asymmetry is
  the entire point of the feature.* (See the fault injector below: this one **can** also be produced
  against a real cluster, contrary to what one would assume.)
- slow responses, to exercise `HealthCheck.ProbeTimeout`
- 401/403, to exercise credential rotation and — importantly — to check we return `Inconclusive`
  rather than `Unhealthy` when the management plane is the thing that is broken (§4)

### Two levels, and a design consequence

The existing test harness points at the right answer for the cheap level — but the two planes are
**not** symmetric, and this is the thing to get right.

`InProcessTestServer` derives from `MemoryCacheRedisServer` and sets `Tunnel = new InProcTunnel(this)`.
It **does not run as a network server at all**: there is no listener, no port, no socket. The client
reaches it because `ConfigurationOptions.Tunnel` replaces the transport wholesale, so "connecting" is
a method call. That is what makes those tests fast and deterministic.

This is not a hack bolted on for tests — it is a first-class, public, documented extension point.
`StackExchange.Redis.Configuration.Tunnel` is a `public abstract class` whose
`ConnectTransportAsync(...)` returns a `RESPite.Transports.DuplexTransport`, i.e. an implementation
**hijacks the transport layer completely**; the shipped `Tunnel.HttpProxy(...)` does the same thing
for CONNECT proxying. `InProcTunnel` is simply another implementation of that same public seam.

There is no such seam on the HTTP side. `HttpClient` resolves a URL and opens a socket, so **you
cannot point a lag-aware probe at the in-process server — there is nothing there to point at.**

So the model to copy is the one the library already has: hijack the HTTP transport completely, the
same way `Tunnel` hijacks the RESP one. In .NET that means an injectable `HttpMessageHandler` (or our
own abstraction over it), which is the exact analogue — `HttpMessageHandler` *is* the HTTP transport,
and substituting it answers requests in-process with no listener, no port and no TLS.

Hence the sharper version of the design consequence below: a configurable *base URL* is not enough.
If the probe's only seam is a `Uri`, every test needs a real listener on a real port, and the cheap
in-process level is simply unavailable. Lettuce evidently hit the same wall — its second constructor,
`LagAwareStrategy(Config, HttpClient)`, exists so the transport can be substituted.

`toys/KestrelRedisServer` then covers the other level: a real, out-of-process, full-fidelity fake with
actual sockets on both planes, for manual exercise and end-to-end runs. Note that this is also the
only level where the *whole* thing is exercised, because it is the only place both planes are real.

The combination is what makes this worth doing: **in-process RESP fakes for several members, plus a
stubbed management plane per member, is a complete geo-redundant failover test with no external
infrastructure at all** — and geo-redundant failover is otherwise close to untestable, which is
presumably why it is still behind `SER007`.

### Real servers are available, and they answer different questions

mgravell notes we have access to real servers that will support this. That does not replace the
fakes; it does something the fakes cannot, and it should come **first**.

Everything in §1 of these notes is read off *documentation*, and documentation for a recent feature
is exactly where reality and prose diverge. A short session against a real cluster settles a list of
things currently taken on trust:

- does the deployed version support `extend_check=lag` at all, and does the
  `availability_lag_tolerance_ms` query parameter genuinely override the cluster default?
- what does a failure body actually look like — are the composed codes
  (`bdb_unavailable_shard_unreachable_port_unbound`) real, and is `error_code` reliably present?
- is a `Host: cnm.cluster.fqdn` header actually required, as the docs' header table implies?
  (`ClusterRestClient` sets no such header and works, so: apparently not — but it only issues
  `GET /v1/bdbs`, so worth re-checking on the availability route.)
- **what lag do real geo-replicated links actually show?** This is the empirical way to settle
  100 ms versus Lettuce's 5000 ms (§3), and it is a much better argument than reading either
  default off a page.

The two then compose rather than compete, and the synthesis is the useful bit: **use the real
cluster to capture responses, and the fakes to replay them.** A recorded set of genuine 200/503/404
bodies baked into the stub means the deterministic tests stop guessing at the wire format.

### The fault injector — and we already have one

I had assumed a real cluster could not be made to show "reachable but stale" on demand. **That is
wrong twice over**: Redis ships a fault injection service for exactly this, *and we already have a
test tier built on it.*

**`tests/StackExchange.Redis.FaultInjector.Tests`** exists on `marc/maint-optin-server`, open as
**[#3191](https://github.com/StackExchange/StackExchange.Redis/pull/3191)** (107 files, +13,160,
unmerged at the time of writing). Per its README it drives *"a real Redis Enterprise deployment
through the fault injector, and watch[es] how SE.Redis reacts … the tier that can observe what no
in-process fake can: real DNS, real TLS identity, real timing."* Gating is
`SER_FI_CONFIG_DIR` plus an explicit `E2E_SCENARIO_TESTS=true` opt-in, with `FAULT_INJECTION_API_URL`
defaulting to `http://127.0.0.1:20324`.

Two pieces of it matter directly here.

**`FaultInjector/FaultInjectorClient.cs`** — `POST /action` returning an id, `GET /action/{id}` to
poll, with its own note that this is *"the same shape go-redis, redis-py and node-redis wrap …
they integrated independently and converged, so the contract is stable"*. It is richer than Lettuce's:
alongside `StartActionAsync` / `RunActionAsync` / `WaitForActionAsync` there is
`GetValidTriggersAsync(scenario, effect, clusterIndex)`, so **we can ask the injector what
lag-inducing effects the deployment actually supports rather than guessing at an action name.**
Lettuce's own Active-Active failover test uses `network_latency` with `bdb_id`, `delay_ms` and
`duration`, which is the obvious candidate to look for.

**`Environment/ClusterRestClient.cs`** — *already a Redis Enterprise REST client*, described as "the
cluster's own REST API on port 9443, for the few facts the fault injector does not expose". It
already does `GET /v1/bdbs?fields=uid,name`, with HTTP Basic auth and `System.Text.Json`. Adding
`GET /v1/bdbs/{uid}/availability?extend_check=lag` beside it is a method, not a project.

That client also **answers several questions in §9 empirically**, which is worth more than the
documentation they were drawn from:

- **TLS on 9443 is self-signed per environment.** The client pins to a CA from the config directory
  via `ServerCertificateCustomValidationCallback` with `X509ChainTrustMode.CustomRootTrust`, with the
  comment *"this channel carries credentials"*. So a lag-aware probe **does** need CA/cert
  configuration in the redis-py mould; relying on the ambient trust store will not do.
  *Corrected (§10):* the 9443 certificate is self-signed by the cluster itself (issuer = subject =
  cluster FQDN, minted at bootstrap), and the CA in the config directory signs only the RESP-plane
  proxy certificates - so that chain build could never succeed. `ClusterRestClient` now pins the leaf
  on first use instead. The conclusion stands and sharpens: a probe needs a pinned certificate or
  fingerprint option, not merely a custom CA.
- **Auth is HTTP Basic**, confirmed against a real cluster rather than inferred from Lettuce.
- **`/v1/bdbs?fields=…` returns an array of objects carrying the requested fields**, so Lettuce's
  `fields=uid,endpoints` discovery is straightforward. That weakens the §5 argument a little: skipping
  discovery still keeps JSON out of **`src/`**, but "discovery is hard" is not the reason to skip it.

The fakes are demoted, not removed: they still cover machines with no Enterprise cluster, they are
far faster (injector actions budget minutes, plus a stabilisation wait), and they reach what the
injector does not obviously reach — specific `error_code` bodies, 401/403 credential expiry, and
slow-but-successful responses for probe-timeout behaviour.

**Sequencing consequence:** an end-to-end lag-aware test is downstream of #3191. That is a real
dependency to plan around, not a detail.

And #3191's own write-up is the strongest possible support for validating §1 before writing code:
*"the specifications are prose, the payloads were never published, and nearly every assumption I
started with was wrong in some way that mattered."* Same vendor, same class of API, same trap.

Gating is a solved problem here: `tests/.../Helpers/Skip.cs` already has `IfNoServer(host, port)`,
`IfNoCluster()`, `IfNoFailoverPair()` and `UnlessLongRunning()`, and `TestConfig` carries per-role
host/port settings, so an Enterprise-backed test suite follows an established pattern and skips as
inconclusive wherever the cluster is absent — which is everywhere except the machines that have one.

The design consequence: **whatever we build must be able to hijack the HTTP transport completely,
from day one** — an injectable `HttpMessageHandler`-shaped seam, not merely a configurable base URL.
That is a requirement on the API shape, it falls out of testability rather than taste, and it is the
same decision the library already made for RESP with `Tunnel`. It also argues mildly against option
C — "document the extension point and let users write it" leaves us with no test coverage of a
failover path we ship.

## 8. If `Tunnel` gains an API for this

mgravell has offered to add one. It is a good fit — but the *shape* decides whether it helps or
quietly undoes §6.

### Two functional arguments for `Tunnel` specifically

1. **The proxy case is already right.** If Redis traffic goes through a CONNECT proxy
   (`Tunnel.HttpProxy(...)`), the Redis Enterprise REST API is almost certainly behind the same
   corporate proxy. Hanging the management-plane transport off the same object means that case works
   by construction instead of needing a second, parallel proxy setting that users must remember to
   keep in sync.
2. **One assignment configures both planes in tests.** `InProcessTestServer` already sets
   `Tunnel = new InProcTunnel(this)`. If the HTTP seam rides on the same object, the harness gets
   management-plane spoofing everywhere it already gets data-plane spoofing, with no new wiring.

### The shape that would be a mistake

```csharp
// don't
public virtual HttpMessageHandler? CreateHttpMessageHandler(Uri restEndpoint);
```

That puts `System.Net.Http` types into **StackExchange.Redis's public API**, giving the core package
a hard dependency for the sake of one commercial deployment target — exactly what option B exists to
avoid. It would make the core-vs-package question moot by deciding it the expensive way.

### The shape that works

Stay transport-neutral. `Tunnel` yields a `Stream` (or `DuplexTransport`) for an endpoint, and the
Enterprise-side package composes that into an HTTP stack itself:

```csharp
// core: no System.Net.Http anywhere
public virtual ValueTask<Stream?> ConnectManagementStreamAsync(
    EndPoint endpoint, CancellationToken cancellationToken) => default;

// Enterprise package: hand it to SocketsHttpHandler
new SocketsHttpHandler
{
    ConnectCallback = async (ctx, ct) =>
        await tunnel.ConnectManagementStreamAsync(Resolve(ctx.DnsEndPoint), ct)
        ?? await DefaultConnectAsync(ctx, ct),
};
```

`SocketsHttpHandler.ConnectCallback` is `Func<SocketsHttpConnectionContext, CancellationToken,
ValueTask<Stream>>` — a `Stream` is precisely the currency needed, so a stream-shaped `Tunnel` member
composes into a complete HTTP hijack with no HTTP types in core at all. `Tunnel` already speaks this
language: `BeforeAuthenticateAsync` returns `ValueTask<Stream?>` and `ConnectTransportAsync` returns
`ValueTask<DuplexTransport?>` (itself public, gated `SER009`).

Adding a **virtual** member to a public abstract class is source- and binary-safe for existing
subclasses, so this is additive in the sense AGENTS.md requires.

### Caveats worth stating

- **`ConnectCallback` is net5.0+.** There is no equivalent on `HttpClientHandler`/`WinHttpHandler`,
  so a down-level implementation would need direct handler injection instead. An Enterprise package
  targeting `net8.0`+ sidesteps this entirely, and is defensible for a new deployment-specific
  feature.
- **Signature mismatch.** Existing `Tunnel` members are RESP-connection-shaped (`EndPoint` plus
  `ConnectionType`). HTTP wants scheme, host, port and TLS — i.e. a `Uri`. Either shape is workable;
  neither is free of a little awkwardness.
- **It widens `Tunnel`'s remit** from "the Redis connection" to "transports this library initiates",
  which the existing subclasses then have to have an opinion about. `HttpProxyTunnel` almost
  certainly *should* apply to the management plane; `LoggingTunnel` probably should *not* start
  logging REST traffic by default. Both need deciding rather than falling out.

### One question this already answers

Open question 5 below asked whether `HealthCheckContext` carries enough for a real probe. It does:
`IConnectionMultiplexer.RawConfig` and `ConfigurationOptions.Tunnel` are **both public**, so an
external probe can already reach `context.Server.Multiplexer.RawConfig.Tunnel` with no change to
`HealthCheckContext` at all.

## 9. Open questions

1. **Which tolerance default?** 100 ms (server, docs, redis-py) or 5000 ms (Lettuce)? Worth asking
   Redis directly; the divergence looks unintentional.
2. **`Unhealthy` or `Inconclusive` when the REST call fails?** See §4. Recommend `Inconclusive`, but
   note that this makes us behave differently from every other client, so it needs to be a stated
   choice.
3. **Which endpoint — `/v1/bdbs/{uid}/availability` or `/v1/local/bdbs/{uid}/endpoint/availability`?**
   Lettuce uses the former. The latter does not redirect to the primary node, which may be the more
   accurate signal when probing a specific endpoint, and is what the docs recommend for load
   balancers under the `all-nodes` proxy policy. Note known issue RS155734 against the endpoint form.
   *Partly answered (§10):* the local form answers for whichever node served the REST call, so through
   the cluster FQDN it is effectively random under `proxy_policy: single`. Not usable from a client
   unless it addresses a specific node.
4. **Explicit bdb uid, discovery, or both?** Explicit avoids JSON entirely (§5).
5. ~~**Is `HealthCheckContext` sufficient?**~~ Answered in §8 — `RawConfig` and
   `ConfigurationOptions.Tunnel` are both public, so a probe can reach the tunnel from the context it
   already gets. What remains: if a single probe instance is ever shared across members it still
   needs to map endpoint → REST configuration, which per-member health checks make unnecessary but do
   not forbid.
6. **Credentials and rotation.** Lettuce takes a `Supplier<RedisCredentials>` so credentials can
   rotate; redis-py supports Basic plus mTLS; our own `ClusterRestClient` pins a CA because the
   management certificate is self-signed per environment (§7). So the probe needs *at least* Basic
   plus CA pinning, and should not bake in a static password.
7. ~~**How is this tested?**~~ Largely answered in §7: the tier exists, in #3191. What is genuinely
   open is **sequencing and scope** — an end-to-end lag-aware test is downstream of an unmerged
   107-file PR, so either this waits for #3191 or it starts at the stubbed tier and the scenario test
   follows. Also open: whether the deployments we have access to expose a lag-inducing effect at all
   (ask `GetValidTriggersAsync` rather than assuming `network_latency`).
8. **Does any of this need `src/` changes at all?** On the evidence so far, possibly none:
   `HealthCheckProbe` is externally subclassable, `RawConfig`/`Tunnel` are public, and the REST
   plumbing precedent lives in the test tier. The `Tunnel` addition in §8 is the one thing that would
   have to land in the core package — and only if we want the management plane to honour tunnels.

## 10. Measured against real deployments (2026-09-18 to 2026-09-30)

Everything here was observed, not read. Three AWS-template environments, all RS 8.0.22, three nodes per
cluster; the last one (`marcgravell-test-dc1342d5`) has two clusters, `c1` and `c2`, carrying two
Active-Active databases between them. Where a line is an inference rather than an observation it says so.

### `extend_check=lag` is an Active-Active check

Same cluster (c1), same session:

| database | `crdt` | `replication` | plain | `extend_check=lag` at 100 ms / 600000 ms / 0 ms |
| --- | --- | --- | --- | --- |
| `re-active-active` | true | false | 200 | 200 / 200 / 200 |
| `re-active-active-oss-cluster` | true | false | 200 | 200 / 200 / 200 |
| control, freshly created | false | false | 200 | 503 / 503 / 503 |
| control, freshly created | false | true | 200 | 503 / 503 / 503 |

Both Active-Active databases answer the same on c2. The control 503 is always
`{"error_code":"bdb_unavailable","description":"BDB <uid> is not available"}`, held for the whole 30 s
observed from creation, and was reproduced on two earlier environments over 60 s.

- **Measured:** off Active-Active, the lag check fails regardless of replication and tolerance - the
  tolerance parameter has no observable effect at all there.
- **Inferred:** the server has no sync lag to measure on such a database and reports "unavailable"
  rather than "not applicable".
- **Consequence for the probe:** lag-awareness on a non-Active-Active member turns a healthy database
  into a permanently unhealthy one. It must be opt-in per member, and that specific 503 should not be
  read as a data-plane verdict - `Inconclusive` at most. This is a stronger version of the §4 argument.
- **Inferred about Lettuce, untested:** `EXTENDED_CHECK_DEFAULT = true` should make `LagAwareStrategy`
  report `UNHEALTHY` forever against any database that is not Active-Active. Worth putting to Redis with
  the 100 ms versus 5000 ms question (§3).
- **Unknown:** whether a 0 ms tolerance is honoured (and an idle link simply reports zero lag) or
  ignored. The idle Active-Active result cannot tell those apart; it needs real lag.

### Under real lag: the check flips, and only on the stale member (2026-10-05)

Environment `marcgravell-test-9db7bbf0`, two clusters, `re-active-active` (uid 1 on both, paired by
`crdt_guid`). Sync *into* c2's member paused with `PUT /v1/bdbs/1 {"crdt_sync":"paused"}` on c2 for 60 s
while c1 took an `INCR` every ~5 s and c2 was read back; then `{"crdt_sync":"enabled"}`.

| phase | c1 member | c2 member | data |
| --- | --- | --- | --- |
| baseline | plain 200, lag 200 (100 / 600000 / 0 ms) | plain 200, lag 200 (all three) | c2 tracks c1 |
| c2 sync paused | unchanged | plain 200, **lag 503 at all three**, first sample ~2 s in | c1 3 to 14, c2 stuck at 2 |
| resumed | unchanged | lag 503 ~5 s more, then 200 | c2 reaches 14 |

- **Measured:** the plain check never noticed; the lag check on the stale member flipped within seconds
  and cleared once the data had caught up. This is the "reachable but stale" case the feature exists for.
- **Measured:** only the member that is *behind* flips. The member being written to stays green - which
  is the right polarity for failback, where the question is "is the target caught up".
- **Measured:** with sync paused, 600000 ms still answers 503 although the true lag was seconds. A paused
  link is unavailable regardless of tolerance.
- **Measured: the tolerance does govern a running link.** Sync enabled; a server-side Lua loop on c1
  wrote ~50 MB per call (500 x 100 KB over a 200-key space) for 30 s, ~2.75 GB in all, while c2 was
  sampled every ~0.5 s. In 66 samples the 100 ms check failed twice, each time with c2 visibly a few
  batches behind, and recovered on the next sample; the 600000 ms check never failed. So 100 ms and
  600000 ms disagree exactly when the link is running but behind - the parameter is honoured.
- **Caveat on the numbers:** both clusters are in `us-east-1`. Under that load sync stayed under
  100 ms almost all the time, but that says nothing about a cross-region link, which is the case the
  100 ms versus 5000 ms question (§3) is actually about.
- **Measured:** the failure body is the same as for a non-Active-Active database -
  `{"error_code":"bdb_unavailable","description":"BDB 1 is not available"}`. No lag-specific code, so a
  probe cannot tell "lagging" from "not applicable" by the response; Active-Active membership has to be
  configuration, not inference.
- **Inferred:** at baseline c2 was observed one write behind (read 1 after c1 wrote 2) while the 0 ms
  check passed - either lag is sampled coarsely or 0 is not honoured.
- **Tooling:** `crdt_sync` `paused`/`enabled` is a narrow, reliably reversible way to produce lag - one
  member, no network change, `status` returns to `active` within a few seconds. Far better suited to a
  scenario test than `network_latency` or `network_failure` (which drops all inbound bar SSH on every
  hosting node and kills `dmcproxy`). It is a direct REST mutation, not an injector action.

### Other answers

- **Cluster default** `availability_lag_tolerance_ms` is 100, on every environment - the server agrees
  with the docs and redis-py, not Lettuce.
- **`Host: cnm.cluster.fqdn` is not required.** A bogus `Host` on the availability route still gets 200.
- **Both error-code families are real.** Database form: `bdb_unavailable`. Local form:
  `bdb_endpoint_unavailable`, description `"Local endpoint of BDB <uid> on node <n> is not available"`.
  The composed suffixes (`_shard_unreachable`, `_port_unbound`) have not been observed yet - nothing so
  far has broken a shard.
- **Healthy is an empty 200.** No body to parse; JSON appears only on failure, as §5 hoped.
- **`fields=` filters on `/v1/bdbs` but not on `/v1/cluster`**, which returns the whole object.
- **The local form answers for the node that served the request.** Through the cluster FQDN that was
  always node 1, which usually does not host the endpoint under `proxy_policy: single` - so 503 for three
  of four Active-Active members that were serving traffic perfectly well.
- **`/v1/bdbs` returns database passwords** (`authentication_redis_pass`, `authentication_admin_pass`)
  when `mask_bdb_credentials` is false, as on these clusters, and `/v1/crdbs` returns the same for every
  instance. Captured bodies cannot be committed as fixtures without scrubbing. Relevant to credential
  scope too: whether `cluster_viewer`/`db_viewer` see the same was not checked (we used admin).

### What a probe can discover, and with what (2026-10-06, `marcgravell-test-60790432`)

**Active-Active membership is not visible over RESP.** Compared on c1: both Active-Active members and a
freshly created plain database (since deleted).

- `HELLO` is identical in shape (`mode: cluster`, `role: master` on all three, including the plain one).
- `CRDT.INFO` is an unknown command everywhere; `INFO crdt` / `INFO crdb` are empty everywhere; no INFO
  field mentions crdt.
- The only INFO differences are incidental: `cmdstat_replconf` on the members (the syncer's own traffic,
  so absent until a peer has connected), a different module list (the members load only ReJSON and
  search), and `rdb_changes_since_last_save`. None is a signal to build on.
- So the probe has to learn it from REST: `GET /v1/bdbs/{uid}?fields=uid,crdt` answers
  `{"crdt":true,"uid":1}`, or it is configuration.

**`db_viewer` is enough, and the least privileged role still sees database passwords.** Temporary
roles (`management: db_viewer`, then `cluster_viewer`) and users, deleted afterwards. RS 8 rejects the
legacy `role` field (`role_not_exist`); roles are objects referenced by `role_uids`, and a fresh cluster
has only `Admin`.

| route, as `db_viewer` (`cluster_viewer` identical) | result |
| --- | --- |
| `/v1/bdbs/{uid}/availability`, with and without `extend_check=lag` | 200 |
| `/v1/bdbs/{uid}?fields=uid,crdt` | 200, no password fields |
| `/v1/bdbs?fields=uid,endpoints` | 200, 599 bytes, no password fields |
| `/v1/bdbs/{uid}` (unfiltered) | 200, **`authentication_redis_pass` and `authentication_admin_pass` populated** |
| `/v1/crdbs` | 200, every instance's passwords populated |

This is with `mask_bdb_credentials: false`, the cluster's setting here; masked clusters were not
tested. Consequence: the probe must only ever issue `fields=`-filtered requests, and the docs should
recommend `db_viewer`. Possibly worth raising with the RS team that the viewer roles see secrets.

**The REST endpoint is derivable from the database hostname.** Every database's `dns_name`, plain,
Active-Active and OSS-cluster alike, is `redis-<port>.<cluster fqdn>`, so the management endpoint is
`https://<cluster fqdn>:9443`. But the OSS-cluster database advertises **IP addresses** through
`CLUSTER SLOTS`/`CLUSTER SHARDS` (`oss_cluster_api_preferred_endpoint_type: ip`), so the derivation must
start from the member's *configured* endpoint, not from discovered nodes. A customer CNAME or a bare IP
defeats it, so it can only be a default with an override. uid discovery by matching `dns_name` and port
against `/v1/bdbs?fields=uid,endpoints` is safe on credentials (filtered, no passwords).

### Multi-cluster shape

- `env_output.json` nests clusters under `.clusters.value[N]`, each with its own FQDN and credentials.
  `FaultInjectorEnvironment` reads only `[0]`.
- Each cluster has its own self-signed 9443 certificate, so pins are per host
  (`cluster-cert.<fqdn>.sha256`).
- Member uids happened to match across c1 and c2; the platform does not promise it. `/v1/crdbs` on one
  cluster reports the other's instance with `db_uid=None`, so pairing members means reading each
  cluster's `/v1/bdbs` and matching on `crdt_guid`. The probe's configuration should therefore be
  "cluster REST endpoint plus uid" per member, not a uid shared across the group.

### Fault injection

- **`network_latency` is not safe to use for this.** It applies `netem` to the node's primary interface,
  not to the database, and its scheduled removal fails ("No existing session" - the cleanup thread reuses
  an SSH session that has been torn down) while the action still reports success. On 2026-09-18 that
  left a 5 s delay on two nodes, which took down the cluster's own DNS and 9443 until removed by hand
  over SSH. Reported to `redis-developer/cae-client-testing`.
- **Holding back sync:** done with `crdt_sync=paused` on the stale member (above), not with the injector.
