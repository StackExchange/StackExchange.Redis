# Introducing ServerMaintenanceEvents

StackExchange.Redis now automatically subscribes to notifications about upcoming maintenance from Redis providers. The ServerMaintenanceEvent on the ConnectionMultiplexer raises events in response to server maintenance notifications, and application code can subscribe to the event to handle connection drops more gracefully during these maintenance operations.

## Types of events

Azure Cache for Redis currently sends the following notifications: 
* `NodeMaintenanceScheduled`: Indicates that a maintenance event is scheduled. Can be 10-15 minutes in advance. 
* `NodeMaintenanceStarting`: This event gets fired ~20s before maintenance begins
* `NodeMaintenanceStart`: This event gets fired when maintenance is imminent (<5s)
* `NodeMaintenanceFailoverComplete`: Indicates that a replica has been promoted to primary
* `NodeMaintenanceEnded`: Indicates that the node maintenance operation is over

## Sample code 

The library will automatically subscribe to the pub/sub channel to receive notifications from the server, if one exists. For Azure Redis caches, this is the 'AzureRedisEvents' channel. To plug in your maintenance handling logic, you can pass in an event handler via the `ServerMaintenanceEvent` event on your `ConnectionMultiplexer`. For example:

```
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
You can see the schema for the `AzureMaintenanceEvent` class [here](..\src\StackExchange.Redis\Maintenance\AzureMaintenanceEvent.cs). Note that the library automatically sets the `ReceivedTimeUtc` timestamp when the event is received, so if you see in your logs that `ReceivedTimeUtc` is after `StartTimeUtc`, this may indicate that your connections are under high load.

## Walking through a sample maintenance event

1. App is connected to Redis and everything is working fine. 

2. Current Time: [16:21:39] -> "NodeMaintenanceScheduled" event is raised, with a StartTimeUtc of "16:35:57" (about 14 minutes from current time).
3. Current Time: [16:34:26] -> "NodeMaintenanceStarting" message is received, and StartTimeUtc is "16:34:56". This start time is about 30 seconds from current time, although the node will in practice become unavailable in around 20 seconds.
4. Current Time: [16:34:46] -> "NodeMaintenanceStart" message is received, so we know the node maintenance is about to happen. We break the circuit and stop sending new operations to the Redis connection. (Note: the appropriate action for your application may be different.) For clustered Redis servers, StackExchange.Redis will automatically refresh its view of the server's topology.
5. Current Time: [16:34:47] -> The Redis connection is disconnected from the server.
6. Current Time: [16:34:56] -> "NodeMaintenanceFailover" message is received. This tells us that the replica node has promoted itself to primary, so the other node can go offline for maintenance.
7. Current Time [16:34:56] -> The Redis connection is reconnected back to the Redis server. It is safe to send commands again to the Redis connection and all commands will succeed.
8. Current Time [16:37:48] -> "NodeMaintenanceEnded" message is received, with a StartTimeUtc of "16:37:48". Nothing to do here if you are talking to the load balancer endpoint (6380/6379). For clustered servers, you can start sending readonly workloads to the replica.

##  Maintenance Event details

### `NodeMaintenanceScheduled` message

*NodeMaintenanceScheduled* messages are sent for maintenance events scheduled by Azure, up to 15 minutes in advance. This event will not get fired for user-initiated reboots. 

### `NodeMaintenanceStarting` message

*NodeMaintenanceStarting* messages are published ~20 seconds ahead of upcoming maintenance - which usually means that one of the nodes (primary/replica) is going to be down for Azure Standard/Premier Sku caches.

It's important to understand that this does *not* mean downtime if you are using an Standard/Premier sku cache. Rather, it means there is going be a failover that will disconnect existing connections going through the load balancer port (6380/6379) or directly to the node (15000/15001) and operations might fail until these connections reconnect.

In the case of clustered servers, you might have to stop sending read/write operations to this node until it comes back up and use the node which will have been promoted to primary. Node maintenance only means complete downtime for the duration of the update for single-node servers (e.g. Azure basic sku).

### `NodeMaintenanceStart` message

*NodeMaintenanceStart* messages are published when maintenance is imminent. These messages do not include a `StartTimeUtc` because they are fired immediately before maintenance occurs.

### `NodeMaintenanceFailover`/`NodeMaintenanceFailoverComplete` message

This message is sent to indicate that a replica has promoted itself to primary. These events do not include a `StartTimeUtc` because the action has already occurred. The library exposes this event as *NodeMaintenanceFailoverComplete*, but on the server side this event will continue to get sent as *NodeMaintenanceFailover* until November 2021.

### `NodeMaintenanceEnded` message

This event is sent to indicate that the maintenance operation has completed. You do *NOT* need to wait for this message to use the load balancer endpoint. This endpoint is always available. However, we included this for logging purposes or for customers who use the replica endpoint in clusters for read workloads.