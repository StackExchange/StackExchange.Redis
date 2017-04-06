Events
===

The `ConnectionMultiplexer` type exposes multiple events that can be used to understand what is happening under the covers. This can be useful in particular for logging purposes.

- `ConfigurationChanged` - raised when the configuration of a connection is changed from inside the `ConnectionMultiplexer`
- `ConfigurationChangedBroadcast` - raised when a reconfiguration message is received via pub/sub; this is most commonly caused by `IServer.MakeMaster` being used to change a node's replication configuration, which can optionally broadcast such a request to all clients
- `ConnectionFailed` - raised when a connection fails for any reason; note that you will not receive further `ConnectionFailed` notifications for that connection until connectivity has been re-established
- `ConnectionRestored` - raised when connectivity is re-established to a node that previously failed
- `ErrorMessage` - raised when the redis server responds to any user-initiated request with an error message; this is in addition to the regular exception / fault that will be reported to the immediate caller
- `HashSlotMoved` - raised when a "redis cluster" indicates that a hash-slot has been migrated between nodes; note that requests will normally be automatically re-routed, so the user is not required to do anything special here
- `InternalError` - raised when the library fails in some unanticipated way; this is intended primarily for debugging purposes, and most users should have no need of this event

Note that the pub/sub  implementation in StackExchange.Redis works *similarly* to events, with `Subscribe` / `SubscribeAsync` accepting an `Action<RedisChannel, RedisValue>` callback that is invoked when messages are received.