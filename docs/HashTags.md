Hash Tags and Slots
===

Outside of Redis Cluster (a single server, or a standalone primary/replica) there are no "slots", so none of this applies -
this topic is specific to Redis Cluster environments.

In a Redis Cluster the keyspace is divided into **16384 hash slots**; every key belongs to exactly one slot, and every
slot is owned by exactly one node at any moment. The slot for a key is based on the CRC hash of the key. Clients (including
StackExchange.Redis) use this to route each command to the node that owns the key's slot; if a key has moved, the server
replies `MOVED`/`ASK` and the client re-routes.

Because a slot lives wholly on one node, **co-locating keys in the same slot guarantees they are on the same node** - which
is what multi-key operations need.

Multi-key operations
---

Operations that involve multiple keys (`MSET`, `SUNIONSTORE`, etc - or `MULTI`/`EXEC` batches) typically require *all* the keys
they touch to be in the same slot, because they cannot span nodes. An attempted multi-slot operation is rejected with a `CROSSSLOT` error.

Hash tags
---

A **hash tag** lets you force otherwise-different keys into the *same* slot. If a key contains a (non-empty) `{...}` section, only the
bytes **between the first `{` and the next `}`** are hashed, instead of the whole key:

| Key | Bytes hashed |
| --- | --- |
| `user:1:profile` | `user:1:profile` (the whole key) |
| `{user:1}:profile` | `user:1` |
| `{user:1}:settings` | `user:1` |
| `{user:1}:orders` | `user:1` |

So `{user:1}:profile`, `{user:1}:settings` and `{user:1}:orders` all hash to the same slot and can participate in one
transaction, script, or bulk import. Note that as a consequence: *all the data with the same hash tag must now fit
on that one shard*; you can't "cheat" the slot rules by using `{dummy}real_key_here`, because that simply pushes your
entire database onto a single node, defeating the entire point of Redis Cluster. Hash-tags are best used to co-locate
genuinely related data - for example splitting things by customer, tenant, etc.

Inspecting the slot
---

`IConnectionMultiplexer.HashSlot(RedisKey)` returns the slot a key resolves to, which is handy for verifying that keys
you intend to group really do share a slot:

``` c#
var slot1 = muxer.HashSlot("{user:1}:profile");
var slot2 = muxer.HashSlot("{user:1}:orders");
// slot1 == slot2
```

Interaction with key prefixes
---

A [key prefix](KeysValues) (via `WithKeyPrefix`) is prepended to the key *before* hashing, so it participates in the
slot calculation like any other bytes. To co-locate prefixed keys, make sure the hash tag falls inside the portion that
ends up shared — for example prefix `"{tenant42}:"` puts every key under that prefix in one slot.
