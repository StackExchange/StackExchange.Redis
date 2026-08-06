# Client-side geographic failover

> **What this is, and what it is called elsewhere.** This feature does one thing: it moves traffic between several independent endpoints that can each serve
> your workload, using health checks, circuit breakers and retries to decide when to move. Nothing in it is tied to a particular server topology, which is why
> the naming here is about the *behaviour* (failover between redundant endpoints) rather than about any specific deployment.
>
> The deployment it is **designed** for is **Active-Active** (geo-distributed, bidirectionally-replicated) Redis - Redis Software / Redis Cloud Active-Active
> databases, or any other topology where the same logical dataset is reachable through more than one independent endpoint. That is where failover is closest to
> transparent: because the members replicate to each other, an operation that lands on the new endpoint still sees what you wrote to the old one.
>
> It works just as well across endpoints that are *not* replicated to each other - separate OSS servers or clusters, instances in different regions with no link
> between them - provided you accept what that means for your data. With no replication between members, each endpoint holds only what was written to it: a
> failover moves you to a *different* dataset, writes made before the failover are not visible after it, and the two do not converge afterwards. That is
> perfectly reasonable for a cache you are content to re-populate, or where the data is reconstructible from a system of record; it is not reasonable if you are
> treating the group as a single durable store. Only you can make that call - the library has no way to know which situation it is in.
>
> The shape will be familiar if you have used the **"multi-DB client"** concept in other Redis client libraries: Jedis'
> [`MultiDbClient`](https://redis.io/docs/latest/develop/clients/jedis/failover/) and redis-py's
> [`MultiDBClient`](https://redis.io/docs/latest/develop/clients/redis-py/failover/) (see also Redis' overview of
> [client-side geographic failover for Active-Active](https://redis.io/blog/client-side-geographic-failover-for-redis-active-active/)) - a weighted set of
> endpoints, one active at a time, with health checks, circuit breakers and retries deciding when to move.

## Overview

Client-side geographic failover provides automatic failover, failback, and intelligent routing across multiple Redis deployments. It is built from several cooperating pieces:

1. **Connecting to multiple servers or regions at once**, giving a deployment redundant endpoints to fall back on.
2. **Health checks** that *actively* probe each endpoint on a timer to monitor its availability.
3. **Circuit breakers** that *passively* monitor availability from the observed success and failure of the traffic already flowing.
4. **Automatic retries** that let straightforward operations ride out a possibly-unstable connection — including silently transitioning to another endpoint during a failover, with no change to your calling code.

These features are available in the `Availability` sub-namespace:

``` csharp
using StackExchange.Redis;
using StackExchange.Redis.Availability;
```

### How the configuration types work

Every configurable piece in this namespace follows the same three-part shape, so once you have learned one you have learned all of them:

1. The **policy type** (`HealthCheck`, `CircuitBreaker`, `RetryPolicy`) is **immutable** and safe to share between members and connections. It exposes a static `Default` and a static `None`.
2. Each policy has a nested **`Builder`** carrying the mutable knobs. A new `Builder` already starts from the default values, so you only set what you want to change; `Create()` validates the values and returns the policy, and a `Builder` also converts *implicitly* to its policy, so it can be assigned or passed inline.
3. **`MultiGroupOptions`** (itself immutable, with its own `Builder`) holds the group-wide defaults; the matching nullable property on `ConnectionGroupMember` overrides them per member.

```csharp
// the same pattern, three times; each builder only mentions what differs from the default
HealthCheck healthCheck = new HealthCheck.Builder { ProbeCount = 5 };
CircuitBreaker breaker = new CircuitBreaker.Builder { FailureRateThreshold = 25 };
RetryPolicy retry = new RetryPolicy.Builder { MaxAttempts = 5 };

MultiGroupOptions options = new MultiGroupOptions.Builder
{
    HealthCheck = healthCheck,
    CircuitBreaker = breaker,
    RetryPolicy = retry,
    HealthCheckInterval = TimeSpan.FromSeconds(2),
};
```

Anything you leave out keeps its default, so a group that only wants a longer failback is just:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder { FailbackDelay = TimeSpan.FromMinutes(2) };
```

Because the policies are immutable, there is no question of whether a change "takes effect" after connecting: to change something, build a new instance. Values are validated once, in `Create()`, which throws `ArgumentOutOfRangeException`/`ArgumentException` naming the offending builder property - so a bad `ProbeCount` or `MaxAttempts` fails at the point you configure it, not later.

### Where each setting lives

| Setting | Group-wide default | Per-member override |
|---------|--------------------|---------------------|
| `HealthCheck` | `MultiGroupOptions.HealthCheck` | `ConnectionGroupMember.HealthCheck` |
| `CircuitBreaker` | `MultiGroupOptions.CircuitBreaker` | `ConnectionGroupMember.CircuitBreaker`, else that member's `ConfigurationOptions.CircuitBreaker` |
| `RetryPolicy` | `MultiGroupOptions.RetryPolicy` | *(none; retry is applied per-database via `WithRetry`)* |
| `HealthCheckInterval` | `MultiGroupOptions.HealthCheckInterval` | *(none; it is the group's re-evaluation cadence)* |
| `FailbackDelay` | `MultiGroupOptions.FailbackDelay` | `ConnectionGroupMember.FailbackDelay` |
| `Weight` | *(none)* | `ConnectionGroupMember.Weight` |
| `SkipInitialHealthCheck` | *(none)* | `ConnectionGroupMember.SkipInitialHealthCheck` |

Resolution is always "member override, else group default". Resolving a group default never writes back into your `ConfigurationOptions` - you can safely reuse one `ConfigurationOptions` for a group member and for an unrelated `Connect` without the group's policies leaking across.

`ConfigurationOptions` carries only the two availability settings that are meaningful for a **single** connection - `CircuitBreaker` and `RetryPolicy`. `HealthCheck` is a group-only concept and lives on `ConnectionGroupMember`.
The library automatically selects the best available endpoint based on:

1. **Availability** - Connected endpoints are always preferred over disconnected ones
2. **Weight** - User-defined preference values (higher is better)
3. **Latency** - Measured response times (lower is better)

This enables scenarios such as:
- Multi-datacenter deployments with automatic failover
- Geographic routing to the nearest Redis instance
- Graceful degradation during maintenance or outages
- Load distribution across multiple Redis clusters

## Basic Usage

### Connecting to Multiple Groups

To create a failover-capable connection, use `ConnectionMultiplexer.ConnectGroupAsync()` with an array of `ConnectionGroupMember` instances:

```csharp
// Define your Redis endpoints
ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East"),
    new("us-west.redis.example.com:6379", name: "US West"),
    new("eu-central.redis.example.com:6379", name: "EU Central")
];

// Connect to all members
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// Use the connection normally
var db = conn.GetDatabase();
await db.StringSetAsync("mykey", "myvalue");
var value = await db.StringGetAsync("mykey");
```

### Using ConfigurationOptions

You can also use `ConfigurationOptions` for more advanced configuration:

```csharp
var eastConfig = new ConfigurationOptions
{
    EndPoints = { "us-east-1.redis.example.com:6379", "us-east-2.redis.example.com:6379" },
    Password = "your-password",
    Ssl = true,
};

var westConfig = new ConfigurationOptions
{
    EndPoints = { "us-west-1.redis.example.com:6379", "us-west-2.redis.example.com:6379" },
    Password = "another-different-password",
    Ssl = true,
};

ConnectionGroupMember[] members = [
    new(eastConfig, name: "US East"),
    new(westConfig, name: "US West")
];

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
```

## Configuring Weights

Weights allow you to express preference for specific endpoints. Higher weights are preferred when multiple endpoints are available:

```csharp
ConnectionGroupMember[] members = [
    new("local-dc.redis.example.com:6379") { Weight = 10 },    // Strongly preferred
    new("nearby-dc.redis.example.com:6379") { Weight = 5 },    // Moderately preferred
    new("remote-dc.redis.example.com:6379") { Weight = 1 }     // Fallback option
];

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
```

Weights can be adjusted dynamically:

```csharp
// Adjust weight based on runtime conditions
members[0].Weight = 1;  // Reduce preference for local DC
members[2].Weight = 10; // Increase preference for remote DC
```

## Working with IDatabase

The `IDatabase` interface works transparently with connection groups. All operations are automatically routed to the currently selected endpoint:

```csharp
var db = conn.GetDatabase();

// String operations
await db.StringSetAsync("user:1:name", "Alice");
var name = await db.StringGetAsync("user:1:name");

// Hash operations
await db.HashSetAsync("user:1", new HashEntry[] {
    new("name", "Alice"),
    new("email", "alice@example.com")
});

// List operations
await db.ListRightPushAsync("queue:tasks", "task1");
var task = await db.ListLeftPopAsync("queue:tasks");

// Set operations
await db.SetAddAsync("tags", new RedisValue[] { "redis", "cache", "database" });
var members = await db.SetMembersAsync("tags");

// Sorted set operations
await db.SortedSetAddAsync("leaderboard", "player1", 100);
var rank = await db.SortedSetRankAsync("leaderboard", "player1");

// Transactions
var tran = db.CreateTransaction();
var t1 = tran.StringSetAsync("key1", "value1");
var t2 = tran.StringSetAsync("key2", "value2");
if (await tran.ExecuteAsync())
{
    await t1;
    await t2;
}

// Batches
var batch = db.CreateBatch();
var b1 = batch.StringSetAsync("key1", "value1");
var b2 = batch.StringSetAsync("key2", "value2");
batch.Execute();
await Task.WhenAll(b1, b2);
```

## Working with ISubscriber

Pub/Sub operations work across all connected endpoints. When you subscribe to a channel, the subscription is established against *all* endpoints (for immediate pickup
during failover events), and received messages are filtered in the library so only the messages for the *active* endpoint are observed. Message publishing
occurs only to the *active* endpoint. The effect of this is that pub/sub works transparently as though
you were only talking to the *active* endpoint:

```csharp
var subscriber = conn.GetSubscriber();

// Subscribe to a channel
await subscriber.SubscribeAsync(RedisChannel.Literal("notifications"), (channel, message) =>
{
    Console.WriteLine($"Received: {message}");
});

// Publish to a channel
await subscriber.PublishAsync(RedisChannel.Literal("notifications"), "Hello, World!");

// Pattern-based subscriptions
await subscriber.SubscribeAsync(RedisChannel.Pattern("events:*"), (channel, message) =>
{
    Console.WriteLine($"Event on {channel}: {message}");
});

// Unsubscribe
await subscriber.UnsubscribeAsync(RedisChannel.Literal("notifications"));
```

**Note:** When the active endpoint changes (due to failover), subscriptions are automatically re-established on the new endpoint.

## Monitoring Connection Changes

You can monitor when the active connection changes using the `ConnectionChanged` event:

```csharp
conn.ConnectionChanged += (sender, args) =>
{
    Console.WriteLine($"Connection changed: {args.Type}");
    Console.WriteLine($"Previous: {args.PreviousGroup?.Name ?? "(none)"}");
    Console.WriteLine($"Current: {args.Group.Name}");
};
```

## Monitoring Member Status

Each `ConnectionGroupMember` provides status information:

```csharp
foreach (var member in conn.GetMembers())
{
    Console.WriteLine($"{member.Name}:");
    Console.WriteLine($"  Connected: {member.IsConnected}");
    Console.WriteLine($"  Unhealthy: {member.IsUnhealthy}");
    Console.WriteLine($"  Weight: {member.Weight}");
    Console.WriteLine($"  Latency: {member.Latency}");
}
```

These are the same instances that were passed into `ConnectGroupAsync`.

## Health Checks

Configurable health checking monitors the health of all endpoints and automatically routes traffic away from unhealthy instances.

### Basic Health Check Configuration

Health checks are configured for all members via `MultiGroupOptions`, and can be overridden per member. Every knob is shown here for orientation, with **the value it already defaults to** - in real code you would set only the ones you want to change:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    ProbeCount = 3,                                 // Maximum number of probe attempts per check
    ProbeTimeout = TimeSpan.FromSeconds(3),         // Timeout for each probe attempt
    ProbeInterval = TimeSpan.FromMilliseconds(500), // Delay between failed probes
    Probe = HealthCheckProbe.Ping,                  // Which probe type to use
    ProbePolicy = HealthCheckProbePolicy.AllSuccess // Evaluation policy
};

MultiGroupOptions options = new MultiGroupOptions.Builder
{
    HealthCheck = healthCheck,
    HealthCheckInterval = TimeSpan.FromSeconds(5),  // How often checks run (a group-level concern)
};

ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 10 },
    new("us-west.redis.example.com:6379", name: "US West") { Weight = 5 }
];

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

Note that **how often** checks run is `MultiGroupOptions.HealthCheckInterval`, not a property of the check: it is the cadence at which the group re-evaluates *all* members, so a per-member value would be meaningless.

### Using Default Health Checks

If you don't specify a health check, the system uses sensible defaults:

```csharp
// Uses default health check settings
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// Equivalent to:
MultiGroupOptions options = new MultiGroupOptions.Builder { HealthCheck = HealthCheck.Default };
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

A new `Builder` starts from the defaults, so customizing means setting only what differs:

```csharp
HealthCheck customHealthCheck = new HealthCheck.Builder { ProbeCount = 5 }; // everything else defaulted

MultiGroupOptions options = new MultiGroupOptions.Builder
{
    HealthCheck = customHealthCheck,
    HealthCheckInterval = TimeSpan.FromSeconds(15),
};
```

There is also a `Builder(policy)` overload, for when you want to start from an instance that *isn't* the default - for example, adjusting a policy you were handed:

```csharp
// take the group's configured check and probe it harder for one member
HealthCheck stricter = new HealthCheck.Builder(conn.Options.HealthCheck) { ProbeCount = 9 };
```

### Health Check Properties

The `HealthCheck.Builder` class provides several configurable properties:

| Property | Default | Description |
|----------|---------|-------------|
| `ProbeCount` | 3 | Number of probe operations to perform per health check |
| `ProbeTimeout` | 3 seconds | Maximum time allowed for an individual probe to complete |
| `ProbeInterval` | 500 milliseconds | Delay between consecutive failed probes |
| `Probe` | `Ping` | The probe operation to execute |
| `ProbePolicy` | `AllSuccess` | Policy for evaluating multiple probe results |

### Per-member health checks, and turning them off

A member can use its own check - including `HealthCheck.None`, which performs no probes at all and reports `Inconclusive`, leaving that member's selection driven purely by its observed connectivity (and by its circuit-breaker):

```csharp
ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 10 },

    // a member we only want to reach for on connectivity grounds - never probe it
    new("archive.redis.example.com:6379", name: "Archive") { Weight = 1, HealthCheck = HealthCheck.None },

    // ...and one we want checked much more aggressively than the rest
    new("us-west.redis.example.com:6379", name: "US West")
    {
        Weight = 5,
        HealthCheck = new HealthCheck.Builder { ProbeCount = 5, ProbePolicy = HealthCheckProbePolicy.MajoritySuccess },
    },
];
```

To disable periodic checking for the whole group, set `MultiGroupOptions.HealthCheckInterval` to `TimeSpan.MaxValue`; the group is then only re-evaluated in response to connection events (such as a tripped circuit-breaker).

### Built-in Probes

StackExchange.Redis provides several built-in health check probes:

#### HealthCheckProbe.Ping

The simplest probe that executes a `PING` command against the server:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    Probe = HealthCheckProbe.Ping
};
```

This is the default and recommended probe for most scenarios as it's lightweight and tests basic connectivity.

#### HealthCheckProbe.IsConnected

Checks the connection status without sending any commands:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    Probe = HealthCheckProbe.IsConnected
};
```

This is even more lightweight than `Ping` but only verifies the socket connection, not Redis responsiveness.

#### HealthCheckProbe.StringSet

Performs a write operation to verify read/write capability:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    Probe = HealthCheckProbe.StringSet
};
```

This probe writes a random value to a health check key and verifies it can be retrieved. It's more comprehensive but has higher overhead than `Ping`. Note that this probe automatically skips replica servers.

### Health Check Policies

The probe policy determines how multiple probe results are evaluated to determine overall health:

#### HealthCheckProbePolicy.AnySuccess

The health check passes if **any** probe succeeds. This provides the most lenient evaluation:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.AnySuccess
};
// Healthy if 1 or more of 3 probes succeed
```

#### HealthCheckProbePolicy.AllSuccess (Default)

The health check passes only if **all** probes succeed. This provides the strictest evaluation:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.AllSuccess
};
// Healthy only if all 3 probes succeed
```

#### HealthCheckProbePolicy.MajoritySuccess

The health check passes if a **majority** of probes succeed:

```csharp
HealthCheck healthCheck = new HealthCheck.Builder
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.MajoritySuccess
};
// Healthy if 2 or more of 3 probes succeed
```

### Health Check Behavior

When a health check fails for a member:
- The member is flagged **unhealthy** (`IsUnhealthy`); this is distinct from `IsConnected` (see *Unhealthy State and Failback* below)
- Traffic is automatically routed to other healthy members based on weight and latency
- The system continues to perform health checks on the unhealthy member
- Once the member recovers and passes health checks, traffic automatically resumes (subject to `FailbackDelay`)

### Best Practices

1. **Choose appropriate probe types**: Use `Ping` for most scenarios; use `StringSet` when you need to verify write capability
2. **Balance probe frequency**: More frequent checks provide faster failover but increase load on your Redis servers
3. **Match policy to requirements**: Use `AnySuccess` for resilience, `AllSuccess` for strict validation, `MajoritySuccess` for balance
4. **Increase probe count for critical systems**: More probes with `MajoritySuccess` reduces false positives from transient failures
5. **Set reasonable timeouts**: Ensure `ProbeTimeout` accounts for network latency to your Redis servers
6. **Consider replica behavior**: Write-based probes automatically skip replicas to avoid false negatives

## Circuit Breakers

Where health checks *actively* probe each member on a timer, a **circuit breaker** works *passively*: it observes the outcome of the normal traffic already flowing over a connection, and tears that connection down as soon as it decides the connection has become unstable. The two are complementary:

- A **health check** answers *"is this member reachable?"* by sending its own probes every `Interval`.
- A **circuit breaker** answers *"is the traffic I'm already sending actually succeeding?"* with no extra round-trips, reacting the instant real commands start failing.

When a circuit breaker trips, it shuts the underlying physical connection down with `ConnectionFailureType.CircuitBreaker`. The connection then reconnects as usual, and - in a connection group - the health check and member-selection logic route traffic to other members until the affected member recovers. A circuit breaker is evaluated **per connection**, so
its state is scoped to exactly the connection whose health it is measuring; a reconnect starts from a clean slate - but breaking the connection is sufficient for the group to notice non-availability.

### Configuring a Circuit Breaker for a Group

Circuit breakers are configured for all members via `MultiGroupOptions`, alongside the health check. The setting flows into every member connection:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    CircuitBreaker = new CircuitBreaker.Builder { FailureRateThreshold = 25 }
};

ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 10 },
    new("us-west.redis.example.com:6379", name: "US West") { Weight = 5 }
];

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

If you don't specify one, `CircuitBreaker.Default` is used automatically:

```csharp
// these are equivalent
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

MultiGroupOptions options = new MultiGroupOptions.Builder { CircuitBreaker = CircuitBreaker.Default };
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

A circuit breaker is also useful *without* a group: set `ConfigurationOptions.CircuitBreaker` and any connection will tear itself down when its traffic starts failing. For a group member, the effective breaker is the member's `CircuitBreaker`, else the member's own `ConfigurationOptions.CircuitBreaker`, else the group default.

### Tuning the Default Circuit Breaker

The default circuit breaker uses a rolling time-window: it counts successes and failures over a short window and trips once the failure rate crosses a
threshold, provided enough failures have been seen to be statistically meaningful. Use `CircuitBreaker.Builder` to tune it:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    CircuitBreaker = new CircuitBreaker.Builder
    {
        FailureRateThreshold = 25,                       // trip above 25% failures
        MinimumNumberOfFailures = 100,                   // ...but only after 100 failures in the window
        MetricsWindowSize = TimeSpan.FromSeconds(5),     // rolling window to measure over
    }
};
```

A `Builder` converts implicitly to a `CircuitBreaker`, so it can be assigned directly as above; calling `.Create()` explicitly is equivalent.

| Property | Default | Description |
|----------|---------|-------------|
| `FailureRateThreshold` | 10 | Percentage of failures within the window that trips the breaker |
| `MinimumNumberOfFailures` | 1000 | Minimum tracked failures in the window before the breaker can trip (avoids acting on tiny samples) |
| `MetricsWindowSize` | 2 seconds | Rolling window over which successes and failures are counted |

Which faults count against the breaker is decided by *classification*, not by exception type: transient and connection-level errors (and timeouts) count as failures, while application-level errors — for example a `RedisServerException` for a bad command, or a `WRONGTYPE` — are treated as a success for circuit-breaking purposes, since they are not a sign of an unhealthy *connection*. This is derived from the fault's `RedisErrorKind`; to change what counts, override `IsFailure(in FaultContext)` on a custom accumulator (see *Custom circuit breakers* below).

### Disabling the Circuit Breaker

Use `CircuitBreaker.None` to opt out entirely:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    CircuitBreaker = CircuitBreaker.None
};
```

## Automatic Retries

Health checks and circuit breakers keep the *group* pointed at a healthy member; **automatic retries** deal with the *individual operation* that was in flight when something went wrong. Wrapping a database with `WithRetry(...)` returns a database that transparently re-issues failed operations according to a `RetryPolicy` — riding out transient faults, and (in a connection group) following a failover across to another member, without the caller having to catch-and-retry by hand.

```csharp
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// wrap the database once; reuse the wrapper like any other IDatabaseAsync.
// the parameterless overload uses the policy configured on the connection (see below)
IDatabaseAsync db = conn.GetDatabase().WithRetry();

// a transient fault (e.g. the active member briefly returning LOADING) is retried
// automatically; if the group fails over in the meantime, the retry lands on the new member
var value = await db.StringGetAsync("mykey");
```

> You can call `WithRetry` on any database (`IDatabase` or `IDatabaseAsync`), but the wrapper it returns exposes only the **async** API — there is no synchronous form, since retrying may inherently have delays. It cannot wrap a batch or an existing transaction, nor an already-retrying database.

> **`asyncState` is not respected.** A database's `asyncState` is stamped onto the task produced by a single dispatch, but a retrying database hands back its own task spanning however many attempts the operation takes, and the per-operation tasks from `retryDb.CreateTransaction()` are durable proxies that outlive any single attempt. Neither can carry it. Rather than dropping the state silently, both refuse it: `conn.GetDatabase(0, asyncState).WithRetry(policy)` throws `InvalidOperationException`, as does `retryDb.CreateTransaction(asyncState)`. Wrap a database obtained without an `asyncState` - note that a key-prefixed view of a state-carrying database is refused too, since it inherits the inner state.

A retrying database can still *create* a transaction: `retryDb.CreateTransaction()` returns an `ITransactionAsync` whose `ExecuteAsync` is retried as a single unit. Each attempt replays the queued operations (and any `WATCH` constraints) against a fresh `MULTI`/`EXEC` - and, in a connection group, onto whichever member is active at the time - so a transaction can ride out a failover just like a single command; the per-operation tasks handed back at build time resolve from the winning attempt. The retry-category gate (below) applies to the transaction as a whole, using the *most* side-effecting operation in it: a transaction containing an `INCR` counts as `CommandRetryWriteAccumulating`. Only the queued operations are considered, not the constraints, so an `AddCondition` guard does not make a transaction count as a conditional write however much it resembles one - supply the category explicitly if that matters to you. In practice that gate rarely blocks a transaction, because the faults that a transaction is most likely to hit - a `MULTI`/`EXEC` the server *rejected* wholesale - are known not to have applied anything, and the category is not consulted in that case (see [Known-not-applied faults](#known-not-applied-faults)).

#### Losing a `WATCH` race

A transaction with conditions can fail to commit in two different ways, and `Execute`/`ExecuteAsync` reports `false` for both. `ITransaction.WasWatchConflict` (also on `ITransactionAsync`, so it works for retrying transactions too) is what tells them apart:

- **a condition was not satisfied** - the transaction is abandoned electively, and no `EXEC` is ever sent. `WasWatchConflict` is `false`, and the offending `ConditionResult.WasSatisfied` is `false` too. Re-attempting is pointless: the value really was not what you asserted, so a replay would assert the same thing again and fail the same way.
- **another connection modified a watched key** - between this connection's conditions being evaluated and its `EXEC` arriving, some *other* client wrote to one of the keys being watched on behalf of those conditions, so the server refused the `EXEC`. `WasWatchConflict` is `true`, and `WasSatisfied` is `true` for every condition: your assertions were all correct, you just lost a race with a concurrent writer. This is contention, not a fault - nothing was applied and nothing is broken.

Note that only *other connections* can cause this. Your own writes on this connection cannot: everything you queue inside the transaction is applied atomically by the `EXEC` itself, after the watch has already been satisfied.

The second case is the one Redis's `WATCH`/`MULTI`/`EXEC` idiom expects you to retry, so a retrying transaction does exactly that, bounded by `MaxAttemptsOnWatchConflict` (default 3). Each re-attempt re-issues the constraints, so a transaction whose condition has genuinely stopped holding (because the concurrent writer left a value you were not expecting) converges on an ordinary elective abort rather than looping. Because it is contention rather than a fault it gets its own budget: `MaxAttempts` is untouched, `RetryDelay` is not applied (only `JitterMax`), no failover is attempted, and `MaxCommandRetryCategory` does not apply - nothing happened, so there is nothing to double-apply. Set `MaxAttemptsOnWatchConflict = 1` to restore the plain "report `false` and let me deal with it" behaviour. On a retrying transaction, `WasWatchConflict` describes the *final* attempt: `false` if it eventually committed, `true` if it ran out of attempts still losing the race.

In either case every per-operation task is cancelled, so `await`ing one throws `OperationCanceledException` rather than hanging.

How likely is this in practice? Much less so than in `redis-cli`-style usage, where a `WATCH` can sit open for as long as the application takes to think. SE.Redis buffers the whole transaction and sends the `WATCH`, the condition reads, the `MULTI`, the queued commands and the `EXEC` as a single dispatch on a multiplexed connection, so a competing writer has only the gap between the condition reads and the `EXEC` landing to squeeze into. Small - but not zero, and on a busy key it will happen.

### Configuring the retry policy

Like the health check and the circuit breaker, `RetryPolicy` can be set as a group-wide default - and the parameterless `WithRetry()` picks it up, so callers don't have to thread a policy through their code:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    RetryPolicy = new RetryPolicy.Builder { MaxAttempts = 5 },
};
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);

// uses MultiGroupOptions.RetryPolicy
IDatabaseAsync db = conn.GetDatabase().WithRetry();
```

The same works for a single connection via `ConfigurationOptions.RetryPolicy`. `WithRetry()` resolves in this order: `MultiGroupOptions.RetryPolicy` for a connection group, `ConfigurationOptions.RetryPolicy` for a single connection, else `RetryPolicy.Default`. Pass a policy explicitly - `WithRetry(policy)` - to override that for one database, and use `RetryPolicy.None` to get a wrapper that never retries.

### RetryPolicy settings

`RetryPolicy.Builder` controls how many times, how often, and how far an operation is retried:

| Property | Default | Description |
|----------|---------|-------------|
| `MaxAttempts` | 3 | Total attempts (including the first) before giving up |
| `MaxAttemptsBeforeFailover` | 1 | Attempts against the current member before a retry is allowed to wait for / move to a failover member (only meaningful for multi-group connections) |
| `RetryDelay` | 1 second | Delay between same-server retries |
| `JitterMax` | 0.5 seconds | Upper bound of the additional random delay added to each retry, to avoid stampedes |
| `FailoverDelay` | 5 seconds | Maximum time to wait for a failover, when a retry is gated on one happening |
| `MaxCommandRetryCategory` | `CommandRetryWriteLastWins` | The most side-effecting command category that will be retried (see below) |
| `MaxAttemptsOnWatchConflict` | 3 | Attempts allowed for a *conditional transaction* that keeps losing a `WATCH` race; separate from `MaxAttempts`, and `1` disables re-attempting |

```csharp
RetryPolicy policy = new RetryPolicy.Builder
{
    MaxAttempts = 5,
    RetryDelay = TimeSpan.FromMilliseconds(200),
    JitterMax = TimeSpan.FromMilliseconds(100),
};
IDatabaseAsync db = conn.GetDatabase().WithRetry(policy);
```

Only *transient* faults are retried — the same `RedisErrorKind`-based classification the default circuit breaker uses. An application-level error such as `WRONGTYPE`, or an unknown command, is not transient and is never retried, no matter what the policy says.

### Which operations are safe to retry

Retrying is not free of consequence: replaying `INCR` after an ambiguous failure could double-count, whereas replaying `GET` is harmless; `SET` is "last wins", so: *usally* fine. Every command therefore carries a **retry category** describing its side-effects, and a policy only retries commands at or below its `MaxCommandRetryCategory`.

For the built-in typed methods (`StringGet`, `StringSet`, `HashSet`, ...) the library assigns the appropriate category automatically, so retries "just work" within the default policy.

The category can depend on a command's *arguments*, not just its name, and the typed methods take that into account: a plain `SET` is an unconditional overwrite ("last wins"), whereas `SET ... IFEQ` (or `NX`/`XX`) is a conditional write, since a replay either has no effect or fails rather than replacing a value written by someone else. The same applies to the server commands that cover several distinct operations under one name, so `CONFIG GET` is not categorized as though it were `CONFIG SET`.

Commands issued through `Execute`/`ExecuteAsync` receive no such treatment, because their arguments are opaque to the library: they take the pessimistic whole-command default, and the caller should supply a category explicitly (see below).

Note that the category describes the effect on the *keyspace*, not on the reply. A `SET ... IFEQ` that is replayed after an ambiguous success reports "not set" even though the write was applied, and the `GET` operand on `SET` returns the value just written rather than the true prior value. The end state is still correct, but callers branching on the returned value should be aware of this.

#### Known-not-applied faults

The category prices the *ambiguity* of a replay, not the write itself. If we know the operation never took effect, re-issuing it is a first attempt rather than a repeat: it cannot double-apply anything, so the category is not consulted at all and even an `INCR` is retried under the default policy. Two things give us that certainty:

- **the client never wrote it** - the message was still waiting to be sent, or sitting in the backlog, when the connection failed.
- **the server explicitly rejected it because of its own state** - `LOADING`, `CLUSTERDOWN`, `MASTERDOWN`, `TRYAGAIN`, `MOVED`, `ASK`, `READONLY`, `MISCONF`, `NOREPLICAS`, `BUSY`, `max clients`. The server can only report these *before* running anything.

`FaultContext.NotApplied` exposes this to custom policies. It is deliberately conservative, and in particular a bare error reply is *not* enough on its own: a Lua script can fail part-way through having already written something, and it propagates the inner error (`WRONGTYPE`, `OOM`, ...) verbatim, so those kinds stay ambiguous. Timeouts, and connection loss after the request was sent, are always ambiguous - which is exactly where `MaxCommandRetryCategory` earns its keep.

An explicit `CommandRetryNever` is still an absolute veto, as is an operation with no category at all (see below): certainty about *whether* it ran does not tell us that re-running it is meaningful.

### Custom commands: `Execute` and `ScriptEvaluate`

The library cannot infer the side-effects of a command it doesn't recognise — and that includes arbitrary commands issued via `Execute`/`ExecuteAsync`, and Lua run via `ScriptEvaluate`/`ScriptEvaluateAsync` (whose effect depends entirely on the script). Such commands are therefore treated **pessimistically**: an uncategorised command defaults to `CommandRetryNever` and is *not* retried.

Note that `Execute`/`ExecuteAsync` do try to *parse* the command name first, so `Execute("get", key)` is recognised as `GET` and picks up that command's category (read-only) automatically; only genuinely unrecognised command names fall back to `CommandRetryNever`.

The categories, from safest to most dangerous, are:

| `CommandFlags` value | Meaning |
|----------------------|---------|
| `CommandRetryAlways` | Always safe to retry, regardless of connection/server state |
| `CommandRetryConnection` | Connection-level or safe metadata (e.g. `CLIENT SETNAME`, `CONFIG GET`) |
| `CommandRetryReadOnly` | Pure reads (e.g. `GET`) |
| `CommandRetryWriteChecked` | Conditional writes (e.g. `SETNX`, `SET ... IFEQ`) |
| `CommandRetryWriteLastWins` | Unconditional overwrite — last-writer-wins (e.g. `SET`) |
| `CommandRetryWriteAccumulating` | Cumulative writes where a retry can double-apply (e.g. `INCR`, `LPUSH`) |
| `CommandRetryServerAdmin` | Server administration (e.g. `CONFIG SET`) |
| `CommandRetryNever` | Never retry |


When possible when using ad-hoc commands or script, callers should supply the most appropriate `CommandRetry*` category in the command's `CommandFlags`:

```csharp
// an arbitrary read-only command: safe to retry
var result = await db.ExecuteAsync("LOLWUT", args: [], flags: CommandFlags.CommandRetryReadOnly);

// a Lua script that only reads: opt into retries
var value = await db.ScriptEvaluateAsync(
    "return redis.call('GET', KEYS[1])",
    keys: [key],
    flags: CommandFlags.CommandRetryReadOnly);
```

Choose the category honestly — it describes what a *replay* would do. If a retry could double-apply a side-effect, use `CommandRetryWriteAccumulating` (or leave it uncategorised) rather than claiming it's a read.
Conversely, if you want more-side-effecting operations retried across the board, raise the policy's `MaxCommandRetryCategory` instead of tagging each call.

## Unhealthy State and Failback

Each member tracks two *independent* pieces of state:

- **`IsConnected`** — the last observed connectivity of the underlying connection.
- **`IsUnhealthy`** — whether the member has been *disabled* by a failing health-check or a tripped circuit breaker.

A member is only eligible to be selected as the active member when it is **connected _and_ not unhealthy**. Separating the two lets a member that is technically reconnected still be held out of rotation until we're confident it is stable again — which is what `FailbackDelay` controls.

### How a member becomes unhealthy

- A **health check** returns `Unhealthy` for it.
- Its **circuit breaker** trips (`ConnectionFailureType.CircuitBreaker`). The failing member is flagged unhealthy immediately on the circuit-breaker fast-path, so traffic routes away without waiting for the next health-check tick.

Each time a member is (re)marked unhealthy, the time of that failure is recorded.

### How a member becomes healthy again

An unhealthy member is cleared in one of three ways:

1. **Automatically**, once it passes a health check *and* its most recent failure is older than `FailbackDelay` (see below).
2. **Explicitly**, by calling `member.ResetIsUnhealthy()`.
3. **Implicitly**, by calling `IConnectionGroup.TryFailoverTo(member)` — an explicit failover request always clears the target's unhealthy flag first, since the caller is deliberately asking for that member.

### `FailbackDelay`

`MultiGroupOptions.FailbackDelay` is the interval a member must remain healthy - measured from its *most recent* failure - before it is automatically returned to rotation:

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    FailbackDelay = TimeSpan.FromMinutes(2), // must be healthy for 2 minutes after its last failure
};
```

Individual members can override this via `ConnectionGroupMember.FailbackDelay`, for example to hold a known-flaky region out of rotation for longer than the rest:

```csharp
ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 10 },
    new("flaky.redis.example.com:6379", name: "Flaky") { Weight = 5, FailbackDelay = TimeSpan.FromMinutes(10) },
];
```

| Value | Behavior |
|-------|----------|
| `TimeSpan.Zero` (default) | Immediate failback — the member is eligible again as soon as a health check passes |
| any positive `TimeSpan` | The member must go `FailbackDelay` with no further failures before it is re-selected |
| `TimeSpan.MaxValue` | **Manual mode** — automatic failback is disabled; the member stays out of rotation until `ResetIsUnhealthy()` or `TryFailoverTo(...)` is called |

This guards against **flapping**: a member that is intermittently failing will keep pushing its "last failure" time forward, so it never satisfies the delay and stays out of rotation until it is genuinely stable.

> Implementation note: the failback check is pure tick math on the wall clock — the last-failure time and the cutoff (`UtcNow - FailbackDelay`) are both compared as raw `long` UTC ticks, so `DateTimeKind` never enters into it. (`DateTime.Ticks` and `TimeSpan.Ticks` share the same 100 ns unit.)

### Anti-flap tiebreak in selection

Member selection ranks candidates by connectivity, explicit override, weight, and latency. When two candidates are otherwise indistinguishable, selection prefers the member that is **already active**, rather than picking arbitrarily. 

## Manual Failover

In some scenarios, you may need to manually control which member is actively serving traffic, overriding the automatic selection based on weight and latency. The `TryFailoverTo` method allows you to explicitly switch to a specific member or restore automatic selection.

### Basic Failover to a Specific Member

```csharp
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// Get the members to find the one you want to fail over to
var groupMembers = conn.GetMembers();
var targetMember = groupMembers.FirstOrDefault(m => m.Name == "US West");

if (targetMember != null)
{
    // Attempt to fail over to the specified member
    bool success = conn.TryFailoverTo(targetMember);

    if (success)
    {
        Console.WriteLine($"Successfully failed over to {targetMember.Name}");
    }
    else
    {
        Console.WriteLine($"Failed to fail over to {targetMember.Name} (member may be disconnected)");
    }
}
```

### Restore Automatic Selection

To remove an explicit failover and return to automatic member selection based on weight and latency:

```csharp
// Pass null to remove the explicit failover
bool hadExplicitFailover = conn.TryFailoverTo(null);

if (hadExplicitFailover)
{
    Console.WriteLine("Removed explicit failover, now using automatic selection");
}
else
{
    Console.WriteLine("No explicit failover was active");
}
```

### Failover Behavior

The `TryFailoverTo` method has the following behavior:

- **Returns `true`**: The failover was successful and the specified member is now active (or an explicit override was successfully removed)
- **Returns `false`**: The failover failed because:
  - The member is not connected
  - The member is not part of this connection group

When an explicit failover is active:
- The specified member will be preferred for all traffic
- Weight and latency are ignored for member selection
- If the explicitly selected member becomes unavailable, the system automatically falls back to other connected members
- Health checks continue to run on all members

### Example: Maintenance Mode

This is particularly useful when performing maintenance on one region and you want to temporarily route all traffic to another:

```csharp
var members = new ConnectionGroupMember[]
{
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 100 },
    new("us-west.redis.example.com:6379", name: "US West") { Weight = 100 }
};

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// During maintenance on US East, explicitly route to US West
var westMember = conn.GetMembers().First(m => m.Name == "US West");
if (conn.TryFailoverTo(westMember))
{
    Console.WriteLine("Traffic now routed to US West for maintenance");
}

// ... perform maintenance on US East ...

// After maintenance, restore automatic selection
if (conn.TryFailoverTo(null))
{
    Console.WriteLine("Maintenance complete, automatic selection restored");
}
```

### Example: Monitoring Failover Events

You can monitor when failovers occur using the `ConnectionChanged` event:

```csharp
conn.ConnectionChanged += (sender, args) =>
{
    if (args.Type == GroupConnectionChangedEventArgs.ChangeType.ActiveChanged)
    {
        Console.WriteLine($"Active member changed from {args.PreviousGroup?.Name ?? "none"} to {args.Group.Name}");
    }
};

// Trigger an explicit failover
var member = conn.GetMembers().First(m => m.Name == "Backup");
conn.TryFailoverTo(member);
// Event will fire: "Active member changed from Primary to Backup"
```

### Important Notes

1. **Connection Required**: You can only fail over to a member that is currently connected (`IsConnected == true`)
2. **Temporary Override**: The explicit failover persists until:
   - You call `TryFailoverTo(null)` to remove it
   - The connection group is disposed
   - The explicitly selected member becomes disconnected (automatic fallback occurs)
3. **Not Persistent**: Explicit failovers are not persisted across application restarts
4. **Thread-Safe**: `TryFailoverTo` is thread-safe and can be called concurrently with normal operations

## Dynamic Member Management

You can add or remove members dynamically using the `IConnectionGroup` interface:

```csharp
// Cast to IConnectionGroup to access dynamic member management
var group = (IConnectionGroup)conn;

// Add a new member at runtime
var newMember = new ConnectionGroupMember("new-dc.redis.example.com:6379", name: "New Datacenter")
{
    Weight = 5
};
await group.AddAsync(newMember);
Console.WriteLine($"Added {newMember.Name} to the group");

// Remove a member
var memberToRemove = members[2]; // Reference to an existing member
if (group.Remove(memberToRemove))
{
    Console.WriteLine($"Removed {memberToRemove.Name} from the group");
}
else
{
    Console.WriteLine($"Failed to remove {memberToRemove.Name} - member not found");
}

// Check current members
var currentMembers = group.GetMembers();
Console.WriteLine($"Current member count: {currentMembers.Length}");
foreach (var member in currentMembers)
{
    Console.WriteLine($"  - {member.Name} (Weight: {member.Weight}, Connected: {member.IsConnected})");
}
```

### Adding Members During Maintenance

Add a new datacenter before removing an old one for zero-downtime migrations:

```csharp
var group = (IConnectionGroup)conn;

// Add the new datacenter
var newDC = new ConnectionGroupMember("new-location.redis.example.com:6379", name: "New Location")
{
    Weight = 10 // High weight to prefer the new location
};
await group.AddAsync(newDC);

// Wait for the new member to be fully connected and healthy
await Task.Delay(TimeSpan.FromSeconds(5));

if (newDC.IsConnected)
{
    Console.WriteLine("New datacenter is online and healthy");

    // Reduce weight of old datacenter
    var oldDC = members[0];
    oldDC.Weight = 1;

    // Wait for traffic to shift
    await Task.Delay(TimeSpan.FromSeconds(10));

    // Remove the old datacenter
    if (group.Remove(oldDC))
    {
        Console.WriteLine("Old datacenter removed successfully");
    }
}
```

## Advanced Customization

The building blocks above cover the common cases. The extension points below let you replace the default health-check, retry, and circuit-breaker behavior with your own logic; most applications will not need them.

### Custom Health Check Probes

You can implement custom health check logic by extending `HealthCheckProbe`. Note that care must be used
if the probe involves talking to data via a `RedisKey`, as on "cluster" configurations, it must be ensured that the
key used resolves to the correct server; for this purpose, the `server.InventKey` method can be used:

A probe receives a `HealthCheckContext`, carrying the `Server` being probed and the `ProbeTimeout` budget:

```csharp
public abstract class CustomProbe : HealthCheckProbe
{
    public override Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
    {
        // create a random key that routes to the correct server, using
        // the specified prefix
        RedisKey key = context.Server.InventKey("health-check/");
        // ...
    }
}
```

Or more conveniently, the key-specific `KeyWriteHealthCheckProbe` encapsulates this logic: 

```csharp
public class CustomWriteProbe : KeyWriteHealthCheckProbe
{
    public override async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        IDatabaseAsync database,
        RedisKey key)
    {
        try
        {
            var value = Guid.NewGuid().ToString();
            await database.StringSetAsync(key, value, expiry: context.ProbeTimeout);
            bool isMatch = value == await database.StringGetAsync(key);

            return isMatch ? HealthCheckResult.Healthy : HealthCheckResult.Unhealthy;
        }
        catch
        {
            return HealthCheckResult.Unhealthy;
        }
    }
}
```

> The context is passed **by value** rather than by `in`, because probes are typically `async`, and async methods cannot take by-ref parameters. The sibling `HealthCheckProbePolicy.Evaluate` is synchronous, so it does take `in HealthCheckProbeContext`.

### Custom Probe Policies

In addition to the inbuilt policies, custom policies can be implemented by extending `HealthCheckProbePolicy`.
By checking the properties of the `HealthCheckProbeContext` parameter, your policy can make a determination
about the health of the server - returning `HealthCheckResult.Healthy` or `HealthCheckResult.Unhealthy` as
appropriate. If you return `HealthCheckResult.Inconclusive`, the health check will continue with additional probes.

#### Example: Require at Least N Successes

This example demonstrates a policy that requires at least a specified number of successful probes before declaring the endpoint healthy:

```csharp
public class AtLeastPolicy(int requiredSuccesses) : HealthCheckProbePolicy
{
    public override HealthCheckResult Evaluate(in HealthCheckProbeContext context)
    {
        // Success if we have at least the required number of successful probes
        if (context.Success >= requiredSuccesses) return HealthCheckResult.Healthy;

        // If no more probes remaining, we haven't met our threshold; otherwise: keep trying
        return context.Remaining == 0 ? HealthCheckResult.Unhealthy : HealthCheckResult.Inconclusive;
    }
}

// Use the custom policy requiring at least 2 successes
HealthCheck healthCheck = new HealthCheck.Builder
{
    ProbeCount = 5,  // Need enough probes to allow for the required successes
    ProbePolicy = new AtLeastPolicy(2)
};

MultiGroupOptions options = new MultiGroupOptions.Builder
{
    HealthCheck = healthCheck
};
```

This policy ensures that transient successes don't immediately mark an endpoint as healthy. It requires at least the specified number of successful probes, which provides better confidence in the endpoint's stability while still being more lenient than `AllSuccess`.

### Custom Circuit Breakers

> This is an advanced extension point; most applications should use `CircuitBreaker.Default` (optionally tuned via `CircuitBreaker.Builder`) or `CircuitBreaker.None`.

You can implement your own policy by extending `CircuitBreaker` and its `Accumulator`. `CreateAccumulator()` is called once per underlying connection, so each accumulator holds the state for a single connection.
For every completed message the connection calls `ObserveResult`, passing a `FaultContext` that describes the outcome: `IsFault` indicates whether a fault occurred, with the associated `Fault`, `ErrorKind` and `ConnectionFailureType` available for inspection. `IsHealthy()` is consulted separately: return `true` while the connection should be considered **healthy**, or `false` to **trip** the breaker and tear the connection down.

By default only faults that `IsFailure` regards as genuine failures reach `ObserveResult` *as* failures (transient/connection errors, timeouts, and similar - classified from the fault's `ErrorKind`); everything else - including application-level errors such as a bad command - is passed as a success. Override `IsFailure(in FaultContext)` if you need different rules for what counts:

```csharp
public sealed class ConsecutiveFailureBreaker(int limit) : CircuitBreaker
{
    public override Accumulator CreateAccumulator() => new Acc(limit);

    private sealed class Acc(int limit) : Accumulator
    {
        private int _consecutiveFailures;

        // a fault is present iff context.IsFault; a success resets the run
        public override void ObserveResult(in FaultContext context)
        {
            if (context.IsFault)
            {
                _consecutiveFailures++;
            }
            else
            {
                _consecutiveFailures = 0;
            }
        }

        // healthy until we hit the configured run of consecutive failures
        public override bool IsHealthy() => _consecutiveFailures < limit;

        // called if the connection wants to discard accumulated history
        public override void Reset() => _consecutiveFailures = 0;

        // (optional) override to change what counts as a failure; the default classifies from ErrorKind
        // protected override bool IsFailure(in FaultContext fault) => fault.IsFault;
    }
}

MultiGroupOptions options = new MultiGroupOptions.Builder
{
    CircuitBreaker = new ConsecutiveFailureBreaker(limit: 5)
};
```

Keep `ObserveResult` cheap and thread-safe: it runs on the hot path for every completed message, and may be called concurrently.

### Custom Retry Policies

`RetryPolicy` is itself extensible: override `CanRetry(in FaultContext fault)` to make the retry decision yourself. It returns a `RetryResult` - `None` to give up, or a combination of `SameServer` and `FailoverServer` to indicate where a retry may be attempted.
The `FaultContext` gives you the classified `ErrorKind`, the `ConnectionFailureType`, and the command `Flags` (including its retry category) to base the decision on:

```csharp
public sealed class ReadOnlyOnlyRetryPolicy : RetryPolicy
{
    public override RetryResult CanRetry(in FaultContext fault)
    {
        // only ever retry pure reads, and only on the same server
        if (fault.ErrorKind == RedisErrorKind.Loading
            && (fault.Flags & CommandFlags.CommandRetryReadOnly) != 0)
        {
            return RetryResult.SameServer;
        }
        // note: base.CanRetry(fault) would apply the default logic
        return RetryResult.None;
    }
}

IDatabaseAsync db = conn.GetDatabase().WithRetry(new ReadOnlyOnlyRetryPolicy());
```

A derived policy inherits the default settings; to derive *and* change the settings, take a `Builder` and pass it to the base constructor:

```csharp
public sealed class ReadOnlyOnlyRetryPolicy(RetryPolicy.Builder builder) : RetryPolicy(builder)
{
    public override RetryResult CanRetry(in FaultContext fault) => /* ... */;
}

IDatabaseAsync db = conn.GetDatabase().WithRetry(
    new ReadOnlyOnlyRetryPolicy(new RetryPolicy.Builder { MaxAttempts = 5 }));
```
