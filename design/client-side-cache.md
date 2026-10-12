# Client-side caching in v4: the design as built

Status: current as of 2026-10-12 (`a22ba168`; shipped in `4.0.127-alpha`). This is the record of **why** the cache is
the way it is - each mechanism with what was decided, what was rejected, and the evidence - gathered from the
places it was previously scattered:

- `interpolated-resp-writer.md` §6 - the original design (key, invalidation, cacheability, tracking, TTL,
  stampedes) and its decision log (§6.10). Still the detailed reasoning for those parts; **superseded where it
  disagrees with this file.
- `message-core-replacement.md` §3d, §7j, §7k, §9b-x - where the cache sits in the new core.
- `v4-ledger.md`, the cache-performance entry - the 2026-10-10/12 performance work, and the open list.
- Commit messages and the `<remarks>` in `src/StackExchange.Redis/Caching/` - the per-change evidence.
- `docs/ClientSideCaching.md` - the user-facing description; no reasoning.

Open work is tracked in the ledger, not here; §14 lists what is open so this file can be read on its own.

---

## 1. What it is, in one paragraph

A server-assisted cache (`CLIENT TRACKING`, RESP3 only, `BCAST` by default), one per multiplexer, keyed on the
**rendered request bytes plus the database**. A reply is stored with the generation each of its keys had when the
request was **sent**; the server's invalidation pushes bump those generations, so an entry is valid exactly while
every key it depends on is unchanged. Everything that can be wrong fails closed - to a miss - and a finite
`TimeToLive` backs up the invalidation stream against anything it misses. Replies live in large GC-owned slabs, are
admitted only on a repeated miss, and are evicted oldest-slab-first; a hit reads, validates and returns without
writing anything shared.

## 2. The invariants everything else serves

1. **A cached reply is a function of the keys it names, and the server will say when they change.** Anything that
   breaks this is refused at the door: keyless requests, errors, keys outside the tracked prefixes, commands not
   declared read-only. Each would be an entry that nothing can ever invalidate - permanently stale, silently.
2. **Fail closed.** A key that cannot be resolved, a frame whose keys cannot be listed, an unreadable invalidation,
   a generation that moved: all are misses (or flushes), never guesses.
3. **Generations are captured at send, not at reply.** An invalidation can land between send and reply, and the
   server will not repeat it (it drops the key from its table when it fires). Capturing first lets the fill see the
   world moved, and refuse (`RefusedRaced`).
4. **Read-your-own-writes is absolute.** Nothing this process wrote may be served from before the write - not by
   serve-stale, not by a coalesced waiter. Local writes stamp their keys *before* the write is sent.
5. **Stamp before drop.** Entries hold the key table's node directly; a node is stamped invalid before it can ever
   leave the table, or entries pointing at it would never learn.
6. **The hit path writes nothing shared.** Every cost that can be moved to a miss, an eviction or a sweep is.

One tolerated race, recorded so nobody "fixes" it: validation is not atomic across a multi-key entry (validate A,
A is invalidated, validate B, serve). The read overlapped the write, so either answer is a legitimate observation.

## 3. Map

| Mechanism | Where | Default |
|---|---|---|
| Key: request bytes + database | `EntryKey`, `RespRequest` | - |
| Invalidation: two tables, generations | `RespKeyTable`, `Dependency` | - |
| Tracking mode | `CacheOptions.TrackingMode`, `RespHandshake` | `Broadcast`, all keys |
| What may be cached | `IsCacheable`, `TryBeginFill`, `IsCacheableReply` | read-only, keyed, non-error |
| Storage | `RespClientCache.Slabs.cs` | slabs of `MaxBytes/32`, ≤ 1 MiB |
| Budget | `MaxBytes`, `MaxEntries`, `MaxPayloadBytes` | all unbounded |
| Eviction | `EvictSlabsToBudget`, `TryEvictOne` | oldest slab; second-chance queue |
| Admission | `CacheAdmission`, `CacheDoorkeeper` | `OnRepeatedMiss` |
| Ageing | `CachePolicy`, `WithMaxCacheAge`, `CacheClock` | TTL 1 min; no early refresh; no grace |
| Stampedes | `InFlight`, `TryAwaitInFlight`, refresh claim | on |
| Reclaiming dead entries | `SweepIfDue` from the heartbeat | every 10 s |

---

## 4. The key: the request, as sent

**Decided:** the cache key is the rendered RESP frame (`RespRequest`) plus the database number. The database is not
in the frame (`SELECT` is connection state), so it is folded into `EntryKey`'s hash and equality.

**Why:** the frame already exists, already encodes everything that distinguishes one read from another (command,
keys, arguments, the context's key prefix), and needs no per-command key-building code. It also makes refresh free:
re-sending the key *is* the refresh (§10), with no factory to retain.

**Rejected:** per-command key derivation (code per command, and every omission a correctness bug); always detaching
the frame to probe (`AsLookupKey()` borrows instead - a detach was a 48-byte lease per hit, §6.10).

**The hash** (`RedisValue.GetHashCode(ReadOnlySpan<byte>)`, shared with `RedisKey`, `RedisChannel` and
`RespKeyTable`) is a word-at-a-time multiply with MurmurHash3's finalizer. The previous multiply-by-33 gave 200,000
framed `GET rf:cbig:{i}` requests ~42,000 distinct hashes - dictionary chains of ~5, and a doorkeeper that saw every
request as a repeat (§9). `8a027bff`; `CacheAdmissionTests.SimilarKeysAndRequestsHashApart` covers 13 key shapes.

## 5. Invalidation: two tables, not a cross-index

**Decided** (§6.6, §6.10): table 1 maps `(frame, database)` to an entry; table 2 (`RespKeyTable`) maps key bytes to a
**global, monotonic generation ticket**. An entry records `(node, generation)` per key. An invalidation stamps the
node; validation is a dereference and a compare - no hashing on a hit.

**Why:** under `BCAST` the server names every key anybody changes, almost none of them ours; invalidation must be
O(1) and allocation-free for an untracked key (hash a span, read one bucket). A `key → set of entries` cross-index
makes invalidation O(entries) and costs on every insert and eviction.

**Rejected:** per-key counters (a counter restarting at zero can collide with a ticket recorded before an
invalidation, validating against a key that changed); the cross-index.

**Details that matter:**

- Table 2 has **no database**: Redis tracking has "a single keys namespace", so writing `foo` in db 3 invalidates a
  cached `foo` from db 2. Deliberate asymmetry with table 1.
- **Flush** (`OnFlush`, stamps every node; memory returns via the sweep): a null invalidation (`FLUSHALL`,
  `FLUSHDB`, tracking switched off), an unreadable push, a lost connection (`ConnectionMultiplexer.OnConnectionFailed`,
  synchronously and before the disposed check - an entry surviving a gap in the push stream is stale with nothing
  left that will ever say so), and a write whose frame cannot be read at all.
- **Local writes** stamp their keys *before* sending (`OnLocalWrite(frame)`), so a read issued in between cannot be
  stored against the old generation. A write whose key marks overflowed (> 62 arguments) stamps **every argument** -
  a superset of the keys, so correct - rather than flushing the cache. A failed write has invalidated for nothing:
  the right way to be wrong.
- A node records `LocalWriteAt`, so serve-stale and single-flight can tell our writes from anybody else's.
- Invalidation is **lazy**: it stamps and nothing else. A dead entry stays resident until the sweep, an eviction, or
  a fill that replaces it - and a fill must replace a dead resident entry, not treat it as a competitor. When it did
  not (`7dffd3da`), the cache decayed to 0% hits under sustained writes; the RespFest cache league found it (58-82%
  for every other client, 0.0% for this one).

## 6. Tracking: RESP3, broadcast, negotiated where the reads happen

**Decided** (§6.13, checked against a live server):

- **RESP3 only; no `REDIRECT`.** The redirected model lets an invalidation arrive before the reply it invalidates;
  we would survive it (generations captured at send), but there is no reason to pay for a protocol that needs it.
  Without RESP3, caching is **refused loudly** at connect (`RespHandshake`), never silently degraded into a cache
  nothing invalidates.
- **`BCAST` by default**, empty prefix (all keys). The server keeps no per-client table, so it cannot run out of
  room; we pay in hearing about keys we never read, which `Prefixes` bounds. `PerKey` is offered; `OPTIN`/`OPTOUT`
  are not, because `CLIENT CACHING YES|NO` applies to "the next command on that connection", which a multiplexer
  cannot promise. The enum's zero is `Default` ("no opinion"), resolved in one place, so the library can change its
  mind without overriding anybody who chose.
- **Prefixes are a connection-level knob, never derived from context key-prefixes**: server prefixes are global,
  must not overlap, and cannot be removed one at a time, while context prefixes nest. A prefix list is also a
  statement about what may be cached: a key outside it is never announced, so it is refused (`RefusedNotTracked`).
- **Negotiated on the connection that reads** (§9b-x). Asked on the shipped core's socket while the new core did the
  reading, per-key mode registered nothing and served stale indefinitely.
- **One cache per multiplexer** (§6.14): the invalidation stream has one destination, so `WithCache` means
  "participate or not", not "bring your own".
- **In a group, the cache is the active member's, resolved per command** (§7j). A shared cache serves A's values
  while B is active; flushing on every switch throws away a warm cache on a healthy switch. The deeper rule (§7k):
  *a cache entry carries an implicit subscription, and subscriptions do not transfer* - even between byte-identical
  replicas.

## 7. What may be cached

The gates, in the order a fill meets them (`TryBeginFill`, `TryCompleteCore`):

| Gate | Refusal counter | Why |
|---|---|---|
| Retry category **declared** and ≤ `CommandRetryReadOnly` | `RefusedByFlags` | Undeclared is zero, which a naive `<=` reads as safest; undeclared must mean "no". Read-only is necessary, not sufficient: non-deterministic and cursor commands are excluded in command metadata, beside the category. |
| Not `NoClientCache`, not `FireAndForget` | `RefusedByFlags` | Opt-out, not opt-in (§6.10: opt-in touches every method and fails silently by omission). Fire-and-forget promises `default`; a hit would return something else. |
| Not inside a transaction | - | A cached answer inside `MULTI` returns a value from before the transaction's own writes. Batches are fine: pipelining, not atomicity. |
| At least one key, and all keys listable (≤ 62 arguments) | `RefusedNoKeys` | Invalidation reports keys; a keyless entry is valid forever - not even a flush reaches it. A partial key list is worse than none. |
| Every key inside `Prefixes` | `RefusedNotTracked` | Every key, not any: one unannounced key makes the whole reply uninvalidatable. |
| Admission (§9) | `RefusedNotAdmitted` | Last of the refusals before anything is allocated. |
| Reply is not an error | `RefusedError` | An error need not be a function of the keys (config, ACLs, topology, OOM); cached, a transient failure becomes permanent. Classified by the first **content** element - RESP3 attributes may precede it. Nulls *are* cached: Redis tracks keys that do not exist. |
| Keys unchanged since send | `RefusedRaced` | Invariant 3. |
| Reply ≤ `MaxPayloadBytes` | `RefusedTooLarge` | Cheapest bound, needs no bookkeeping. |

Scripts: `EVAL_RO`/`EVALSHA_RO` are cacheable by default (the server enforces read-only); plain `EVAL` is not, unless
the caller declares it read-only - one rule, no script inspection (§6.9).

Every counter is incremented only on the fill path - after a round trip - so diagnostics cost nothing on a hit.
They exist because this design's remaining failure mode is silent: "why is this stale?" and "why is nothing cached?"
should both be answerable without a debugger.

## 8. Storage, budget and eviction

### Where a reply lives

The storage was rebuilt twice, and the reversals are the useful part:

1. **Pooled, reference-counted payloads** (§3d: "filling the cache is a retain rather than a copy"). Every hit
   retained and released one shared count; on a hot key that count was the ceiling for the whole cache.
2. **A GC-owned array per entry** (`04ef29d9`): the GC is the reference count, a hit writes nothing shared - one hot
   key at 24 threads went 11.5 → 149 M ops/s. But each entry became the object a generational GC handles worst:
   alive long enough to be promoted, then dead on eviction (~100 gen1 and 5-10 gen2 collections on the all-miss cell).
3. **Slabs** (`1bd841e6`): replies are copied, in fill order, into large arrays the GC owns. One slab is born in the
   large-object heap - nothing to promote, little to mark - and is never reused, only dropped, so a reader still
   parsing keeps it alive. Hits still write nothing shared.

Then: the **request is copied into the slab beside its reply** (`f3f9c958`; for a small reply the key is the larger
of the two, and a lookup that finds the key is beside the reply: hits ~+10% at 100k keys / 24 threads), and a
**single dependency is held inline** in the entry (`046f7a8e`; one dependent load fewer per hit, +3-5%).

Slab size is `MaxBytes / 32`, capped at 1 MiB, so evicting the oldest frees a fraction of the budget. Below 4 MiB
(128 KiB × 32) there are no slabs at all - a cache that small is not where GC churn matters, and it keeps exact
per-entry accounting. A reply over an eighth of a slab gets its own array.

### The budget

- `MaxBytes` is **memory held, not bytes carried**: whole slabs however few of their replies live, plus each entry's
  bookkeeping estimated at 256 bytes plus the request's length (`20b8a3de`). Before, a million cached counts reported
  ~5 MB while holding ~350 MB of entry objects: a budget that does not bind under the workload that needed it.
- `MaxEntries` for when the count itself is what to bound; `MaxPayloadBytes` for one reply.
- Enforced **after** a store: whether a reply fits is not known until it has arrived and been paid for, so it is
  stored and the cache evicts back to the budget - a target briefly overshot, not a wall.
- All three default to unbounded - the honest description of a client-side cache without one.

### Eviction

- **Bytes go by slab, oldest first** (`EvictSlabsToBudget`): evicting single entries frees no memory until their
  whole slab is empty. **One evictor at a time**: when every over-budget store evicted "the oldest", concurrent stores
  each took a slab where one was due, and a 32 MiB cache held ~11k 1 KiB entries instead of ~31k.
- Entries die in roughly the order they were born (TTL runs from the fill, and a hot key's refresh lands in the
  current slab), which is why FIFO-by-slab loses little - but it **is** FIFO: a hot entry in the oldest slab goes
  with it. Hits record nothing, deliberately (§12).
- **Entry-at-a-time eviction** (unslabbed replies, and `MaxEntries`) is **second chance over a queue of keys in
  store order** (`ae71b985`): the head is taken if dead, a live head examined and spared goes to the back, and the
  first live entry in a window of `EvictionSampleSize` (8) goes. It replaced sampling the dictionary, which had to
  walk to a moving offset - O(n) per eviction: removing the walk took the all-miss cell from 71k to 419k ops/s. Only
  entries that this path will read are enqueued; enqueueing every slabbed entry grew the queue without bound
  (`a5af2dec`).
- **Rejected:** true LRU (a write on every hit - Redis approximates its own LRU by sampling for the same reason);
  per-entry striped refcounts (~150 MB of padded counters); the pinned-object heap for entries (no better);
  finalizer-driven slab recycling (interior pointers outlive the wrapper, and finalizable objects are mid-life by
  construction).

## 9. Admission: only what is read again

**Decided:** `CacheAdmission.OnRepeatedMiss` is the default (`cc2c3624`, Marc, 2026-10-10): a first miss is
remembered, not stored; a request that misses again soon afterwards is cached. TinyLFU's doorkeeper. `OnFirstMiss`
restores the old behaviour; `Default` (zero) is "no opinion", resolved in one place, as for the tracking mode.

**Why:** storing is not free - a copy, dependencies registered, an entry kept until evicted - and on a workload that
rarely repeats, one-off reads cost a fill each *and* push out the entries that are re-read. Measured (Zipf, s = 0.99,
200k keys, 32 MiB of 1 KiB replies): 72.4% → 76.0-76.4% hit rate, and 1.07M → 1.45M ops/s at 64 callers. On a
workload that never repeats it removes the cache's overhead (all-miss 307k → 593k ops/s, GC pause 29% → 4%). It loses
only where every key is read exactly twice. RespFest (4.0.124): 75.9% vs 72.3%; 2.65M vs 2.10M ops/s at 64 callers.

**The doorkeeper** (`CacheDoorkeeper`): a Bloom filter, two probes from the request's hash after a MurmurHash3
32-bit finalizer, ~8 bits per expected entry (a power of two from 4 Ki to 16 Mi bits), sized from `MaxEntries`, else
`MaxBytes / 256` (the least an entry is charged), else 65,536. False positives only - an occasional first miss
admitted - never a hot request kept out. Racy on purpose: every race admits one miss early or late, nothing worse.

**Aged by every miss, cleared after an eighth as many as it has bits.** "Recently" is a window of misses, not of
time, which bounds what remembering costs at any keyspace size. It was first aged only by *first sightings*: then a
keyspace smaller than the window filled it once and was remembered forever, every miss read as a repeat, and
admission became `OnFirstMiss` for good. RespFest's Zipf cells (a replayed table of ~120k distinct keys against a
131,072 window) showed exactly that: 72% either way at 64 callers, with zero refusals in the model. Fixed in
`a22ba168`; RespFest's 1M and 16M tables now agree. A 64k window scored better still (76.7% vs 76.1%, modelled) and
was **not** taken: that is tuning to one workload.

**Known, accepted:** a refused miss is not registered in flight, so concurrent first misses on one request do not
coalesce (each was going to be refused anyway; the duplicate round trips are bounded by the doorkeeper's window).

**Open:** the gap to W-TinyLFU (Caffeine: a frequency sketch, and a candidate admitted only if more frequent than the
victim - which a replayed table does not fool). It needs a write per hit, against a hit path that writes nothing.
This is the "layered admission" question: doorkeeper first, frequency second. Model before code (`admitsim.py`),
on standard traces as well as Zipf.

## 10. Ageing: policy applied on read

**Decided** (§6.14): **options** are what change what we send or record (prefixes, budget, admission) and are fixed
per cache; **policy** is how we interpret what we hold, applied when an entry is **read**, never stamped at store - so
one shared entry serves callers with different tolerances. Stamping at store would cache identical replies once per
distinct lifetime.

- **`TimeToLive`** (1 minute): a safety bound against a *missed* invalidation, not a tuning knob, and it must be
  finite ("no TTL" is a policy, and it is infinity). The Redis docs say the same. A minute is comfortably longer
  than flush-on-disconnect plus keepalive takes to notice a dead connection.
- **`RefreshAfter`** (off): stale-while-revalidate. Between it and the TTL the entry is served *and* one reader
  claims a background refresh. Off by default: serving a value known to be old is a decision.
- **`InvalidationGracePeriod`** (off): serve an *invalidated* entry briefly while it refreshes - the more valuable of
  the two, because an invalidation hits every reader of a hot key at the same instant. Measured from the
  **invalidation**, not from first notice (a key nobody read for an hour should expire, not be resurrected); it is
  also the cap (on a hot-written key every refresh is invalidated before it lands). Never for our own writes. Within
  the TTL and the caller's `WithMaxCacheAge` - it shortens an entry's life, never extends it (`09bbeafc`). Costs a
  timestamp in the invalidation path only for a cache that asked for it.
- **`RespContext.WithMaxCacheAge`**: a per-caller freshness requirement - the one cache setting that could not be
  added to `IDatabase` without a binary break, and free on the context.

**The clock** (`0023c9e6`): every hit reads it for the TTL test. `Stopwatch.GetTimestamp` was ~15 ns of a ~134 ns
hit; `Environment.TickCount64` is ~4 ns at ~1-16 ms resolution, invisible against ages of seconds to hours.
Down-level keeps `Stopwatch` (no `TickCount64`, and the 32-bit one wraps every ~49 days). The grace window alone uses
the precise clock: it can be tens of milliseconds, and is read only for an invalidated entry.

## 11. Stampedes: single-flight and one refresh

(§6.15.) `RedundantFills` measured the problem before anything mitigated it.

- **Single-flight:** a miss registers itself in flight; a concurrent miss on the same request waits and then
  **re-probes** the cache rather than being handed the leader's payload (which would need racy retention, and
  re-probing gets the uncacheable case right for free). Sound because no observer can distinguish sharing from
  sending at the same moment - except for read-your-own-writes, so attaching is refused if any dependency moved since
  the leader sent. Remove-before-publish ordering, so a woken waiter cannot re-attach to a finished registration. The
  completion source is created only when someone actually waits (it was a large share of a miss's allocation).
  **Asynchronous callers only:** a synchronous send does not wait on another caller's in flight - that would be
  sync-over-async - so it sends its own copy, as before single-flight. A deliberate gap, closed if the executor
  gains a synchronous wait.
- **One refresh per entry:** the claim is a flag on the shared entry, taken atomically with the lookup, released on
  every outcome (a failed refresh that kept it would pin the entry stale until hard expiry, silently). A refresh is
  not registered in flight: a concurrent miss has nothing yet, and should fetch rather than queue behind a nicety.
- **Cancellation** applies only to the caller's await; the fill populates a shared cache, so it is worth finishing.

## 12. The hit path

What a hit does: hash the frame (borrowed, not detached), one dictionary lookup, validate one inline dependency
(dereference + compare), read the coarse clock, return a payload over the slab. Nothing shared is written: no
refcount, no recency, no frequency. Hits never touch the doorkeeper.

Measured: single-thread hit 134 → 114 ns over the 2026-10-10 work; asking the route for a constant on every cached
read had been ~30% of RespFest's cached throughput (`c5370268`). What remains is mostly the request builder
(~26 ns over a 4 ns floor; `design/parked/builder-inlining.patch`, unmeasured).

The trade this forces is explicit and runs through §8 and §9: **every policy that wants to know what was hit - LRU,
second chance at slab eviction, a frequency sketch - costs a write on this path.** None has yet shown enough to pay
for it.

## 13. Reclaiming memory

Invalidation stamps and expiry is decided on read, so neither frees anything. `Sweep` removes both kinds of dead
entry (an expired one is otherwise resident forever if nobody reads it again); `SweepIfDue` runs it from the
multiplexer heartbeat at most every `SweepInterval` (10 s), claiming the timestamp first so concurrent drivers make
one sweep. A cost property, not a correctness one: overlapping sweeps are already safe.

## 14. Open questions

From the ledger unless noted; none blocks shipping.

- **Admission beyond the doorkeeper** (§9): frequency against the victim, at the price of a write per hit.
- **Second chance at slab eviction** (§8): copy entries that were hit forward rather than evict them with their slab
  - a write-once "touched" flag per entry. Only if hot keys are seen churning out.
- **`RespKeyTable` never removes a node**, so it grows with every distinct key ever cached, not with the cache.
  Bounded by the keyspace, but unbounded by the budget.
- **The cached miss sends untyped and parses afterwards**, where an uncached send parses typed on the reader.
- **Slab recycling** via per-thread reader counters - only if large-object churn shows up as a cost.
- **A second tier of trust** (§6.14): when invalidation is not confirmed live, a much shorter TTL or no cache at
  all, and the client loud about which regime it is in. Today RESP2 refuses outright, so the question is open only
  for a tracking connection that is up but suspect.
- **`OPTIN`/`OPTOUT`** need the pipeline to write `CLIENT CACHING` and the read atomically, as it does a transaction.

Three descriptions that had drifted from the code were corrected with this file: `EvictionSampleSize` (it
described sampling by fill time), `CacheAdmission.OnRepeatedMiss` (the filter's window), and §6.14 of the
original notes (flush-on-disconnect and the TTL as unwired).

## 15. Measuring it: lessons paid for

- **Measure the hit rate at the server** (commands received ÷ operations), not from client counters that share the
  code under test. The 0%-under-writes defect (§5) was invisible from inside.
- **Bench on more than one key shape.** Zero-padded keys hashed fine; RespFest's unpadded `rf:cbig:{i}` exposed a
  hash that collided ~5× (§4).
- **Do not replay a key table.** A table replayed brings every one-off back, which is all a doorkeeper asks; it hid
  the admission gain, and hid the aging defect behind it (§9). `CacheHitScaling` now draws as it goes.
- **Do the arithmetic before the benchmark**: capacity ÷ keyspace bounds an all-miss cell's hit rate; ~25k 1 KiB
  entries fit 32 MiB.
- **Code layout moves the hit path by 20+ ns between builds** of any change to the probe function; interleave A/B
  runs and bisect before believing a few nanoseconds.
- Tools: `CacheHitSendBenchmarks` (`--inProcess`), `cache-scaling [s] host:port` (hits), `cache-scaling miss|zipf
  [s] host:port [nocache|admit]`, `design/admitsim.py` (admission and eviction, no Redis), and the RespFest cache
  league.
