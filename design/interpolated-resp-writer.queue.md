# Queue — interpolated RESP writer / client-side caching

Working list for the `marc/interpolated-writer-design` branch. Kept separate from
`interpolated-resp-writer.md` so it can be edited without conflicting with the design notes, which are
long and appended to constantly.

**Convention:** items move to Done with the commit that closed them. Anything removed rather than done gets
a line saying why, because "we decided not to" is worth as much as "we did".

---

## Now

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

- [ ] **Get the arrays off the new API.** There should be very close to zero. Counted at the start: 32 array
      occurrences on the `SER010`/`SER011` surface, of which **29 are `ValueTask<T[]>` returns** -
      `RedisValue[]`, `HashEntry[]`, `SortedSetEntry[]`, `double?[]`, `long[]`, `bool[]`,
      `ExpireResult[]`, `PersistResult[]`. Inherited wholesale from the old surface, where there was no
      alternative; here there is.
      The *inputs* were already done right - `ReadOnlySpan<T>` throughout - so this is one-sided, and it is
      the side that allocates per call with nothing able to reclaim it.

      **Why now and not later.** An array return is a binary-compat trap of its own: `T[]` can never
      become anything else without a break, so the experimental window is the only chance. And the cost
      grows with every command group added - each new group written in the old shape is more to undo, and
      groups are being added right now.

      **The shape.** A return cannot be a span, because these are all `async`; it has to be something that
      carries a count and can be given back. `ReadOnlyLease<T>` already exists for exactly this reason and
      already solved the hard part (see 6.16 - `Release()` is a bare decrement, which is why it is a class
      and not a struct). The value-type element arrays are the sweetest: `double?[]`, `long[]`, `bool[]`,
      `ExpireResult[]`, `PersistResult[]` pool with *no* element allocation at all. For `RedisValue[]` the
      lease saves the array and not the elements - `RedisValue` has no lifetime, which is settled and not
      to be relitigated - but on a large `MGET` the array is the part that lands in gen-2.

      **The honest cost:** a lease must be disposed and an array need not be, so this trades forgiveness
      for reclaim. That trade is already made elsewhere in this design (`RespResult`, `ReadOnlyLease<byte>`),
      so the inconsistency today is that these were left behind, not that changing them is novel.

      **The one real exception:** `RespAttribute` - `params string[]` and `Tokens`. Attribute arguments
      must be arrays; the CLR gives no choice. Worth stating so it is not "fixed" by someone later.

      **Worked example landed:** `Strings.Get(keys)` (MGET) now returns `ReadOnlyLease<RedisValue>`, with
      an internal `GetArray` sibling for `TransitionalDatabase`, and `RespHandlers.ValueLease` beside
      `RespHandlers.Values`. 28 array returns left, all the same transformation. Prerequisite found and
      fixed on the way: `ReadOnlyLease<T>` was returning pooled arrays unwiped, which is right for `byte`
      and retention for any `T` holding a reference.

      It does not need `Parse(ref RespReader)` after all - a handler can fill a rented span from a span
      reply perfectly well - but the two still compose, and doing the remaining groups after that lands
      would avoid touching each handler twice.

      **Satisfying the old API, which still says `T[]`.** `TransitionalDatabase` has to keep returning
      arrays, so something has to bridge. The obvious move - give `ReadOnlyLease<T>` an internal "hand me
      your buffer" escape hatch - **does not work**, and it is worth saying why before someone tries it:
      `Rent` goes to `ArrayPool<T>.Shared`, which returns an *oversized* array, while the old contract
      promises an exactly-sized one the caller owns. The steal could essentially never fire. So the variant
      is not a method on the lease; it is a question about how the result is *built*, which is a question
      about the handler.

      Shape: **a parallel internal extension method on the typed context**, sitting beside the public one,
      sharing the message construction and differing only in the handler:

      ```csharp
      public static ValueTask<ReadOnlyLease<RedisValue>> Get(this in RespStrings strings, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
          => strings.Context.SendAsync($"{RedisCommand.MGET}{keys}", flags, RespHandlers.ValueLease);

      internal static ValueTask<RedisValue[]> GetArray(this in RespStrings strings, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
          => strings.Context.SendAsync($"{RedisCommand.MGET}{keys}", flags, RespHandlers.Values);
      ```

      Three things this buys over a generic `GetCore<T>(..., IRespHandler<T>)` helper. It stays in the
      classic `this` extension form, so the legacy sibling retires the way everything else on this surface
      does. Being **internal**, it never appears on the public API, so it adds no array site to fix later.
      And `TransitionalDatabase` stays a genuine one-line pass-through - `context.Strings.GetArray(...)` -
      which was the whole point of that class.

      Sharing the construction is available now that `Render` exists: it is exactly the primitive both
      siblings need, since each call wants its own frame and what is shared is the *composition*, not the
      frame. Duplicating the interpolated line instead is one line and no knowledge, so either is fine.

      `RespHandlers.Values` (`IRespHandler<RedisValue[]>`) already exists and is one of the three non-return
      array sites: it is not deleted, it is demoted - off the public surface, onto the legacy sibling.

      Rejected, and recorded so nobody builds it: an internal "hand me your buffer" hatch on
      `ReadOnlyLease<T>`. `Rent` goes to `ArrayPool<T>.Shared`, which returns an *oversized* array, while
      the old contract promises an exactly-sized one the caller owns - so the steal could essentially never
      fire, and what is left is `ToArray()` wearing a disguise. It would also put an ownership ambiguity
      into the one type whose entire point is that ownership is unambiguous.

      `ToArray()` stays public on the lease regardless - that is the escape hatch for *callers* who want an
      array, and it copies, honestly and visibly.


- [ ] **`Parse(ref RespReader)`** (§2.2, §6.16). Smaller prize than it looked once the outgoing-copy rule
      landed — the sharing argument moved to `ReadOnlyLease` — so it is back to being about
      **composability**: `IRespHandler<T[]>` built from `IRespHandler<T>`. Cheapest while handlers live in
      one file. Mechanical: delete two lines per handler, take the parameter.

- [ ] **`CLIENT TRACKING` negotiation in the real client.** RESP3-only; the mode and prefixes now come
      from `CacheOptions.TrackingMode` / `CacheOptions.Prefixes`, which are already validated against each
      other (§6.13). Must refuse **loudly** when RESP3 is unavailable rather than silently caching without
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
- [x] `Execute` -> `Render` on the context: rendering is not executing — `682cc687`
- [x] Wipe pooled arrays whose elements can hold references — `2c905046`
- [x] MGET returns a pooled lease; the array shape moves to an internal sibling — this change
- [x] `CacheTrackingMode`: broadcast vs per-key, with prefixes validated against it — `728e9102`
- [x] Byte and entry quotas, with sampled eviction — `87d5afa2`
- [x] `MaxPayloadBytes`, and a sweep that actually runs: `SweepInterval` + the multiplexer heartbeat, and
      `Sweep` reclaiming expired entries rather than only invalidated ones — `e2d2ea3c`

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
