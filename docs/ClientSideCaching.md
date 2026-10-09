Client-side caching
===

StackExchange.Redis can keep a local copy of read results and answer repeated reads from memory, without a round trip - while Redis tells it, as keys change, which copies are no longer valid. This is Redis' [server-assisted client-side caching](https://redis.io/docs/latest/develop/reference/client-side-caching/) (`CLIENT TRACKING`), built into the client.

It is **off by default**. A cache changes what a read can return - a value that is very slightly behind the server is now possible - so it is something you turn on deliberately, not something you get by upgrading.

Turning it on
---

```csharp
using StackExchange.Redis;
using StackExchange.Redis.Caching;

var options = ConfigurationOptions.Parse("localhost:6379");
options.Protocol = RedisProtocol.Resp3;             // required - see below
options.ClientCache = new CacheOptions
{
    Prefixes = ["app:"],                             // recommended - see "Tracking modes and prefixes"
    MaxBytes = 64 * 1024 * 1024,                     // recommended - the default is unbounded
};

using var conn = await ConnectionMultiplexer.ConnectAsync(options);
var db = conn.GetDatabase();

await db.Strings.GetAsync("app:user:42");   // a round trip; the reply is cached
await db.Strings.GetAsync("app:user:42");   // answered from memory
```

Requirements:

- **RESP3.** Invalidations arrive as out-of-band pushes, which only RESP3 can deliver on the same connection. With a cache configured and RESP2, connecting **fails** with a message saying so, rather than quietly running without invalidation. See [Using RESP3](Resp3).
- **A server that supports `CLIENT TRACKING`** (Redis 6.0 or later, or a compatible server). If the server refuses it - including a managed service that disables the command - the connection fails, for the same reason: a cache that is never invalidated is worse than no cache.

The cache is configured in code only - it is not part of the connection string, because its settings are correctness choices that deserve to be read before they are set.

There is **one cache per multiplexer**, shared by every database, every `IDatabase`, and every context made from it.

What it covers
---

The cache works below the API surface, so it applies the same way to:

- the original `IDatabase` methods - `db.StringGet(...)`, `db.StringGetAsync(...)`, synchronous and asynchronous alike;
- the command groups - `db.Strings.GetAsync(...)`, `db.Hashes.GetAsync(...)`, ...;
- ad-hoc commands - `db.Context.SendAsync<T>($"...")` - as long as keys are written as `RedisKey` (see [Ad-hoc commands](Execute)): a key passed as a `string` is not known to be a key, so it can neither be cached safely nor invalidated.

**Cached:** reads of keys, whose answer depends only on the keys they name. That includes:

- `GET`, `MGET`, `GETRANGE`, `STRLEN`, `EXISTS`, `TYPE`;
- the hash, list, set and sorted-set reads - `HGET`, `HMGET`, `HGETALL`, `LRANGE`, `LINDEX`, `SMEMBERS`, `SISMEMBER`, `ZRANGE`, `ZSCORE`, and so on;
- bitmap, HyperLogLog, geospatial, stream and vector-set reads;
- `HSCAN`, `SSCAN` and `ZSCAN` pages, and range walks such as vector-set `VRANGE` paging - each page is its own entry, invalidated when its key changes;
- read-only scripts - `EVAL_RO` / `EVALSHA_RO` - on the keys they declare (see [Scripts](#scripts));
- "not found": a nil reply is cached like any other, so a missing key does not cost a round trip every time.

A multi-key read such as `MGET a b c` is one entry; a change to any of its keys invalidates it.

**Never cached:**

- **writes** - and a write from this client invalidates what it touches *before* it is sent (see [Consistency](#consistency));
- **commands that name no key** - `SCAN`, `KEYS`, `INFO`, `DBSIZE`, `RANDOMKEY`, ... - because the server can only announce changes to keys, so nothing could ever invalidate them;
- **random reads** - `SRANDMEMBER`, `HRANDFIELD`, `ZRANDMEMBER`, `VRANDMEMBER` - which would otherwise return the same "random" answer every time;
- **reads that report time or access** - `TTL`, `PTTL`, the hash-field TTL reads, `TOUCH`, and `OBJECT FREQ` / `IDLETIME` / `REFCOUNT` - whose answers change without any write;
- **error replies**;
- **reads inside a transaction** (`MULTI`/`EXEC`): those are answered by `EXEC`, atomically with the transaction. Reads inside a *batch* do use the cache - a batch is pipelining, not atomicity;
- **fire-and-forget** commands (`CommandFlags.FireAndForget`) - there is no reply to keep;
- **replies larger than `MaxPayloadBytes`** (1 MiB by default);
- **keys outside `Prefixes`**, when prefixes are configured - the server would never announce their changes;
- anything sent with `CommandFlags.NoClientCache`, or through a context made with `WithoutCache()`.

Commands the client does not know - a module's, say - are not cached unless you declare them read-only with `flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly)` (see [Extending the client](Extending)). Only do that if the server announces changes to the keys the command reads - which depends on the module marking its writes as key modifications; check before relying on it.

Consistency
---

What you can rely on:

- **You always read your own writes.** A write sent through this multiplexer invalidates the keys it names locally, before it goes on the wire - so a read issued after it, through the same multiplexer, never sees the old value. This holds even with `InvalidationGracePeriod` enabled.
- **Other clients' writes are seen shortly after they happen.** Redis pushes an invalidation when the key changes, and it arrives shortly after - but not instantly, and with no fixed bound (the server sends invalidations after the replies it was already writing). A read that lands in that gap can return the previous value. If a reply races an invalidation, it is not stored.
- **A lost connection empties the cache.** Changes made while the client was not listening are never announced, so on any connection failure the whole cache is discarded and refilled from the server.
- **`FLUSHDB` / `FLUSHALL` empty it too** - by anyone, on any database.

What it does **not** cover:

- **Key expiry is not announced.** When a key reaches its TTL, Redis sends no invalidation, so a cached copy outlives it until `TimeToLive` retires the entry. If you rely on keys disappearing on time, keep `TimeToLive` short or pass `CommandFlags.NoClientCache` for those reads.
- **A dead connection nobody has noticed** is not detected by the cache; it relies on the multiplexer's own failure detection, with `TimeToLive` as the backstop. That is why `TimeToLive` is always finite.
- **Undeclared keys in scripts** - see [Scripts](#scripts).

Two behaviours that are safe, but worth knowing:

- **Databases**: entries are kept per database number, but an invalidation names only the key, so a write to `foo` in database 3 also discards a cached `foo` from database 2. That costs a refill, never a stale read.
- **Replicas**: a read with `CommandFlags.PreferReplica` may be answered from an entry filled by the primary, and the other way round. Both are kept current by the same invalidations.

Controlling it
---

### `CacheOptions` - set once, when connecting

| option | default | what it does |
|---|---|---|
| `Enabled` | `true` | Whether the cache exists. Setting `ClientCache` at all is what turns caching on; this lets you keep the options and switch it off. |
| `TrackingMode` | `Default` (= `Broadcast` today) | How the server decides which changes to announce - see below. |
| `Prefixes` | none (all keys) | Which keys to track and cache, in `Broadcast` mode. |
| `MaxBytes` | unbounded | A memory budget for cached replies. Entries are evicted, roughly oldest first, to get back under it. |
| `MaxEntries` | unbounded | A limit on the number of entries - use it alongside `MaxBytes` when replies are small and numerous. |
| `MaxPayloadBytes` | 1 MiB | The largest single reply that may be cached. |
| `EvictionSampleSize` | 8 | How many entries eviction considers per choice; larger is closer to true LRU, and costs more per eviction. |
| `SweepInterval` | 10 seconds | How often memory held by invalidated or expired entries is reclaimed. Correctness does not depend on it. |
| `DefaultPolicy` | `CachePolicy.Default` | How entries behave as they age - see below. |

### `CachePolicy` - how entries age

| setting | default | what it does |
|---|---|---|
| `TimeToLive` | 1 minute | The longest an entry is ever served, whether or not it was invalidated. A safety bound against missed invalidations and key expiry. |
| `RefreshAfter` | off | Once an entry is this old, a read still gets it, *and* a refresh is started in the background - so a hot key never goes from "cached" to "missing" for everyone at once. |
| `InvalidationGracePeriod` | off | After *another client's* change invalidates an entry, keep serving the old value for up to this long while a refresh runs. This deliberately serves a value the server has said is out of date, to protect a very hot key from every reader missing at the same instant. It never applies to your own writes. |

### Tracking modes and prefixes

- **`Broadcast`** (the default): the server announces every change to every key matching your `Prefixes`, whoever made it, and keeps no record of what you read. Cheap for the server; the cost is on the client, which hears about keys it never cached. **Set `Prefixes` to the parts of the keyspace you actually want cached**: with none, this connection hears about every write to every key in the instance, which on a busy server is a lot of traffic. Keys outside the prefixes are simply not cached. Prefixes must not overlap one another (`"app:"` and `"app:user:"` together are rejected), and they are compared with the key as sent - so if you use `WithKeyPrefix`, include that prefix.
- **`PerKey`**: the server remembers which keys this client read and announces only those. Precise, but the server holds a table per client, and evicts from it (announcing what it drops) under pressure. `Prefixes` cannot be combined with it.

### Per call, and per context

- **`CommandFlags.NoClientCache`** - this call neither reads from nor writes to the cache:

  ```csharp
  var exact = await db.Strings.GetAsync(key, CommandFlags.NoClientCache);
  ```

- **`WithMaxCacheAge(TimeSpan)`** - a context that accepts cached answers only up to a given age. It can only tighten `TimeToLive`, never extend it:

  ```csharp
  var fresh = db.Context.WithMaxCacheAge(TimeSpan.FromSeconds(1));
  var value = await fresh.Strings.GetAsync(key);
  ```

- **`WithoutCache()`** - a context that bypasses the cache entirely:

  ```csharp
  var uncached = db.Context.WithoutCache();
  ```

Contexts are cheap to make and immutable; create them where you need them, or keep one for a part of your application with stricter needs. See [Command groups](Groups).

Scripts
---

`EVAL_RO` / `EVALSHA_RO` (the read-only script commands, via `ScriptEvaluateReadOnly` or `Scripts.EvaluateReadOnlyAsync`) are cached, and invalidated by changes to the keys in `KEYS[]`. **A script that reads a key it did not declare** - building a key name inside Lua, say - will not be invalidated when that key changes, so its cached result can go stale until `TimeToLive`. Declare every key a script reads, or pass `CommandFlags.NoClientCache` for that script. Ordinary `EVAL` / `EVALSHA` are treated as writes and never cached.

Results from the cache
---

Cached replies are shared, not copied for each caller. That is invisible for ordinary results (`RedisValue`, `long`, arrays). For the low-allocation forms - `ReadOnlyLease<T>`, `RespResult` - a cache hit hands you a read-only view over the shared buffer: dispose it as usual; it does not affect anyone else's copy. A mutable `Lease<byte>` is always your own copy.

A cache hit completes its `ValueTask` synchronously. Do not use that as a test for "this came from the cache": misses can complete synchronously too (a blocking context, for one), and nothing about the result says where it came from.

Is it working?
---

There are no public counters for the cache in this version. The reliable check is on the server, which cannot be fooled by the client:

- `CLIENT TRACKINGINFO` on the connection, or `CLIENT LIST` (look for `flags=t` on the interactive connections), confirms tracking is on;
- comparing `total_commands_processed` from `INFO stats` before and after a read-heavy workload shows how many reads reached the server - an operation answered from the cache is one the server never sees. This is how this library's own tests measure hit rate.

See also
---

- [Using RESP3](Resp3)
- [Configuration](Configuration)
- [Command groups](Groups) - contexts, and where `WithMaxCacheAge` / `WithoutCache` fit
- [Redis: client-side caching](https://redis.io/docs/latest/develop/reference/client-side-caching/)
