# Active:Active

## Overview

The Active:Active feature provides automatic failover and intelligent routing across multiple Redis deployments. The library
automatically selects the best available endpoint based on:

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

To create an Active:Active connection, use `ConnectionMultiplexer.ConnectGroupAsync()` with an array of `ConnectionGroupMember` instances:

```csharp
using StackExchange.Redis;

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

The `IDatabase` interface works transparently with Active:Active connections. All operations are automatically routed to the currently selected endpoint:

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
    Console.WriteLine($"  Weight: {member.Weight}");
    Console.WriteLine($"  Latency: {member.Latency}");
}
```

These are the same instances that were passed into `ConnectGroupAsync`.

## Health Checks

The Active:Active feature includes configurable health checking to monitor the health of all endpoints and automatically route traffic away from unhealthy instances.

### Basic Health Check Configuration

Health checks are configured globally for all members using the `MultiGroupOptions` parameter:

```csharp
var healthCheck = new HealthCheck
{
    Interval = TimeSpan.FromSeconds(10),      // How often to check health
    ProbeCount = 3,                           // Maximum number of probe attempts per check
    ProbeTimeout = TimeSpan.FromSeconds(2),   // Timeout for each probe attempt
    ProbeInterval = TimeSpan.FromSeconds(1),  // Delay between failed probes
    Probe = HealthCheckProbe.Ping,            // Which probe type to use
    ProbePolicy = HealthCheckProbePolicy.AnySuccess // Evaluation policy
};

var options = new MultiGroupOptions
{
    DefaultHealthCheck = healthCheck
};

ConnectionGroupMember[] members = [
    new("us-east.redis.example.com:6379", name: "US East") { Weight = 10 },
    new("us-west.redis.example.com:6379", name: "US West") { Weight = 5 }
];

await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

### Using Default Health Checks

If you don't specify a health check, the system uses sensible defaults:

```csharp
// Uses default health check settings
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);

// Equivalent to:
var options = new MultiGroupOptions
{
    DefaultHealthCheck = HealthCheck.Default
};
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

You can also clone and customize the default:

```csharp
var customHealthCheck = HealthCheck.Default.Clone();
customHealthCheck.Interval = TimeSpan.FromSeconds(5);
customHealthCheck.ProbeCount = 5;

var options = new MultiGroupOptions
{
    DefaultHealthCheck = customHealthCheck
};
```

### Health Check Properties

The `HealthCheck` class provides several configurable properties:

| Property | Default | Description |
|----------|---------|-------------|
| `Interval` | 10 seconds | How frequently health checks are performed |
| `ProbeCount` | 3 | Number of probe operations to perform per health check |
| `ProbeTimeout` | 2 seconds | Maximum time allowed for an individual probe to complete |
| `ProbeInterval` | 1 second | Delay between consecutive failed probes |
| `Probe` | `Ping` | The probe operation to execute |
| `ProbePolicy` | `AnySuccess` | Policy for evaluating multiple probe results |

### Built-in Probes

StackExchange.Redis provides several built-in health check probes:

#### HealthCheckProbe.Ping

The simplest probe that executes a `PING` command against the server:

```csharp
var healthCheck = new HealthCheck
{
    Probe = HealthCheckProbe.Ping
};
```

This is the default and recommended probe for most scenarios as it's lightweight and tests basic connectivity.

#### HealthCheckProbe.IsConnected

Checks the connection status without sending any commands:

```csharp
var healthCheck = new HealthCheck
{
    Probe = HealthCheckProbe.IsConnected
};
```

This is even more lightweight than `Ping` but only verifies the socket connection, not Redis responsiveness.

#### HealthCheckProbe.StringSet

Performs a write operation to verify read/write capability:

```csharp
var healthCheck = new HealthCheck
{
    Probe = HealthCheckProbe.StringSet
};
```

This probe writes a random value to a health check key and verifies it can be retrieved. It's more comprehensive but has higher overhead than `Ping`. Note that this probe automatically skips replica servers.

### Health Check Policies

The probe policy determines how multiple probe results are evaluated to determine overall health:

#### HealthCheckProbePolicy.AnySuccess (Default)

The health check passes if **any** probe succeeds. This provides the most lenient evaluation:

```csharp
var healthCheck = new HealthCheck
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.AnySuccess
};
// Healthy if 1 or more of 3 probes succeed
```

#### HealthCheckProbePolicy.AllSuccess

The health check passes only if **all** probes succeed. This provides the strictest evaluation:

```csharp
var healthCheck = new HealthCheck
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.AllSuccess
};
// Healthy only if all 3 probes succeed
```

#### HealthCheckProbePolicy.MajoritySuccess

The health check passes if a **majority** of probes succeed:

```csharp
var healthCheck = new HealthCheck
{
    ProbeCount = 3,
    ProbePolicy = HealthCheckProbePolicy.MajoritySuccess
};
// Healthy if 2 or more of  probes succeed
```

### Custom Health Check Probes

You can implement custom health check logic by extending `HealthCheckProbe`. Note that care must be used
if the probe involves talking to data via a `RedisKey`, as on "cluster" configurations, it must be ensured that the
key used resolves to the correct server; for this purpose, the `server.InventKey` method can be used:

```csharp
public abstract class CustomProbe : HealthCheckProbe
{
    public override Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IServer server)
    {
        // create a random key that routes to the correct server, using
        // the specified prefix
        RedisKey key = server.InventKey("health-check/");
        // ...
    }
}
````

Or more conveniently, the key-specific `KeyWriteHealthCheckProbe` encapsulates this logic: 

```csharp
public class CustomWriteProbe : KeyWriteHealthCheckProbe
{
    public override async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheck healthCheck,
        IDatabaseAsync database,
        RedisKey key)
    {
        try
        {
            var value = Guid.NewGuid().ToString();
            await database.StringSetAsync(key, value, expiry: healthCheck.ProbeTimeout);
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
var healthCheck = new HealthCheck
{
    ProbeCount = 5,  // Need enough probes to allow for the required successes
    ProbePolicy = new AtLeastPolicy(2)
};

var options = new MultiGroupOptions
{
    DefaultHealthCheck = healthCheck
};
```

This policy ensures that transient successes don't immediately mark an endpoint as healthy. It requires at least the specified number of successful probes, which provides better confidence in the endpoint's stability while still being more lenient than `AllSuccess`.

### Health Check Behavior

When a health check fails for a member:
- The member's `IsConnected` property reflects the unhealthy state
- Traffic is automatically routed to other healthy members based on weight and latency
- The system continues to perform health checks on the unhealthy member
- Once the member recovers and passes health checks, traffic automatically resumes

### Best Practices

1. **Choose appropriate probe types**: Use `Ping` for most scenarios; use `StringSet` when you need to verify write capability
2. **Balance probe frequency**: More frequent checks provide faster failover but increase load on your Redis servers
3. **Match policy to requirements**: Use `AnySuccess` for resilience, `AllSuccess` for strict validation, `MajoritySuccess` for balance
4. **Increase probe count for critical systems**: More probes with `MajoritySuccess` reduces false positives from transient failures
5. **Set reasonable timeouts**: Ensure `ProbeTimeout` accounts for network latency to your Redis servers
6. **Consider replica behavior**: Write-based probes automatically skip replicas to avoid false negatives

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
