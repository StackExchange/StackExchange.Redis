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

## Unverified

The `create_database` parameter names in `DatabaseShape.ToCreateParameters` are the injector's wire schema,
which is documented only as prose. They are gathered in one place so a real run can correct them; go-redis's
`DatabaseConfig` is the closest reference implementation. Treat them as unconfirmed until a run accepts them.
