Hot Keys
===

The `HOTKEYS` command allows for server-side profiling of CPU and network usage by key. It is available in Redis 8.6 and later.

This command is available via the `IServer.HotKeys*` methods:

``` c#
// Get the server instance.
IConnectionMultiplexer muxer = ... // connect to Redis 8.6 or later
var server = muxer.GetServer(endpoint); // or muxer.GetServer(key)

// Start the capture; you can specify a duration, or manually use the HotKeysStop[Async] method; specifying
// a duration is recommended, so that the profiler will not be left running in the case of failure.
// Optional parameters allow you to specify the metrics to capture, the sample ratio, and the key slots to include;
// by default, all metrics are captured, every command is sampled, and all key slots are included.
await server.HotKeysStartAsync(duration: TimeSpan.FromSeconds(30));

// Now either do some work ourselves, or await for some other activity to happen:
await Task.Delay(TimeSpan.FromSeconds(35)); // whatever happens: happens 

// Fetch the results; note that this does not stop the capture, and you can fetch the results multiple times
// either while it is running, or after it has completed - but only a single capture can be active at a time.
var result = await server.HotKeysGetAsync();

// ...investigate the results... 

// Optional: discard the active capture data at the server, if any.
await server.HotKeysResetAsync();
```

The `HotKeysResult` class (our `result` value above) contains the following properties:

- `Metrics`: The metrics captured during this profiling session.
- `TrackingActive`: Indicates whether the capture currently active.
- `SampleRatio`: Profiling frequency; effectively: measure every Nth command. (also: `IsSampled`)
- `SelectedSlots`: The key slots active for this profiling session.
- `CollectionStartTime`: The start time of the capture.
- `CollectionDuration`: The duration of the capture.
- `AllCommandsAllSlotsTime`: The total CPU time measured for all commands in all slots, without any sampling or filtering applied.
- `AllCommandsAllSlotsNetworkBytes`: The total network usage measured for all commands in all slots, without any sampling or filtering applied.

When slot filtering is used, the following properties are also available:

- `AllCommandsSelectedSlotsTime`: The total CPU time measured for all commands in the selected slots.
- `AllCommandsSelectedSlotsNetworkBytes`: The total network usage measured for all commands in the selected slots.

When slot filtering *and* sampling is used, the following properties are also available:

- `SampledCommandsSelectedSlotsTime`: The total CPU time measured for the sampled commands in the selected slots.
- `SampledCommandsSelectedSlotsNetworkBytes`: The total network usage measured for the sampled commands in the selected slots.

If CPU metrics were captured, the following properties are also available:

- `TotalCpuTimeUser`: The total user CPU time measured in the profiling session.
- `TotalCpuTimeSystem`: The total system CPU time measured in the profiling session.
- `TotalCpuTime`: The total CPU time measured in the profiling session.
- `CpuByKey`: Hot keys, as measured by CPU activity; for each:
  - `Key`: The key observed.
  - `Duration`: The time taken. 

If network metrics were captured, the following properties are also available:

- `TotalNetworkBytes`: The total network data measured in the profiling session.
- `NetworkBytesByKey`: Hot keys, as measured by network activity; for each:
  - `Key`: The key observed.
  - `Bytes`: The network activity, in bytes.

Note: to use slot-based filtering, you must be connected to a Redis Cluster instance. The
`IConnectionMultiplexer.HashSlot(RedisKey)` method can be used to determine the slot for a given key. The key
can also be used in place of an endpoint when using `GetServer(...)` to get the `IServer` instance for a given key.
