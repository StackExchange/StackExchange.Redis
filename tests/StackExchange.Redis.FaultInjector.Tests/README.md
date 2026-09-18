# StackExchange.Redis.FaultInjector.Tests

Scenario tests that drive a **real Redis Enterprise deployment** through the fault injector, and watch how
SE.Redis reacts. These are the tier that can observe what no in-process fake can: real DNS, real TLS identity,
real timing.

## Running them

```bash
cd <the environment directory>          # holds docker-compose.yml, env_output.json, ...
docker compose up -d                    # 10-15 minutes for the cluster to come up

export SER_FI_CONFIG_DIR=$PWD           # or FI_CONSOLE_CONFIG_DIR, which is also honoured
export E2E_SCENARIO_TESTS=true          # explicit opt-in: these create and delete databases
dotnet test tests/StackExchange.Redis.FaultInjector.Tests
```

**Point it at the *current* environment.** A stale directory passes every check we make: the credentials are
only checked for being well-formed, and node discovery and database provisioning both go through the injector,
which is configured separately - so the only thing that fails is the template databases, which belong to a
cluster that no longer exists. If `endpoints.json` names a different cluster than the one the injector is
driving, that directory is not the one you want.

One path is the whole configuration. That directory is the one mounted into the injector as `/app/config`, so
it already holds the cluster credentials (`env_output.json`), the CA certificate, and the compose file; nothing
has to be hand-carried into the test run. `FAULT_INJECTION_API_URL` overrides the injector URL
(default `http://127.0.0.1:20324`).

## The one test that is opt-in even here

`RetentionAgeScenarioTests` measures how long the server retains a completion for replay to a
newly-opted-in connection - the last unmeasured property of the catch-up channel. It fires one failover and
then probes on a ladder (1, 2, 5, 10, 20, 30, 45, 60, 90, 120, 180, 240 minutes), so it runs for as long as
you let it and skips unless you ask for it:

```bash
export SER_FI_RETENTION_AGE_MINUTES=180        # trims the ladder; absent means skip
export SER_FI_RETENTION_AGE_LOG=/tmp/age.log   # optional; defaults under the temp directory
```

Two details that are not incidental:

- **Progress is written to a file, flushed per line.** `ITestOutputHelper` is buffered until the test ends, so
  over three hours a run in progress and a run that has wedged look identical through the normal channel.
- **Two probes per rung.** If the first sees the replay and the second does not, the server clears the retained
  item on delivery - in which case later rungs are measuring an empty channel rather than an expired one. That
  confound is invisible with one probe per rung and looks exactly like an early expiry. (Measured 2026-09-02:
  both probes see it, so retention is not consumed on delivery.)
- A probe that cannot connect is recorded as **inconclusive, not as a miss**: the run outlives its cluster's
  lease easily, and counting a dead environment as "no replay" would report an expiry at whatever minute the
  cluster went away.

## The destructive scenarios, behind a second gate

`DestructiveScenarioTests` breaks things rather than moving them - a shard, a node, or a proxy is killed - so
it needs its own opt-in on top of the tier's:

```bash
export SER_FI_DESTRUCTIVE=true      # absent means skip
export SER_FI_NODE_TO_KILL=2        # optional; which node node_failure kills (default 2, never 1)
```

`E2E_SCENARIO_TESTS` says "you may create and delete databases"; it does not say "you may kill nodes", and a
cluster that has to be re-provisioned is 10-15 minutes of somebody's afternoon. Run these at the end of a
cluster's life, watching.

Two things measured on 2026-09-03 that change how you read a green run:

- **The scope differs per action**, and the injector's schema does not say so. Learned by being told:

  | action | parameter |
  |---|---|
  | `shard_failure`, `proxy_failure` | `bdb_id` |
  | `node_failure`, `node_remove` | `node_id` |
  | `cluster_failure` | `node_ids` (a list) |
  | `execute_rladmin_command` | `bdb_id` *and* `rladmin_command` |
  | `network_failure` | `bdb_id` *and* `delay` (seconds) |

- **`SER_FI_CLUSTER_FAILURE=true` is a separate gate, and it ends the deployment.** `cluster_failure` stops
  the nodes you name and restores nothing, so naming them all leaves the cluster down for good - `rladmin`
  stops answering and the environment needs re-provisioning. Run it last or not at all.
- **Node-scoped actions need the *right* node.** `ClusterNodes.FindServingAsync` resolves the database
  hostname and matches it against `rladmin status nodes`, because killing an arbitrary node usually proves
  nothing: the deployment absorbs it and the client never notices. Node discovery goes through the injector
  rather than the cluster's REST API on 9443, which is not reachable from outside the deployment's network.
- **Only `proxy_failure` was visible to the client** (one `SocketClosed`, restored ~8s later). `shard_failure`
  on a replicated database and `node_failure` against a node we were not connected through both produced zero
  drops - the deployment absorbed them. That is worth knowing and is *not* coverage of our recovery path, so
  the test says so in its output when it sees no drops. Node 1 is avoided by default because cluster
  management usually lives there, and killing it takes the fault injector's own access with it.

## Active-Active

`ActiveActiveFailoverScenarioTests` is the one scenario here that is not about maintenance notifications. It
exercises client-side geographic failover (`ConnectionMultiplexer.ConnectGroupAsync`) across the member
databases of an Active-Active deployment, using `network_failure` to blackhole one member for a known
duration and asserting that the client failed over, stayed over for the whole outage, and failed back to the
highest-weight member.

It needs a `re-active-active` entry in `endpoints.json` whose `endpoints` array lists **two** member
databases - one per cluster - and skips with the count if it lists fewer. Two properties of that file are
worth knowing, because both silently produce the wrong answer if a reader is strict about them:

- **`bdb_id` arrives as a JSON *string* for the Active-Active entries** and as a number for every other entry
  in the same file. `ExistingDatabase` reads either; a strict `TryGetInt32` drops exactly those entries, and
  the resulting "this environment has no 're-active-active' database" reads like a template difference rather
  than a parser bug.
- **`endpoints` values may be bare `host:port`, with no scheme.** They are split on the last colon rather than
  parsed as a `Uri`, because a dotted hostname is itself a valid URI scheme - so `new Uri("db.example.com:1")`
  yields an empty host and no port instead of failing.

Three deliberate divergences from the rest of the tier:

- **Maintenance notifications are `Disabled`, not `Enabled`.** On a notification-capable build, letting the
  handoff machinery react to the same failover would set it racing the group's circuit breaker - two features
  measured at once, and an ambiguous failure.
- **Timeouts are 2s, not 15s.** A circuit breaker trips on observed failures, and a 15s command budget
  outlives a 15s outage, so nothing would fail and nothing would fail over.
- **Timings are asserted, not just logged** (failback no sooner than the outage length), and the workload is
  concurrent (`MultiThreadedWorkload`, 18 workers) because a failover is only observable under load.

One gap to be aware of when reading a failure: `/action` addresses a `bdb_id`, and in an Active-Active
topology that id exists on *both* clusters. `cluster_index` exists only on the scenario routes, so which copy
is blackholed is the injector's choice - it evidently picks the first, which is `Members[0]`. The test logs
every member's `host:port` and says so in the final assertion message, because a wrong-cluster injection
otherwise looks exactly like a failback that never happened.

## Three states, deliberately distinct

| state | behaviour |
|---|---|
| no `SER_FI_CONFIG_DIR` | every test **skips** - the ordinary case, including `build.ps1`'s full traversal |
| directory set, no `E2E_SCENARIO_TESTS=true` | every test **skips** - nobody should create databases by accident |
| configured and enabled, but broken | every test **fails** |

The third row is the important one. A suite that skips when the environment is broken reports success for tests
that never ran, and it will be trusted at exactly the wrong moment.

## Databases are created by the tests, not by you

Each *shape* (`DatabaseShape`) is a fixture shared by the classes that need it, because creating a database on a
real cluster is slow. The shapes exist because they change client behaviour rather than for coverage's sake: the
number of A records a hostname carries follows proxy placement, and the handoff takes a different branch
depending on whether a live sibling address exists.

Every database is named `sertest-<shape>-<runid>`. Cleanup is per fixture and unconditional; a sweep at startup
removes leaks from runs that were killed, matching on the `sertest-` prefix and nothing else, so it can never
touch a database created by hand.

## TLS

Certificates are self-signed per environment, so tests call `ConfigurationOptions.TrustIssuer(caPath)` with the
CA found in the config directory. If the CA is missing, TLS tests **fail** rather than disabling validation - a
TLS test that quietly stops checking identity reports success for the one thing it exists to catch.

## Traits

`tier=fault-injector` on everything, so the whole tier can be excluded in one filter; `scenario=<family>` for
subsets.

## Deliberately not here: remapping the main suite

`endpoints.json` describes standalone, TLS and `oss_cluster` databases as well as the Active-Active pair, so it
is tempting to use it as an override layer on the main test project's `TestConfig` - remapping `Primary*`,
`Replica*`, `Ssl*`, `Cluster*` and running the *whole* integration suite against Redis Enterprise. That is a
real goal and this file is the right source for it, but it is not attempted here, and one earlier attempt is
worth not repeating: it round-tripped through `ConfigurationOptions.ToString()`, which cannot carry certificate
trust, so it structurally could not serve the TLS entries in the very file it read. When it is wanted, read
`ExistingDatabase`s and hand over `ConfigurationOptions` *objects*. Note also that `TestConfig` exposes
host/port pairs assembled ad hoc by dozens of tests, so this is a per-property retrofit rather than one seam.

## Unverified

The `create_database` parameter names in `DatabaseShape.ToCreateParameters` are the injector's wire schema,
which is documented only as prose. They are gathered in one place so a real run can correct them; go-redis's
`DatabaseConfig` is the closest reference implementation. Treat them as unconfirmed until a run accepts them.
