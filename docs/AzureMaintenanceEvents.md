# Introducing ServerMaintenanceEvent

If you are running Azure Cache for Redis, SE.R now automatically subscribes to notfications about upcoming maintenance to the cache so that downtime/connection blips can be handled more gracefully on the client side.

## Types of events

The server currently sends the following notifications: 
* `NodeMaintenanceScheduled`: Indicates that a maintenance event is scheduled. Can be 10-15 minutes in advance. 
* `NodeMaintenanceStarting`: This event gets fired ~20s before maintenance begins
* `NodeMaintenanceStart`: This event gets fired when maintenance is imminent (<5s)
* `NodeMaintenanceEnded`: Indicates that the node maintenance operation is over
* `NodeMaintenanceFailoverComplete`: Indicates that a replica has been promoted to primary

## Sample code to listen to *AzureRedisEvents* 

The library will automatically subscribe to the AzureRedisEvents Pub/Sub channel. To plug in your maintenance response logic, you can pass in an event handler via the `ServerMaintenanceEvent` property on your `ConnectionMultiplexer`. 

```
multiplexer.ServerMaintenanceEvent += (object sender, ServerMaintenanceEvent e) =>
{
    if (e is AzureMaintenanceEvent && ((AzureMaintenanceEvent)e).NotificationType == AzureNotificationType.NodeMaintenanceStart)
    {
        // Break circuit because maintenance is imminent
    }
};
```
The server sends the message as a string in the form of key-value pairs delimited by pipes (|), which is then parsed into the `AzureMaintenanceEvent` class (you can look at the schema for the class [here](..\src\StackExchange.Redis\Maintenance\AzureMaintenanceEvent.cs).) The `ToString()` representation for Azure events also returns this format:
```
NotificationType|{NotificationType}|StartTimeInUTC|{StartTimeInUTC}|IsReplica|{IsReplica}|IPAddress|{IPAddress}|SSLPort|{SSLPort}|NonSSLPort|{NonSSLPort}
```
Note that the library automatically sets the `ReceivedTimeUtc` timestamp when the event is received. If you see in your logs that `ReceivedTimeUtc` is after `StartTimeUtc`, this may indicate that your connections are under high load. 

## Walking through a sample maintenance event

1. App is connected to Redis and everything is working fine. 

2. Current Time: [16:21:39] -> Message received through *AzureRedisEvents* channel. The message notification type is "NodeMaintenanceScheduled" and StartTimeInUTC is "16:35:57" (about 14 minutes from current time). So we wait. 
    ```
    NotificationType|NodeMaintenanceScheduled|IsReplica|False|IPAddress|52.158.249.185|SSLPort|15000|NonSSLPort|13000|StartTimeInUTC|2021-10-20T16:35:57
    ```
3. Current Time: [16:34:26] -> "NodeMaintenanceStarting" message is received, and StartTimeInUTC is "16:34:56". This is about 30 seconds from current time, although the node will in practice become unavailable in around 20 seconds. 
    ```
    NotificationType|NodeMaintenanceStarting|IsReplica|False|IPAddress|52.158.249.185|SSLPort|15000|NonSSLPort|13000|StartTimeInUTC|2021-10-20T16:34:56
    ```
4. Current Time: [16:34:46] -> "NodeMaintenanceStart" message is received, so we know the node maintenance is about to happen. We break the circuit and stop sending new operations to the Redis object. SE.R will automatically refresh its view of the cache's topology. 
    ```
    NotificationType|NodeMaintenanceStart|IsReplica|False|IPAddress|52.158.249.185|SSLPort|15000|NonSSLPort|13000
    ```
6. Current Time: [16:34:47] -> The Redis object is disconnected from the Redis server. You can listen to these [ConnectionDisconnected events](<https://stackexchange.github.io/StackExchange.Redis/Events>).

5. Current Time: [16:34:56] -> "NodeMaintenanceFailover" message is received. This tells us that the old replica has promoted itself to primary, in response to the maintenance happening on the other node.
    ```
    NotificationType|NodeMaintenanceFailover|IsReplica|False|IPAddress|52.158.249.185|SSLPort|15001|NonSSLPort|13001
    ```
5. Current Time [16:34:56] -> The Redis object is reconnected back to the Redis server (again, you can listen to the Reconnected event on your client). It is safe to send ops again to the Redis connection and all ops will succeed.

6. Current Time [16:37:48] -> "NodeMaintenanceEnded" message is received, with a StartTimeInUTC of "16:37:48". Nothing to do here if you are talking to 6380/6379. For clustered caches, you can start sending readonly workloads to the replica. 
    ```
    NotificationType|NodeMaintenanceEnded|IsReplica|True|IPAddress|52.158.249.185|SSLPort|15000|NonSSLPort|13000|StartTimeInUTC|2021-10-20T16:37:48
    ```

##  Maintenance Event details

### `NodeMaintenanceScheduled` message

*NodeMaintenanceScheduled* messages are sent for maintenance events scheduled by Azure, up to 15 minutes in advance. This event will not get fired for user-initiated reboots. 

### `NodeMaintenanceStarting` message

*NodeMaintenanceStarting* messages are published ~20 seconds ahead of upcoming maintenance - which usually means that one of the nodes (primary/replica) is going to be down for Standard/Premier Sku caches. 

It's important to understand that this does *not* mean downtime if you are using a Standard/Premier sku caches. Rather, it means there is going be a failover that will disconnect existing connections going through the LB port (6380/6379) or directly to the node (15000/15001) and operations might fail until these connections reconnect.

In the case of clustered nodes, you might have to stop sending read/write operations to this node until it comes back up and use the node which will have been promoted to primary. For basic sku only, this will mean complete downtime until the update finishes.

One of the things that can be done to reduce impact of connection blips would be to stop sending operations to the cache a second before the `StartTimeUtc` until the connection is restored which typically takes less than a second in most clients like StackExchange.Redis.

### `NodeMaintenanceStart` message

*NodeMaintenanceStart* messages are published when maintenance is imminent. These messages do not include a `StartTimeUtc` because they are fired immediately before maintenance occurs.

### `NodeMaintenanceEnded` message

This event is sent to indicate that the maintenance operation has completed. You do *NOT* need to wait for this message to use the LB endpoint. The LB endpoint is always available. However, we included this for logging purposes or for customers who use the replica endpoint in clusters for read workloads.

### `NodeMaintenanceFailover`/`NodeMaintenanceFailoverComplete` message

This message is sent to indicate that a replica has promoted itself to primary. These events do not include a `StartTimeUtc` because the action has already occurred. The library exposes this event as *NodeMaintenanceFailoverComplete*, but on the server side this event will continue to get sent as *NodeMaintenanceFailover* until November 2021. 