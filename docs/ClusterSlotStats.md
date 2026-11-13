Cluster slot statistics
===

The `CLUSTER SLOT-STATS` command reports per-slot usage statistics - key counts, and optionally CPU,
memory and network metrics - so you can see *where* in the keyspace a cluster's load lands, and which
slots would be worth migrating. It is available in Redis 8.2 and later, on cluster-enabled servers
only (a standalone server answers with an error).

This command is available via the `IServer.ClusterSlotStats*` methods, which are
[experimental (`SER012`)](exp/SER012) while the API shape settles:

``` c#
// Get a server instance; each node reports only the slots its own shard serves,
// so ask each node to see the whole cluster.
IConnectionMultiplexer muxer = ... // connect to a Redis 8.2+ cluster
var server = muxer.GetServer(endpoint);

// Form 1: the stats for a slot range (inclusive), in ascending slot order.
var range = await server.ClusterSlotStatsBySlotRangeAsync(0, 16383);

// Form 2: the node's slots sorted by a metric, busiest first; here, the ten
// slots with the most keys.
var top = await server.ClusterSlotStatsOrderByAsync(ClusterSlotStatsMetric.KeyCount, limit: 10);

foreach (var slot in top.Slots)
{
    Console.WriteLine($"slot {slot.Slot}: {slot.KeyCount} keys, cpu: {slot.CpuTime}");
}
```

Each `ClusterSlotStatsEntry` carries:

- `Slot`: the hash slot the entry describes.
- `KeyCount`: the number of keys in the slot; always collected.
- `CpuTime`: CPU time spent serving the slot's keys (the server's `cpu-usec`, as a `TimeSpan?`).
- `MemoryBytes`: memory used by the slot's keys.
- `NetworkBytesIn` / `NetworkBytesOut`: network traffic for the slot's keys.
- `Metadata`: any metric the server reports that this library does not recognize; the metric set is
  configuration-dependent and has grown before, so unknown metrics are preserved rather than discarded.

Metric collection
---

Only `key-count` is always collected. The other metrics are gated by the `cluster-slot-stats-enabled`
server configuration, which takes the metric groups to enable (`cpu`, `net`, `mem`, or `yes` for all;
`mem` additionally requires memory tracking from server startup):

```
CONFIG SET cluster-slot-stats-enabled "cpu net"
```

A metric that is not being collected comes back as `null` on `ClusterSlotStatsEntry` - deliberately
distinct from a collected zero. Sorting (`ClusterSlotStatsOrderBy`) by a metric that is not being
collected is refused by the server; `ClusterSlotStatsMetric.KeyCount` always works.

Relationship to `HOTKEYS`
---

The two are complementary: [`HOTKEYS`](HotKeys) profiles *which keys* are hot on a server, while
`CLUSTER SLOT-STATS` reports *which slots* carry the load - the granularity at which a cluster can
actually rebalance. A typical investigation finds the heavy slots with `CLUSTER SLOT-STATS ORDERBY`,
then profiles the keys within them with `HOTKEYS START ... SLOTS ...`.
