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
