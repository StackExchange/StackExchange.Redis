# Lag-aware availability: API sketch

Status: **sketch for review, not implemented.** Evidence is in [`findings.md`](findings.md) (section 10
especially); sequence in [`plan.md`](plan.md). Everything here sits behind `SER007`
(`Experiments.GeoRedundantFailover`), like the rest of `Availability/`.

Aim: parity with Lettuce, Jedis, redis-py and go-redis where it serves users, and deliberate,
documented deviations where the measurements say the others get it wrong.

## The shape in one example

```csharp
var east = new ConnectionGroupMember("redis-14460.c1.example.com:14460,password=...", "east")
{
    // new: consulted only before moving traffic *to* this member (see 1)
    FailbackHealthCheck = HealthCheck.LagAware(new LagAwareOptions
    {
        Credentials = _ => new(new NetworkCredential("probe@example.com", secrets.Get("re-mgmt"))),
    }),
};
```

That is the whole opt-in. The REST endpoint, database uid, whether the database is Active-Active, and
the tolerance are all derived or left to the cluster (see 3). Liveness stays with the member's
existing `HealthCheck` (PING by default).

## 1. Where the lag verdict goes: a failback-only check, not a new result value

**Proposal:** a second, optional health check per member, consulted only when deciding whether a
member may *become* active.

```csharp
public sealed class ConnectionGroupMember
{
    public HealthCheck? HealthCheck { get; set; }          // existing: liveness, may evict
    public HealthCheck? FailbackHealthCheck { get; set; }  // new: eligibility, never evicts
}

public sealed class MultiGroupOptions.Builder
{
    public HealthCheck? FailbackHealthCheck { get; set; }  // new: group default, null = none
}
```

Rules, applied by `SelectPreferredGroup` / `UpdateState`:

- **The active member is kept while its liveness check passes**, whatever its failback check says.
- **A member becomes eligible for selection** when its liveness check passes *and* its failback
  check (if any) answers `Healthy`. `Inconclusive` from the failback check means **not eligible**:
  "could not ask" must not let the group fail back onto a member that may be stale.
- **If no member is eligible, select on liveness alone.** Every member lagging (a partition between
  regions, say) must degrade to "serve from somewhere", never to "serve from nowhere".
- The failback check runs on the same `HealthCheckInterval` poll as liveness, but only for members
  that are not active (the active member's answer is never used).

**Why not the new `HealthCheckResult` value** (accepted in principle, but this is better, I think):

- One check would have to answer two different questions: "is it alive" (PING) and "is it caught up"
  (REST). A new value means one probe that does both, so every lag-aware user also takes over
  liveness.
- Every consumer of `HealthCheckResult` would have to learn the new value: `CollateAsync`,
  `UpdateState`, all three `HealthCheckProbePolicy` implementations, and any user-written probe or
  policy. Measured: they currently treat everything except `Unhealthy` as healthy, so a value they do
  not know would silently mean "fine".
- It matches go-redis's `FailbackOnly()`, the only client that addresses this at all.

**Parity note:** Lettuce, Jedis and redis-py put the lag check in the single health-check slot, so a
stale reading evicts the active member. Measured: only the member *behind* flips, which is never the
one being written to, so for a single-writer deployment that eviction does not happen, and for a
multi-writer one it would be wrong. This is a deliberate deviation.

## 2. Running once per member, not per endpoint

`HealthCheck.CheckHealthAsync(IConnectionMultiplexer)` calls the probe once per server. A REST probe
answers per database, so an OSS-cluster member with six endpoints would make six identical calls.

```csharp
public enum HealthCheckProbeScope { Endpoint = 0, Member = 1 }

public abstract class HealthCheckProbe
{
    public virtual HealthCheckProbeScope Scope => HealthCheckProbeScope.Endpoint;   // new
}
```

A `Member`-scoped probe is called once per pass, with `HealthCheckContext.Server` set to a connected
server of that member (needed for the configured endpoint and its `RawConfig`). Adding a virtual
member is binary-safe for existing subclasses.

## 3. The probe

```csharp
public sealed partial class HealthCheck   // existing type; new static factories
{
    public static HealthCheck LagAware(LagAwareOptions options);       // extend_check=lag (if Active-Active)
    public static HealthCheck DatabaseAvailability(LagAwareOptions options); // plain availability only
}

public sealed class LagAwareOptions
{
    // required: the one thing that cannot be derived
    public Func<CancellationToken, ValueTask<NetworkCredential>> Credentials { get; set; }

    // optional; null = derive or defer
    public Uri? RestEndpoint { get; set; }          // null: https://<configured db host>:9443
    public int? DatabaseId { get; set; }            // null: discover by host and port
    public LagCheckMode LagCheck { get; set; }      // default Auto
    public TimeSpan? LagTolerance { get; set; }     // null: send none, cluster setting applies
    public RemoteCertificateValidationCallback? CertificateValidation { get; set; }
    public void TrustIssuer(string path);           // same helper as ConfigurationOptions
    public void TrustIssuer(X509Certificate2 issuer);
}

public enum LagCheckMode { Auto = 0, Enabled = 1, Disabled = 2 }
```

The `HealthCheck` returned is an ordinary `HealthCheck` (so `ProbeCount`, `ProbeTimeout` and
`ProbeInterval` apply, via `new HealthCheck.Builder(HealthCheck.LagAware(...)) { ... }`), whose
probe is `Member`-scoped.

### What it does, per pass

1. **Endpoint:** `RestEndpoint`, else `https://<host of the member's first configured endpoint>:9443`.
   Measured: every database's host is `redis-<port>.<cluster fqdn>`, the 9443 certificate's SANs
   cover `*.<fqdn>`, and any node answers. Configured endpoint, not discovered nodes: an OSS-cluster
   database advertises IP addresses.
2. **uid** (once, cached; cleared on 404, as Lettuce does): `DatabaseId`, else
   `GET /v1/bdbs?fields=uid,endpoints`, matched on `dns_name` and port.
3. **Active-Active** (once, cached), for `LagCheck = Auto`: `GET /v1/bdbs/{uid}?fields=uid,crdt`.
4. **Check:** `GET /v1/bdbs/{uid}/availability`, adding `extend_check=lag` when the database is
   Active-Active (`Auto`) or `Enabled`, and `availability_lag_tolerance_ms` only if `LagTolerance` is set.

**Never an unfiltered `/v1/bdbs` or `/v1/crdbs`.** Measured: those return database passwords even to
`db_viewer` when `mask_bdb_credentials` is off.

### Result mapping

| response | result | note |
| --- | --- | --- |
| 200 | `Healthy` | body is empty |
| 503 (any `error_code`) | `Unhealthy` | lag and "unavailable" share `bdb_unavailable` |
| 404 | `Unhealthy` | and clear the cached uid |
| 401, 403, other status | `Inconclusive` | management-plane problem; logged |
| connect / TLS failure, timeout | `Inconclusive` | as above |

The last two rows are the deviation from every other client (all map REST failure to unhealthy). As a
failback check, `Inconclusive` already means "not eligible" (1), so the deviation only changes the
outcome where it should: a management-plane outage can no longer evict anything.

### Defaults compared

| | Lettuce | Jedis | redis-py | go-redis (branch) | **this** |
| --- | --- | --- | --- | --- | --- |
| activation | explicit | explicit | explicit | explicit | explicit |
| REST endpoint | supplied | supplied | supplied | derived from db host | **derived**, overridable |
| uid | discovered | discovered | discovered (unfiltered) | discovered | **discovered (filtered)**, overridable |
| lag check | on | on | on | on | **only if Active-Active** (`Auto`) |
| tolerance | 5000 ms | 5000 ms | 5000 ms | 5000 ms | **unset: cluster's own** (100 ms measured) |
| REST failure | unhealthy | unhealthy | unhealthy | unhealthy | **inconclusive** |
| evicts active member | yes | yes | yes | no (failback only) | **no (failback only)** |
| credentials | rotating supplier | supplier | static | static | **rotating callback** |
| TLS | own truststore | own truststore | own CA / verify | own `tls.Config` | **same callback type as Redis**, separate setting |

## 4. Activation

Explicit, as in every other client: setting `FailbackHealthCheck`. Management credentials are a
separate secret from the Redis password, so no options provider can supply them, and nothing else
needs configuring. What a provider *can* do, later and separately: `RedisEnterpriseOptionsProvider`
could carry a group-wide default once credentials have a home in configuration. Not proposed now.

## 5. Platform

| TFM | |
| --- | --- |
| `net8.0`, `net10.0` | everything |
| `net472`, `netstandard2.0` | everything; JSON parsing for steps 2 and 3 by a small internal reader or a `System.Text.Json` package reference on those targets only (**open**, below) |
| `net461` | works with ambient trust; setting `CertificateValidation`/`TrustIssuer` throws `PlatformNotSupportedException` at creation, on the understanding that `ServerCertificateCustomValidationCallback` needs .NET Framework 4.7.1 (*unverified*; the build will say) |

Test seam: an internal `Func<HttpMessageHandler>` on `LagAwareOptions`, reached via
`InternalsVisibleTo`, so tests replay captured responses without a listener. Kept internal so no
`System.Net.Http` type enters the public API; Lettuce's public `HttpClient` constructor shows there is
demand, and it can be made public later.

## Open

1. **JSON on older targets:** a small internal reader (the shapes are tiny) or `System.Text.Json`.
2. **Names:** `LagAware` follows Lettuce, Jedis and redis-py; `FailbackHealthCheck` is ours.
3. **Credentials type:** `NetworkCredential` (in-box everywhere), or a dedicated type that could
   later carry mTLS (redis-py supports client certificates for this call).
4. **The all-lagging fallback** in 1: select on liveness alone, as proposed, or stay with the
   current active member even if it is no longer healthy.
