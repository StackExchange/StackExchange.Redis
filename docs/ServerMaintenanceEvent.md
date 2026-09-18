# Introducing ServerMaintenanceEvents

StackExchange.Redis now automatically subscribes to notifications about upcoming maintenance from supported Redis providers. The ServerMaintenanceEvent on the ConnectionMultiplexer raises events in response to notifications about server maintenance, and application code can subscribe to the event to handle connection drops more gracefully during these maintenance operations.

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