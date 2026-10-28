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

await db.Strings.GetAsync("app:user:42");   // a round trip; the key is inside the prefixes, so the reply is cached
await db.Strings.GetAsync("app:user:42");   // answered from memory
await db.Strings.GetAsync("other");         // a round trip, every time: outside the prefixes, so never cached
```

With `Prefixes` set, the prefixes decide what is cached: a read inside them is cached on its first miss, a read outside them is not cached at all (unless the call says `CommandFlags.CacheOptIn`). Without `Prefixes`, every read is cacheable, and a reply is cached the second time it is read rather than the first, so a request read only once never costs the cache anything - see [Tracking modes](#tracking-modes-and-prefixes) and [Admission](#admission-caching-what-is-read-again).

Requirements:

- **RESP3.** Invalidations arrive as out-of-band pushes, which only RESP3 can deliver on the same connection. With a tracking mode **named** and RESP2, connecting **fails** with a message saying so, rather than quietly running without invalidation. See [Using RESP3](Resp3). With the mode left unset, the cache instead falls back to keyspace notifications - see "Falling back", below. The two modes that do not use `CLIENT TRACKING` - `KeyspaceNotifications` and `None` - do not need RESP3.
- **A server that supports `CLIENT TRACKING`** (Redis 6.0 or later, or a compatible server). If the server refuses it - including a managed service that disables the command - the connection fails, for the same reason: a cache that is never invalidated is worse than no cache.

The cache is configured in code only - it is not part of the connection string, because its settings are correctness choices that deserve to be read before they are set.

There is **one cache per multiplexer**, shared by every database, every `IDatabase`, and every context made from it. On a [connection group](Failover) each member has a cache of its own and the options are set once, on `MultiGroupOptions.ClientCache` rather than on the members - see [Connection groups](#connection-groups).

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
- **reads that report time or access** - `TTL`, `PTTL`, the hash-field TTL reads, `TOUCH`, `OBJECT FREQ` / `IDLETIME` / `REFCOUNT`, `XINFO CONSUMERS`, and the per-entry form of `XPENDING` - whose answers change without any write;
- **`SORT` with a `BY` or `GET` pattern** (`BY weight_*`, `GET object_*->name`) - it reads keys it never names, so their changes could not invalidate it. A plain `SORT`, `BY nosort` and `GET #` are cached;
- **error replies**;
- **reads inside a transaction** (`MULTI`/`EXEC`): those are answered by `EXEC`, atomically with the transaction. Reads inside a *batch* do use the cache - a batch is pipelining, not atomicity. In `Tracking` mode a transaction that contains a read is sent behind `CLIENT CACHING NO`, so the server does not track keys nobody here will keep;
- **fire-and-forget** commands (`CommandFlags.FireAndForget`) - there is no reply to keep;
- **replies larger than `MaxPayloadBytes`** (1 MiB by default);
- **keys outside `Prefixes`**, when prefixes are configured - in `Broadcast` mode the server would never announce their changes; in `TrackingByPrefix` mode (what prefixes select by default) a call can still opt such a read in with `CommandFlags.CacheOptIn`;
- anything sent with `CommandFlags.CacheOptOut`, or through a context made with `WithoutCache()`;
- in `TrackingOptIn` mode, anything sent **without** `CommandFlags.CacheOptIn` - see [Tracking modes](#tracking-modes-and-prefixes).

Commands the client does not know - a module's, say - are not cached unless you declare them read-only with `flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly)` (see [Extending the client](Extending)). Only do that if the server announces changes to the keys the command reads. For module commands it usually does: Redis signals a key as modified - which is what drives invalidation - automatically when a module closes a key it opened for writing, unless the module has opted out (`REDISMODULE_OPTION_NO_IMPLICIT_SIGNAL_MODIFIED`) and signals by hand. To confirm for a particular module, cache a read (by default, read it twice), change the key with the module's own write command from a second connection, and check that the next read sees the change.

Consistency
---

What you can rely on:

- **You always read your own writes.** A write sent through this multiplexer invalidates the keys it names locally, before it goes on the wire - so a read issued after it, through the same multiplexer, never sees the old value. This holds even with `InvalidationGracePeriod` enabled.
- **Other clients' writes are seen shortly after they happen.** Redis pushes an invalidation when the key changes, and it arrives shortly after - but not instantly, and with no fixed bound (the server sends invalidations after the replies it was already writing). A read that lands in that gap can return the previous value. If a reply races an invalidation, it is not stored.
- **A lost connection empties the cache.** Changes made while the client was not listening are never announced, so on any connection failure the whole cache is discarded and refilled from the server.
- **`FLUSHDB` / `FLUSHALL` empty it too** - by anyone, on any database.

What it does **not** cover:

- **Key expiry is not announced.** When a key reaches its TTL, Redis sends no invalidation, so a cached copy outlives it until `TimeToLive` retires the entry. If you rely on keys disappearing on time, keep `TimeToLive` short or pass `CommandFlags.CacheOptOut` for those reads.
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
| `TrackingMode` | `Default` (= `Tracking` today, or `TrackingByPrefix` when `Prefixes` are set) | How the server decides which changes to announce, and whether reads are cached by default (`Tracking`, `Broadcast`), only inside `Prefixes` (`TrackingByPrefix`), or only when a call asks (`TrackingOptIn`) - see below. |
| `Prefixes` | none (all keys) | Which keys to cache, as `RedisKey` values: strings, raw bytes, or a key made with `WithKeyPrefix` (taken whole). Copied when set; changing your list afterwards changes nothing. Setting any selects `TrackingByPrefix`, where the list decides for calls that pass no flag; under `Broadcast` it is also sent to the server to filter what is announced. |
| `MaxBytes` | unbounded | A memory budget for cached replies. Entries that have not been read since they were stored are evicted, oldest first, to get back under it - see [Eviction](#eviction). |
| `MaxEntries` | unbounded | A limit on the number of entries - use it alongside `MaxBytes` when replies are small and numerous. |
| `MaxPayloadBytes` | 1 MiB | The largest single reply that may be cached. |
| `EvictionSampleSize` | 8 | How many live entries one eviction will examine before taking one that was read; larger is closer to true LRU, and costs more per eviction. Only applies to eviction by count (`MaxEntries`) and to replies too large to share a slab. |
| `SweepInterval` | 10 seconds | How often memory held by invalidated or expired entries is reclaimed. Correctness does not depend on it. |
| `DefaultPolicy` | `CachePolicy.Default` | How entries behave as they age - see below. |
| `Admission` | `Default` (= `OnRepeatedMiss` today) | When a reply that missed is stored: only once the same request has missed recently before, or (`OnFirstMiss`) every time. See below. |

### `CachePolicy` - how entries age

| setting | default | what it does |
|---|---|---|
| `TimeToLive` | 1 minute | The longest an entry is ever served, whether or not it was invalidated. A safety bound against missed invalidations and key expiry. |
| `RefreshAfter` | off | Once an entry is this old, a read still gets it, *and* a refresh is started in the background - so a hot key never goes from "cached" to "missing" for everyone at once. |
| `InvalidationGracePeriod` | off | After *another client's* change invalidates an entry, keep serving the old value for up to this long while a refresh runs. This deliberately serves a value the server has said is out of date, to protect a very hot key from every reader missing at the same instant. It never applies to your own writes. |

Entry ages - `TimeToLive`, `RefreshAfter` and `WithMaxCacheAge` - are measured on a coarse clock that is cheap enough to read on every hit, so they are accurate to a few milliseconds (about 16 ms on Windows). `InvalidationGracePeriod` is measured precisely.

### Serving stale while refreshing

Without either of the last two settings, an entry goes from "cached" to "gone" in one step - and for a hot key, every concurrent reader misses at that same instant and goes to the server together. Both settings replace that cliff with a window in which the old value is still answered *and* one background refresh fetches the new one. They differ in what opens the window:

- **`RefreshAfter`** - **read-while-ageing**: the window opens by age. An entry younger than `RefreshAfter` is simply fresh; between `RefreshAfter` and `TimeToLive` it is served and refreshed; past `TimeToLive` it is a miss like any other. Nothing here serves a value the server has said is wrong - only one that is older than you would like. A typical setting is a fraction of `TimeToLive` (say `TimeToLive = 1 minute`, `RefreshAfter = 45 seconds`), so a key that is read steadily is refreshed before it ever expires.
- **`InvalidationGracePeriod`** - **read-while-stale**: the window opens when *another client* changes the key. The server has said the cached value is out of date, and for up to this long it is served anyway while the refresh runs. This is the stronger choice: an invalidation reaches every reader of a key at once, so it is the stampede that `RefreshAfter` cannot help with, but it means answering with a value already known to be superseded. Keep it short - milliseconds to a few seconds - and use it only for data where a slightly late update is harmless.

How the window behaves, for both:

- **One refresh per entry.** The first read inside the window starts the refresh; the other readers are answered from the cache and do not start their own. The read that starts it is not delayed either: the refresh runs in the background, and that caller also gets the cached value.
- **The refresh is the original request, re-sent.** There is nothing to supply - no factory or callback - because the cached request *is* the recipe.
- **A failed refresh is retried by a later read.** The old value goes on being served for the rest of the window, then the entry expires as usual.
- **A change wins over a refresh in flight.** If the key changes while a refresh is on the wire, the refresh's reply is not stored, exactly as for an ordinary fill.
- **The grace period is measured from the invalidation**, not from the first read to notice it, and it is the cap. A key nobody reads during the window simply expires - nothing resurrects it later - and on a key written faster than it can be refreshed, stale answers stop when the window ends rather than continuing indefinitely.
- **Neither extends an entry's life.** `TimeToLive` remains the longest any entry is served, and a context's `WithMaxCacheAge` is honoured inside the window too: a caller who asked for fresher gets a miss, not a stale answer.

When the window does **not** apply:

- **Your own writes.** A write through this multiplexer invalidates the keys it names immediately and permanently for those entries: the next read goes to the server, whatever the grace period. You always read your own writes.
- **A lost connection, or `FLUSHDB` / `FLUSHALL`.** The whole cache is discarded with no grace: changes made while the client was not listening were never announced, so nothing in it can be trusted.
- **Entries that were never cached** - a miss is a miss; neither setting pre-fetches anything.

Both settings are off by default, and that is deliberate: serving an old value is a decision about correctness, not a performance tweak, so it should be made explicitly. They can be set in `DefaultPolicy` for the whole cache:

```csharp
options.ClientCache = new CacheOptions
{
    Prefixes = ["catalog:"],
    DefaultPolicy = new CachePolicy
    {
        TimeToLive = TimeSpan.FromMinutes(1),
        RefreshAfter = TimeSpan.FromSeconds(45),                    // refresh steadily-read entries before they expire
        InvalidationGracePeriod = TimeSpan.FromMilliseconds(250),   // ride out the herd when a hot key changes
    },
};
```

### Admission: caching what is read again

A reply that misses is not stored straight away. The first miss for a request is **remembered** - in a small, fixed-size filter - and only a request that misses again soon afterwards is cached. So a request read once never touches the cache, and a request read often is cached after its second read rather than its first. This is TinyLFU's "doorkeeper", a well-established technique.

It applies to reads that are cacheable by default - every read in `Tracking` and `Broadcast` mode. A read that **opted in** is stored on its first miss regardless: one flagged `CommandFlags.CacheOptIn`, or any read inside `Prefixes` in `TrackingByPrefix` mode (the default when prefixes are set). Those reads have already paid for `CLIENT CACHING YES`, and a tracked key whose reply is then not stored would be the worst of both.

Why it is the default: storing a reply costs something - it is copied, and an entry is kept until it is evicted - and one-off reads that are stored push out the entries that are read again. Measured on a skewed workload (a few hot keys and a long tail, Zipf s = 0.99), caching on the second miss held a **higher** hit rate than caching on the first (76.6% against 72.4%) at about 50% more throughput; on a workload that never repeats, it removed the cache's overhead entirely.

What it costs: one extra round trip for each key before it is cached - and again whenever it re-enters the cache after being evicted or invalidated. That only loses on a workload where every request is read exactly twice and never again.

To store every reply on its first miss instead - for example, a small set of keys you know will be read repeatedly, where you want the very first repeat served from memory:

```csharp
options.ClientCache = new CacheOptions
{
    Admission = CacheAdmission.OnFirstMiss,
    // ...
};
```

`CacheAdmission.Default` (the value you get by not setting it) means "the library's choice", which is `OnRepeatedMiss` today; name a rule explicitly if you depend on it.

### Eviction

The policy is **Clock**, an approximation of LRU: each entry carries a flag that a read sets, and eviction walks the entries oldest first, sparing - and clearing the flag of - any entry that was read since it was last examined, and taking the first that was not. An entry that keeps being read is never evicted; one that stops being read goes the next time its turn comes round. What a read costs is one flag write per entry per eviction cycle, not a write per read.

Under `MaxBytes`, replies are stored in large slabs in the order they arrive and memory comes back a slab at a time: the oldest slab is evicted, and the entries in it that were read are copied forward into the current slab instead of being dropped (`Promoted` in the cache's counters). Measured on a skewed trace (Zipf s = 0.99, a cache a tenth of the keyspace), the hit rate lands at or slightly above an exact LRU on the same capacity, and about five points above FIFO; on a hot set that fits plus a stream of one-off reads, every read of the hot set hits, where FIFO would miss a quarter of them. Admission (below) decides what is stored in the first place; eviction decides what is kept.

### Tracking modes and prefixes

- **`Tracking`** (the default without `Prefixes`): the server remembers which keys this client read and announces only those. Precise - invalidation traffic scales with what this client holds, not with how busy the server is - but the server holds a table per client, and evicts from it (announcing what it drops) under pressure, which costs a refill here. `Prefixes` cannot be combined with it. Negotiated as `CLIENT TRACKING ON OPTOUT`: every read is tracked and cacheable unless a call says otherwise with `CommandFlags.CacheOptOut`, which also tells the server not to track that read (a `CLIENT CACHING NO` goes out as one unit with it).
- **`TrackingByPrefix`** (the default when `Prefixes` are set): `Prefixes` decide for the calls that pass no flag. A read whose keys all fall under a prefix is treated as if it had said `CommandFlags.CacheOptIn`: it goes out behind `CLIENT CACHING YES`, the server tracks it, and it is stored on its first miss. Any other read goes to the server as it would without a cache. The flags still win: `CacheOptIn` on a read outside the prefixes caches it, `CacheOptOut` on a read inside them does not. Negotiated as `CLIENT TRACKING ON OPTIN`; the list is **not** sent to the server (the server only accepts `PREFIX` with `BCAST`), so the server tracks exactly the keys this client holds. The price is an extra frame and reply per miss inside the prefixes, which is the traffic the cache exists to shrink. Prefixes are `RedisKey` values, so a byte prefix is as good as a string one, and a key made with `WithKeyPrefix` can be given as it is. They must not overlap one another (`"app:"` and `"app:user:"` together are rejected), and they are compared with the key as sent - so if you build keys with `WithKeyPrefix`, the prefix must include that.
- **`TrackingOptIn`**: opt-in by flag alone. Negotiated as `CLIENT TRACKING ON OPTIN`: the server tracks nothing, and nothing is cached, unless a call says so with `CommandFlags.CacheOptIn` - that read goes out behind `CLIENT CACHING YES`, is tracked, and is stored on its first miss. Every other read goes to the server as it would without a cache. For "cache these few hot keys and leave everything else alone" when the keys are not a prefix; the price is an extra frame and an extra reply per opted-in miss. `Prefixes` cannot be combined with it - that is `TrackingByPrefix`.
- **`Broadcast`**: the server announces every change to every key matching your `Prefixes`, whoever made it, and keeps no record of what you read. Cheap for the server; the cost is on the client, which hears about keys it never cached, and that cost is set by how busy the server is rather than by anything this client does - which is why prefixes no longer select this mode by default. **Set `Prefixes`** if you choose it: with none, this connection hears about every write to every key in the instance. Keys outside the prefixes are simply not cached, and here the list is a hard boundary: the server was never asked about those keys, so no flag can opt one in. The per-call flags keep only their local meaning: the server has no per-client table to opt in to or out of.
- **`KeyspaceNotifications`**: no `CLIENT TRACKING` at all; the cache subscribes to [keyspace notifications](KeyspaceNotifications) instead - `__keyspace@*__:{prefix}*` once per entry in `Prefixes`, or `__keyspace@*__:*` without them - and invalidates the key named by any event. For servers or proxies that do not offer `CLIENT TRACKING` but do offer pub/sub, and it works over RESP2. **The server must have notifications enabled** (`notify-keyspace-events` including `K` and the event classes that can change what you cache; `KA` covers everything, expiry and eviction included). This is not checked: with notifications off the subscription is silent and the cache behaves as `None`. Like `Broadcast`, it hears about every matching change by anyone, so set `Prefixes` on a busy server; they are a hard boundary on what is cached, and must not contain glob characters (`*`, `?`, `[`). In a cluster each node raises notifications only for its own keys, so the subscriptions are made on every node. Weaker than tracking in two further ways: a `FLUSHDB`/`FLUSHALL` by another client raises no per-key event, and pub/sub does not replay what was missed while the subscription was down (losing a connection flushes the cache, as in every mode). `TimeToLive` bounds both.
- **`None`**: no server-assisted invalidation. Entries are retired by `TimeToLive` and by writes made through this multiplexer, and nothing else: a write by another client is served stale until the entry ages out, so choose the lifetime as the staleness you are prepared to accept. Nothing is negotiated, so it works over RESP2 and against any server. `Prefixes`, if set, only bound what is cached.

`CacheTrackingMode.Default` (the value you get by not setting it) means "the library's choice", resolved as above today; name a mode explicitly if you depend on it.

#### Falling back

Under `Default` only, when server-assisted tracking turns out to be unavailable, the cache switches to `KeyspaceNotifications` instead of failing the connection. That happens when:

- the connection is not RESP3;
- `CLIENT` is disabled in the `CommandMap` (known before connecting);
- the server answers `CLIENT TRACKING` with any error - an older server, a proxy, an ACL.

The switch is for the whole cache, happens at most once, and never reverts. It flushes the cache, starts the keyspace subscription, and logs a warning. If `CONFIG GET notify-keyspace-events` is permitted and shows that notifications are off, a second warning says so: the cache then behaves as `None`, retired by `TimeToLive` alone. That is a diagnostic, not a failure. `ClientCache.EffectiveTrackingMode` (from `GetClientCache()`) reports the mode actually in force. A mode named explicitly never falls back: it fails the connection as before.

Keyspace notifications match prefixes as patterns, so a prefix containing `*`, `?` or `[` cannot be subscribed to; after a fallback such a cache logs the failed subscription and is retired by `TimeToLive` alone.

What the two flags do, by mode:

| | `Tracking` | `TrackingByPrefix` | `TrackingOptIn` | `Broadcast`, `KeyspaceNotifications`, `None` |
|---|---|---|---|---|
| no flag | cached; tracked | inside `Prefixes`: cached, stored on first miss, `CLIENT CACHING YES` sent; outside: not cached, not tracked | not cached; not tracked | cached if inside `Prefixes` |
| `CacheOptIn` | cached, stored on first miss | cached, stored on first miss; `CLIENT CACHING YES` sent - inside or outside `Prefixes` | cached, stored on first miss; `CLIENT CACHING YES` sent | cached if inside `Prefixes`, stored on first miss |
| `CacheOptOut` | not cached; `CLIENT CACHING NO` sent | not cached | not cached | not cached |

Passing both flags is rejected. Neither flag can make an uncacheable command cacheable (a write, a keyless read, a random or time-based read). In `Broadcast`, `KeyspaceNotifications` and `None` modes `CacheOptIn` does not bypass `Prefixes`; in `TrackingByPrefix` mode it does, because there the list is only the policy for calls that passed no flag.

### Invalidating by hand

The server announces every change it sees, so you normally never touch the cache. For the change it cannot announce - a key whose expiry you know has passed (expiry is not announced), a module command that opted out of signalling, or a value you simply know is wrong - the cache can be told directly:

```csharp
var cache = multiplexer.GetClientCache();   // null when ClientCache was not configured
cache?.Invalidate(key);                      // one key, as sent to the server (include any context prefix)
cache?.InvalidateAll();                      // everything
```

These behave as a write from this process does: the entries are dropped at once, `InvalidationGracePeriod` does not apply, and on a connection group every member's cache is stamped. Nothing is sent to the server. `Invalidate` returns whether anything cached depended on the key.

The same object carries the cache's counters - `Count`, `Bytes`, `Stored`, `Evicted`, `Promoted`, `Expired`, `Refreshes`, `ServedStale` - cumulative since the connection was made. There is no hit counter, because a hit deliberately records nothing; see [Is it working?](#is-it-working) for the measurement that cannot be fooled.

### Connection groups

With a [connection group](Failover) (geo-failover, Active-Active), the cache is configured once, on `MultiGroupOptions.ClientCache`, and every member inherits it; a member whose own `ConfigurationOptions.ClientCache` is set is refused when the group connects. Each member still needs `Protocol = RedisProtocol.Resp3`, unless the mode is `KeyspaceNotifications` or `None`.

```csharp
MultiGroupOptions options = new MultiGroupOptions.Builder
{
    ClientCache = new CacheOptions { Prefixes = ["app:"], MaxBytes = 64 * 1024 * 1024 },
};
await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
```

What that gives you:

- **One cache per member, not one for the group.** A cached entry is only as good as the invalidation stream behind it, and that stream belongs to one member's connections. So each member keeps its own cache, and a read is served from, and stored into, the cache of whichever member is active at that moment. Nothing is copied or flushed when the active member changes: the member you left keeps its cache, still invalidated by its own server, ready for when you come back.
- **Your own writes reach every member.** A write made while member B is active reaches A's server only by replication, so without help A's cache could, after a failback, hand you the value you had just replaced. To prevent that, a write from this process invalidates the key in every member's cache. A then misses and asks its server, which may still be catching up; that is ordinary replication lag, and an uncached read would see the same. Server-sent invalidations, and the flush that follows a lost connection, stay with their own member.
- **The members must agree.** That is why the setting lives on the group: a member that cached differently, or not at all, would change what callers get on every failover.

`GetClientCache()` on a group returns the active member's cache at the time of the call. `Invalidate` and `InvalidateAll` through it reach every member, as a write does; its counters describe that member only.

### Per call, and per context

- **`CommandFlags.CacheOptOut`** - this call neither reads from nor writes to the cache, and in `Tracking` mode the server is told not to track it:

  ```csharp
  var exact = await db.Strings.GetAsync(key, CommandFlags.CacheOptOut);
  ```

- **`CommandFlags.CacheOptIn`** - this call's reply is stored on its first miss, skipping [admission](#admission-caching-what-is-read-again); in `TrackingOptIn` mode it is also the only way a reply is cached at all, and in `TrackingByPrefix` mode it caches a read outside the prefixes:

  ```csharp
  var hot = await db.Strings.GetAsync(key, CommandFlags.CacheOptIn);
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

`EVAL_RO` / `EVALSHA_RO` (the read-only script commands, via `ScriptEvaluateReadOnly` or `Scripts.EvaluateReadOnlyAsync`) are cached, and invalidated by changes to the keys in `KEYS[]`. **A script that reads a key it did not declare** - building a key name inside Lua, say - will not be invalidated when that key changes, so its cached result can go stale until `TimeToLive`. Declare every key a script reads, or pass `CommandFlags.CacheOptOut` for that script. Ordinary `EVAL` / `EVALSHA` are treated as writes and never cached.

Results from the cache
---

Cached replies are shared, not copied for each caller. That is invisible for ordinary results (`RedisValue`, `long`, arrays). For the low-allocation forms - `ReadOnlyLease<T>`, `RespResult` - a cache hit hands you a read-only view over the shared buffer: dispose it as usual; it does not affect anyone else's copy. A mutable `Lease<byte>` is always your own copy.

A cache hit completes its `ValueTask` synchronously. Do not use that as a test for "this came from the cache": misses can complete synchronously too (a blocking context, for one), and nothing about the result says where it came from.

Is it working?
---

`multiplexer.GetClientCache()` exposes the cache's own counters (see [Invalidating by hand](#invalidating-by-hand)), which tell you what was stored and what was evicted but not how often a read was answered locally. The reliable check for that is on the server, which cannot be fooled by the client:

- `CLIENT TRACKINGINFO` on the connection, or `CLIENT LIST` (look for `flags=t` on the interactive connections), confirms tracking is on;
- comparing `total_commands_processed` from `INFO stats` before and after a read-heavy workload shows how many reads reached the server - an operation answered from the cache is one the server never sees. This is how this library's own tests measure hit rate;
- `INFO commandstats` breaks the same count down per command (`cmdstat_get:calls=...`), which isolates the read under test from handshake, setup and other traffic - the better measure for a test of one command.

When checking by hand, remember that a reply is cached on its **second** read by default (see [Admission](#admission-caching-what-is-read-again)): read a key three times, and expect two of them to reach the server. A test that reads twice and expects one round trip is testing `OnFirstMiss`, and should set it.

See also
---

- [Using RESP3](Resp3)
- [Configuration](Configuration)
- [Command groups](Groups) - contexts, and where `WithMaxCacheAge` / `WithoutCache` fit
- [Redis: client-side caching](https://redis.io/docs/latest/develop/reference/client-side-caching/)
