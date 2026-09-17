# Lag-aware availability checks

Investigation, September 2026. Does Redis Enterprise's lag-aware database-availability API belong in
our health checks for geo-redundant failover, and if so, where?

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
| Jedis | `LagAwareStrategy` | (same family) |
| redis-py | `LagAwareHealthCheck` | **100 ms** |
| Redis Enterprise cluster | `availability_lag_tolerance_ms` | **100 ms** |
| REST API docs | "Recommended value" | **100 ms** |

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
