# Queue — interpolated RESP writer / client-side caching

Working list for the `marc/interpolated-writer-design` branch. Kept separate from
`interpolated-resp-writer.md` so it can be edited without conflicting with the design notes, which are
long and appended to constantly.

**Convention:** items move to Done with the commit that closed them. Anything removed rather than done gets
a line saying why, because "we decided not to" is worth as much as "we did".

---

## Now

- [ ] **Free up the name `Execute`.** `RespContext.Execute(...)` currently returns a rendered `RespFrame` —
      it does not execute anything — while `IDatabase.Execute` in this same library *sends and returns a
      result*. Two opposite meanings for one verb, in one codebase. Rename the frame-returning one
      (`Render` reads right) and let `Execute` mean what everybody expects. ~73 call sites, entirely
      mechanical, but it will collide with any in-flight worktree, so do it immediately after a merge.
      Until then `ExecuteAsync` carries the ad-hoc API, because async has no clash.

- [ ] **Cacheability metadata for the seven exclusions** (§6.9). `SRANDMEMBER`, `HRANDFIELD`,
      `ZRANDMEMBER`, the `*SCAN` family, `TTL`/`PTTL`, `TOUCH`, `PFCOUNT` all sit in
      `CommandRetryReadOnly` alongside `GET` and would be cached wrongly today. A correctness hole, and
      small. `DUMP` wants a second opinion.
      **Narrowed, not closed, by `CachePolicy.Prefixes`.** These are *command*-shaped defects and prefixes
      are a *key-space* opt-in, so a non-deterministic command on a declared key is still cached wrongly.
      What changed is the blast radius: nothing is cached unless its key space was positively declared, so
      a caller who scopes tightly is no longer exposed on key families they never meant to cache at all.
      It does largely answer the module worry below for free — `FT.*` names indexes, and an index name is
      not usually in a data-key prefix list, so those replies now fall out as `RefusedNotTracked` rather
      than being cached with nothing to invalidate them.

## Next

- [ ] **`Parse(ref RespReader)`** (§2.2, §6.16). Smaller prize than it looked once the outgoing-copy rule
      landed — the sharing argument moved to `ReadOnlyLease` — so it is back to being about
      **composability**: `IRespHandler<T[]>` built from `IRespHandler<T>`. Cheapest while handlers live in
      one file. Mechanical: delete two lines per handler, take the parameter.

- [ ] **`CLIENT TRACKING` negotiation in the real client.** RESP3-only, `BCAST`, empty prefix by default
      (§6.13). Must refuse **loudly** when RESP3 is unavailable rather than silently caching without
      invalidation, and the `PREFIX` arguments must come from `CachePolicy.Prefixes` rather than a second
      list — the cache already refuses keys outside that set, so the two drifting apart would mean either
      caching what nothing announces, or refusing what something does.
      **Now the only thing left between `ClientCache` and a cache that works by itself:**
      hosting and routing are done, so a caller who sets the policy and never issues `CLIENT TRACKING`
      gets a cache that fills, expires on TTL, and is never invalidated — the exact silent-wrongness this
      item exists to prevent. Until it lands, `ConfigurationOptions.ClientCache` is experimental in the
      strong sense.

- [ ] **The rest of the `Execute` family on `TransitionalDatabase`.** `ExecuteResp`/`ExecuteRespAsync` are
      done (a pass-through; the signatures agree exactly). `Execute`/`ExecuteAsync` returning `RedisResult`
      need a `RespResult` -> `RedisResult` step, for which `RespReaderExtensions.ReadRedisResult` already
      exists. The `object[]`/`ICollection<object>` overloads lose key-ness to boxing, so they cannot cache;
      that is a property of the old signature, not something to fix here.

- [ ] **`StringGetLease` and the legacy-lease pattern.** The shape to follow: the new surface produces
      `ReadOnlyLease<byte>` (which may share), and the legacy adapter converts to `Lease<byte>` — a copy,
      honestly, because the legacy contract promises the caller owns the bytes. That conversion wants a
      pooled `ToLease()` on `ReadOnlyLease<T>` rather than `ToArray()`, which allocates outside the pool.
      Not added yet, deliberately: no caller, no API.

- [ ] **More command groups**, in `RespSurface.<Group>.cs` + `TransitionalDatabase.<Group>.cs` pairs.
      Mechanical now; `Strings` and `Bitmaps` are the worked examples. SER352 counts what is left.

## Later / decide first

- [ ] **Byte quota and eviction.** `MaxPayloadBytes` bounds one entry; nothing yet bounds the total. Count
      the *buffer*, not the payload: each reply is copied into its own `ArrayPool<byte>.Shared` rent, and
      the shared pool rounds to power-of-two buckets, so accounting on payload length under-counts by up to
      ~2x - which is the error that makes a quota fail to bind under the workload that most needs it.
      `RefCountedBuffer.Length` is the honest number. A secondary entry-count cap is worth having too,
      since the key table grows independently of payload bytes.
      On eviction policy: **LRU would tax the one path that is currently free.** A hit today is a dictionary
      lookup plus a refcount bump; recency tracking adds a write to every read. Redis approximates LRU by
      sampling for exactly this reason, and the same answer is available here. Whatever the policy, eviction
      must release exactly its own reference while readers hold theirs - `Release()` is a bare decrement
      with no idempotence guard, and `TryRemove` does not hand back the stored key.

- [ ] **Per-context `CachePolicy` override** (`WithCachePolicy`). The other half of the options/policy
      split: policy settings are read-time, so they can vary per call, and the override rides in the
      context's service slot exactly as `MaxCacheAgeService` does. `WithMaxCacheAge` stays as the
      ergonomic spelling of the common case rather than being subsumed.
      One wrinkle: `InvalidationGracePeriod` is not purely read-time. Whether grace is on gates a
      timestamp *write* in the invalidation path, which sees every key the server mentions. So arming it
      belongs on `CacheOptions` and only the duration can vary per context.

- [ ] **`IServer` / `ISubscriber` contexts** still throw from `IRespTarget.Context`.

- [ ] **The retry executor** (`WithRetry`). Prerequisites in place; no design written.

- [ ] **Should the existing `ResultProcessor` path copy too?** (§6.16). Sharing there is *correct* — it is
      single-owner — so this is a policy change, not a fix, and it would strand `TryReservePayload`,
      `IPayloadReservationProvider` and `PayloadReservation` as dead code now that `ReadLease` (read-only)
      is their only remaining consumer. Own merits or not at all.

- [ ] **Module-read tracking.** Do module reads register for invalidation? A five-minute experiment
      against a real server, never run. Relevant because the docs put the whole `FT.*` family outside
      server-side tracking — though a prefix list that names data keys already excludes index names, so
      the exposure now requires someone to have declared a prefix covering them.

- [ ] **`RespContext` sizing.** Currently 48 bytes. `CachePolicy` rides on the cache and the freshness
      override rides in the service slot, so nothing has grown it yet — but SWR adds knobs, and the
      measurement that justified moving `ChannelPrefix` out (§3.3) should be repeated rather than assumed.

## Done

- [x] Single-flight / request coalescing — `934e8d2d`
- [x] `CachePolicy` + finite entry lifetime, per-context `WithMaxCacheAge` — `9cf7da77`
- [x] Invalidation delivery proven against a real server (`TrackingExecutor`) — `4f02b657`
- [x] Push classification matching `PhysicalConnection` — `1f8e3cbd`
- [x] `RespResult` as a built-in result type (the NRedisStack path) — `4b4a7434`
- [x] `ReadOnlyLease<byte>`, and retiring the mutable `ReadLease` spelling — `9a1a37bf`
- [x] Ad-hoc `ExecuteAsync` returning `RespResult`, on the context and on `IRespTarget`; `ExecuteResp`
      wired through `TransitionalDatabase` — `30d28d70`
- [x] `RespResult` shares the reply buffer instead of copying it — `696a5c3f`
- [x] Interface-based default handler lookup; `IRespHandler` made invariant — `a539a538`
- [x] Stale-while-revalidate on expiry, with background refresh — `253cc2e4`
- [x] Invalidation grace period, with the read-your-own-writes carve-out — `6b94d588`
- [x] Flush the cache when a connection is lost — `f2811156`
- [x] Hosting the cache on the multiplexer (`ConfigurationOptions.ClientCache`), and routing real
      invalidation pushes to it through `PhysicalConnection` — `4d608ddd`
- [x] Refuse to cache keys outside the tracked prefixes: no announcement, no invalidation path — `e42c8d22`
- [x] Fire-and-forget is neither cached nor served; sync F+F no longer throws `"No reply."` — `abd87708`
- [x] Split `CacheOptions` (settled once: prefixes, budget) from `CachePolicy` (read-time, per-call) — `286a461a`
- [x] `MaxPayloadBytes`, and a sweep that actually runs: `SweepInterval` + the multiplexer heartbeat, and
      `Sweep` reclaiming expired entries rather than only invalidated ones — this change

## Decided against

- **A "buffer is shared" flag on `RespReader`**, and a second read-only reservation interface. Unnecessary
  once the *type* carries the distinction: the mutable path simply never calls `TryReservePayload`. §6.16.
- **`ReadOnlyLease<byte>` as a `struct`.** `RefCountedBuffer.Release()` is a bare decrement with no
  idempotence guard, so a copied-and-disposed-twice struct would return a live buffer to the pool while
  other holders still read it.
- **`NOLOOP` on `CLIENT TRACKING`.** In default mode the server stops tracking a key we wrote even when it
  suppresses the message, so anything whose key set we under-declare (`EVAL` with computed keys) goes
  *permanently* stale rather than briefly. §6.13.
- **Sharing a buffer into a `RedisValue`.** Tempting, because single-value replies are a large cohort and
  they are exactly the ones a cache serves. But `RedisValue` has no disposal, so it can never give a
  reference back — which leaves only "pin the buffer forever" or "let the pool reclaim it while the value
  still points at it". That is not a gap to be plugged; it is the absence of a lifetime, and `RespResult`
  exists because it *has* one.

- **Deriving `BCAST PREFIX` from `WithKeyPrefix`.** Prefixes are connection-global, must not overlap —
  context prefixes routinely nest — and cannot be removed individually. §6.13.
