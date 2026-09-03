# Introducing ServerMaintenanceEvents

StackExchange.Redis now automatically subscribes to notifications about upcoming maintenance from supported Redis providers. The ServerMaintenanceEvent on the ConnectionMultiplexer raises events in response to notifications about server maintenance, and application code can subscribe to the event to handle connection drops more gracefully during these maintenance operations.

There are two sources of these events, and they arrive by completely different routes:

* **Azure Cache for Redis** publishes them on a pub/sub channel (`AzureRedisEvents`), and they surface as `AzureMaintenanceEvent`. This is the original support, and is described below.
* **Redis Enterprise and Redis Cloud** send them as RESP3 *push frames* on the connection that carries your commands, and they surface as `PushMaintenanceEvent`. This is newer, does more than report, and is covered in [its own section](#server-native-maintenance-notifications-redis-enterprise-and-redis-cloud).

Both raise the same `ServerMaintenanceEvent` event, so a handler can watch for either.

If you are a Redis vendor and want to integrate support for ServerMaintenanceEvents into StackExchange.Redis, we recommend opening an issue so we can discuss the details.

## Types of events

Azure Cache for Redis currently sends the following notifications: 
* `NodeMaintenanceScheduled`: Indicates that a maintenance event is scheduled. Can be 10-15 minutes in advance. 
* `NodeMaintenanceStarting`: This event gets fired ~20s before maintenance begins
* `NodeMaintenanceStart`: This event gets fired when maintenance is imminent (<5s)
* `NodeMaintenanceFailoverComplete`: Indicates that a replica has been promoted to primary
* `NodeMaintenanceEnded`: Indicates that the node maintenance operation is over

## Sample code 

The library will automatically subscribe to the pub/sub channel to receive notifications from the server, if one exists. For Azure Redis caches, this is the 'AzureRedisEvents' channel. To plug in your maintenance handling logic, you can pass in an event handler via the `ServerMaintenanceEvent` event on your `ConnectionMultiplexer`. For example:

```csharp
multiplexer.ServerMaintenanceEvent += (object sender, ServerMaintenanceEvent e) =>
{
    if (e is AzureMaintenanceEvent azureEvent && azureEvent.NotificationType == AzureNotificationType.NodeMaintenanceStart)
    {
        // Take whatever action is appropriate for your application to handle the maintenance operation gracefully. 
        // This might mean writing a log entry, redirecting traffic away from the impacted Redis server, or
        // something entirely different.
    }
};
```
You can see the schema for the `AzureMaintenanceEvent` class [here](https://github.com/StackExchange/StackExchange.Redis/blob/main/src/StackExchange.Redis/Maintenance/AzureMaintenanceEvent.cs). Note that the library automatically sets the `ReceivedTimeUtc` timestamp when the event is received, so if you see in your logs that `ReceivedTimeUtc` is after `StartTimeUtc`, this may indicate that your connections are under high load.

## Walking through a sample maintenance event

1. App is connected to Redis and everything is working fine. 
2. Current Time: [16:21:39] -> `NodeMaintenanceScheduled` event is raised, with a `StartTimeUtc` of 16:35:57 (about 14 minutes from current time).
    * Note: the start time for this event is an approximation, because we will start getting ready for the update proactively and the node may become unavailable up to 3 minutes sooner. We recommend listening for `NodeMaintenanceStarting` and `NodeMaintenanceStart` for the highest level of accuracy (these are only likely to differ by a few seconds at most).
3. Current Time: [16:34:26] -> `NodeMaintenanceStarting` message is received, and `StartTimeUtc` is 16:34:46, about 20 seconds from the current time.
4. Current Time: [16:34:46] -> `NodeMaintenanceStart` message is received, so we know the node maintenance is about to happen. We break the circuit and stop sending new operations to the Redis connection. (Note: the appropriate action for your application may be different.) StackExchange.Redis will automatically refresh its view of the overall server topology.
5. Current Time: [16:34:47] -> The connection is closed by the Redis server.
6. Current Time: [16:34:56] -> `NodeMaintenanceFailoverComplete` message is received. This tells us that the replica node has promoted itself to primary, so the other node can go offline for maintenance.
7. Current Time [16:34:56] -> The connection to the Redis server is restored. It is safe to send commands again to the connection and all commands will succeed.
8. Current Time [16:37:48] -> `NodeMaintenanceEnded` message is received, with a `StartTimeUtc` of 16:37:48. Nothing to do here if you are talking to the load balancer endpoint (port 6380 or 6379). For clustered servers, you can resume sending readonly workloads to the replica(s).

##  Azure Cache for Redis Maintenance Event details

#### NodeMaintenanceScheduled event

`NodeMaintenanceScheduled` events are raised for maintenance scheduled by Azure, up to 15 minutes in advance. This event will not get fired for user-initiated reboots.

#### NodeMaintenanceStarting event

`NodeMaintenanceStarting` events are raised ~20 seconds ahead of upcoming maintenance. This means that one of the primary or replica nodes will be going down for maintenance.

It's important to understand that this does *not* mean downtime if you are using a Standard/Premier SKU cache. If the replica is targeted for maintenance, disruptions should be minimal. If the primary node is the one going down for maintenance, a failover will occur, which will close existing connections going through the load balancer port (6380/6379) or directly to the node (15000/15001). You may want to pause sending write commands until the replica node has assumed the primary role and the failover is complete.

#### NodeMaintenanceStart event

`NodeMaintenanceStart` events are raised when maintenance is imminent (within seconds). These messages do not include a `StartTimeUtc` because they are fired immediately before maintenance occurs.

#### NodeMaintenanceFailoverComplete event

`NodeMaintenanceFailoverComplete` events are raised when a replica has promoted itself to primary. These events do not include a `StartTimeUtc` because the action has already occurred.

#### NodeMaintenanceEnded event

`NodeMaintenanceEnded` events are raised to indicate that the maintenance operation has completed and that the replica is once again available. You do *NOT* need to wait for this event to use the load balancer endpoint, as it is available throughout. However, we included this for logging purposes and for customers who use the replica endpoint in clusters for read workloads.

# Server-native maintenance notifications (Redis Enterprise and Redis Cloud)

> These APIs are experimental, behind diagnostic id `SER010`; see [SER010](exp/SER010.md).

Redis Enterprise and Redis Cloud can tell a client *directly* that a disruption is coming: a shard is migrating, a node is failing over, or the endpoint you are connected to is being replaced. Unlike the Azure events above, these arrive as RESP3 push frames on the connection itself, and the client does not merely report them: it relaxes timeouts for the duration, re-reads the cluster topology when slots have moved, recovers sharded subscriptions that were stranded, and moves off an endpoint that is going away rather than waiting to be disconnected.

## Do I need to configure anything?

Usually not. If you connect using the hostname your provider gave you, the matching options provider recognizes it and turns the feature on for you.

| You connect to | Recognized as | Notifications |
|---|---|---|
| `something.cloud.redislabs.com`, `.cloud.redis.io`, `.redislabs.com` | Redis Cloud | on (`Auto`) |
| `something.redis.azure.net`, `.redisenterprise.cache.azure.net` | Azure Managed Redis | on (`Auto`) |
| your own hostname, a CNAME, private DNS, or through a proxy | nothing | **off** |
| a self-managed Redis Enterprise cluster | nothing (there is no DNS pattern to recognize) | **off** |

The last two rows are the ones to know about, because nothing fails: the connection works normally and you simply never receive a notification. If your endpoint does not look like your provider's, say so explicitly. Either:

```csharp
// the whole deployment posture: prefer RESP3, skip the OSS config-broadcast channel, and ask for notifications
var options = ConfigurationOptions.Parse("my-redis.internal.example.com:6379,defaults=enterprise");
```

or, to change nothing except this feature:

```csharp
var options = ConfigurationOptions.Parse("my-redis.internal.example.com:6379,maintNotifications=Auto");
```

`defaults=` accepts `rediscloud`, `enterprise`, `amr` and `azure`; see [Configuration](Configuration.md) for what each provider sets. It is also the right answer for a *hosted* deployment reached somewhere its own provider cannot see it, such as behind a CNAME or a private endpoint.

### RESP3 is required, and is already the default

These notifications are RESP3 push frames, so RESP3 is a hard requirement. You do not normally need to ask for it: with no protocol configured the client assumes a 6.0 server and negotiates RESP3, which is enough. But three settings take RESP3 away again, and each one silently disables this feature:

* `protocol=resp2` (or `Protocol = RedisProtocol.Resp2`)
* `defaultVersion` below 6.0, which is how the client decides RESP3 is available at all
* disabling or renaming `HELLO` in the [command map](Configuration.md), since RESP3 is negotiated by `HELLO`

If you use `maintNotifications=Enabled` (see below) you will find out about this immediately, because the connection will be refused rather than quietly running without the feature.

## Choosing a mode

```csharp
options.MaintenanceNotifications = MaintenanceNotificationMode.Auto;
```

| Mode | Meaning |
|---|---|
| `Disabled` | never ask. The default when nothing recognizes your endpoint |
| `Auto` | ask, and carry on if the server says no. What the providers select |
| `Enabled` | **require** them: if the server will not deliver them, or the connection ends up on RESP2, the connection is **rejected** |

`Auto` is the right choice almost always: asking costs one command during the handshake, and a server that accepts and then never sends anything costs nothing at all. `Enabled` exists for the case where running without advance warning is worse than not running: it turns a silent absence into a startup failure, which also makes it a useful way to prove the feature is live in a staging environment.

### Asking where to go next

When an endpoint is being replaced, the server can name its replacement - but only if asked. The client asks by
default (`maintMovingEndpointType=Auto`), working out the right form per connection:

| | connected address is private | otherwise |
|---|---|---|
| **without TLS** | `internal-ip` | `external-ip` |
| **with TLS** | `internal-fqdn` | `external-fqdn` |

The TLS split is about certificate validation: a certificate carrying DNS names cannot validate a bare address,
so an encrypted connection asks for names. Where there is no address to classify - a tunnel, a custom transport,
a Unix domain socket - the client asks for `none` rather than guessing, and falls back to reconnecting the way it
originally connected.

This matters more than it sounds. Without a named replacement, a handoff has to wait for DNS to be repointed,
and DNS has been measured trailing the notification by anywhere from 4 to 19 seconds while the socket closes at
about 16 to 19 seconds - so on a bad run the connection is gone before DNS is ready. With a named replacement
the client moves within a second and the server never has to close anything.

Override it if your deployment needs a specific form:

```
maintMovingEndpointType=ExternalFqdn
```

or `ServerDefault` to ask for nothing at all, which is what earlier versions did.

## What the client does without your involvement

| Notification | What the client does |
|---|---|
| `MIGRATING`, `FAILING_OVER`, `SMIGRATING` | relaxes command timeouts for that server while the disruption lasts |
| `MIGRATED`, `FAILED_OVER` | ends the window, but keeps timeouts relaxed for a short tail while things settle |
| `SMIGRATED` | as above, and re-reads the cluster topology, and re-subscribes any sharded channels whose slots moved |
| `MOVING` | works out the replacement address, lets in-flight work finish, then replaces the connections |

So an application that does nothing at all still benefits: commands that would have timed out during a migration are given more room, a moved slot is learned without waiting to be redirected, and a `MOVING` is acted on before the server closes the socket.

### Notifications that arrive as you connect

Redis Enterprise **retains the most recent completion** - `MIGRATED` or `FAILED_OVER` - and replays it to each connection that opts in, so a client that connects after a disruption still learns that it happened. Measured behaviour, worth knowing if you handle these events yourself:

* Only completions are replayed. Starters (`MIGRATING`, `FAILING_OVER`) are not, and neither is `MOVING` - so a replay can never demand that you move.
* One item, most-recent-replaces; there is no queue.
* It arrives within milliseconds of the opt-in being accepted, which is *during* connection establishment - so an event handler attached after `ConnectAsync` returns will usually not see it.
* **It can be very old.** The same `FAILED_OVER` was still being replayed to fresh connections **three hours** after the failover - the longest anybody has measured, and it had not expired then - and a completion carries no time field, so nothing in the notification says how old it is.

Because of that last point, a completion that arrives while the connection is still being established does **not** relax timeouts: it is history, not news. A completion that arrives on a live connection does, as the table above says. If you act on these events yourself, treat one that arrives at connection time as "this happened at some point", not "this is happening".

That applies to completions only. A *starter* arriving as you connect is not a replay - nothing retains those - so it still relaxes timeouts: it is the server telling a late-joining connection what is left of a disruption already in progress, which is when patience is most useful.

Note that a deliberate handoff appears as a `ConnectionFailed` event with `FailureType == ConnectionFailureType.MaintenanceHandoff`. That is expected during planned maintenance and does not indicate a fault; if you alert on `ConnectionFailed`, filter it out.

## Watching the events

```csharp
multiplexer.ServerMaintenanceEvent += (sender, e) =>
{
    if (e is PushMaintenanceEvent maintenance)
    {
        logger.LogInformation(
            "{Type} from {EndPoint} (seq {Sequence}, {Time})",
            maintenance.NotificationType, maintenance.EndPoint, maintenance.SequenceId, maintenance.Time);

        foreach (var migration in maintenance.SlotMigrations)
        {
            logger.LogInformation("slots {Slots}: {Source} -> {Target}", migration.RawSlots, migration.Source, migration.Target);
        }
    }
};
```

Two things are worth knowing before you build on the detail:

* **`EndPoint` is whichever node told us first.** Every node broadcasts a given event, so the client collapses the copies and raises one event; it is not necessarily the node being maintained, and for the cluster notifications it is usually a bystander reporting somebody else's movements.
* **`SequenceId` is observed behaviour, not a contract.** No specification defines it. In practice it is monotonic per database and shared across notification types, which makes it useful for spotting a replay, but do not depend on it across deployments or versions.

`Time` is what the server announced, and may legitimately be zero or negative for a connection that arrived mid-window, meaning "this is happening now".

## Timeouts during maintenance

Three settings control the relaxed window, all in seconds:

| Setting | Default | Meaning |
|---|---|---|
| `maintRelaxedTimeout` | 10s | the timeout to use while a disruption is in progress, and the floor for how long a window lasts |
| `maintRelaxedWindowMax` | 3x the relaxed timeout | the longest a single window may last, in case a closing notification never arrives |
| `maintPostEventRelaxed` | 2x the relaxed timeout | how long timeouts stay relaxed *after* the disruption ends |

The tail applies to a completion that arrives on a live connection. A completion replayed as you connect gets no tail at all, for the reasons above.

The announced duration is clamped rather than honoured literally. Windows as short as two seconds have been observed in practice, which is not long enough to cover a client reconnecting, and a client that trusted the announced value would stop being patient exactly when it mattered. The tail exists for the same reason in reverse: after a handoff, servers and other clients are still settling.

If a command does time out during a window, the exception carries the reason: `RedisTimeoutException.MaintenanceType` (and the same property on `RedisConnectionException`) names the notification that was in effect, which distinguishes "the deployment was moving" from "this query is slow". A window that closed very recently still counts, because timeouts are reported by a once-a-second sweep and the command had already been waiting for its whole timeout before that - so the window that caused a timeout is often over by the time you see the exception.

A handoff that does not get a replacement connection fully established before the announced window runs out is reported as a warning:

```
10.0.0.1:6379: Maintenance handoff did not establish a replacement within the announced 15000ms (interactive: False, subscription: True)
```

Worth watching for, because it is otherwise invisible: commands succeed either way, since the relaxed window covers the gap, so a handoff that took three times its budget looks exactly like one that worked.

## Checking that it is working

Wire up an `ILoggerFactory` and the client reports the outcome of the opt-in, per server:

```csharp
options.LoggerFactory = loggerFactory;
await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
```

```
10.0.0.1:6379: Requesting maintenance notifications (Auto)
10.0.0.1:6379: Maintenance notifications accepted
```

or, when the server declines, the reason it gave:

```
10.0.0.1:6379: Maintenance notifications refused (ERR maintenance notifications are disabled on this server)
```

Received notifications are logged too, which is the quickest way to answer "did anything actually arrive?" - and, when a new connection is unexpectedly patient about timeouts, "was that a replay?":

```
10.0.0.1:6379: Maintenance notification: FailingOver seq=41
10.0.0.1:6379: Maintenance notification: FailedOver seq=42
10.0.0.2:6379: Maintenance notification: FailedOver seq=42 (catch-up)
```

The last line is the retained copy described above, delivered to a connection that opted in afterwards.

A handoff is reported the same way, which is worth knowing because it replaces connections:

```
10.0.0.1:6379: Maintenance handoff: Recycle -> 10.0.0.2:6379: db.example.com now resolves to 10.0.0.2:6379
```

Alternatively set `MaintenanceNotifications = Enabled` in a test or staging environment: if anything prevents the feature working, including ending up on RESP2, the connection fails instead of running silently without it.

## Which deployments send these

Redis Enterprise and Redis Cloud send them, subject to the feature being enabled on the cluster. Azure Managed Redis is configured to ask for them ahead of its own rollout, so the setting is harmless until their servers begin emitting. Redis Open Source, Valkey and other servers do not send them at all, and the setting is simply inert there: the opt-in is refused and the client carries on.
