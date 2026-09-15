# Queue — interpolated RESP writer / client-side caching

Working list for the `marc/interpolated-writer-design` branch. Kept separate from
`interpolated-resp-writer.md` so it can be edited without conflicting with the design notes, which are
long and appended to constantly.

**Convention:** items move to Done with the commit that closed them. Anything removed rather than done gets
a line saying why, because "we decided not to" is worth as much as "we did".

---

## Framing: this is V4, and the two implementations never run in parallel

Decided 2026-09-15, and it changes how several items below should be read. The spike is not a parallel
alternative that might ship - it is the next major version's core, and the old implementation goes.

Four consequences, none of them cosmetic:

1. **The old `IDatabase` surface still exists; the old *implementation* does not.** Binary compatibility is
   paramount here, so the signatures stay and are served by the new core, and people are *led* to the new
   surface rather than pushed. That makes the internal `...Array` siblings **permanent fixtures**, not
   scaffolding, and their doc comments now say so.

   **`Message` stays too**, and for a better reason than inertia: it is abstract over exactly two members,
   `ArgCount` and `WriteImpl`. Everything else it carries - db, flags, command, slot, status, timeouts,
   result pairing, high-integrity, profiling - is concrete shared bookkeeping. So the core is already
   pluggable at precisely the rendering step, which is the only step the frame path wanted to replace, and
   a new `IMessage` would only re-spell a seam that is already two members wide. What V4 changes is not the
   abstraction but the population: 36 `Message` subclasses exist mainly to implement `WriteImpl` for one
   command each, and every command that moves to the writer makes one of them redundant. The end state is a
   **deletion**, not a reconciliation. The innards may evolve once sync is no longer a requirement; that is
   deferred, and it is a smaller cut than it first looked.

   `ArgCount` shrinks with them. Its consumers are the `REDIS_MAX_ARGS` guard, composite forwarding, and
   per-command arithmetic that lives in the subclasses being deleted - so it stops being something each
   command must compute correctly and becomes a field the frame already holds, having counted while
   writing. That is the `IRespArgument`-over-`RespFragment` argument again: a count taken during the write
   cannot disagree with the bytes.

2. **`Fallback<T>()` must reach zero.** `TransitionalDatabase` currently delegates transactions to
   `RedisDatabase`. With nothing to delegate to, `MULTI`/`WATCH`/`HIMPORT`/`EVALSHA` stop being acceptable
   permanent residue and become **release blockers**. Deferring them is a temporary state with a mandatory
   exit, not an end state.

3. **The `Message` layer is load-bearing, and a second write path is not merely unattractive - it is
   incoherent.** The backlog is one `ConcurrentQueue<Message>` per bridge, and `FrameMessage : Message`, so
   frames *already* participate in ordering, backlog replay, retry accounting and the reconnect handshake.
   Two owners of one socket's write path cannot share any of those. So "build a raw executor beside the
   shim" is not an option at all, and the shim is not a hack to escape - it is what lets frames have those
   properties for free.

   **Which means the vexing commands need composition, not a new path.** Both mechanisms already exist in
   that layer: `IMultiMessage` (one logical message expanding into several, written as a unit - this is how
   `TransactionMessage` does MULTI/EXEC today) and write-lock injection (a connection-local preamble decided
   once the connection is known - this is how `HashImport` does PREPARE). What the frame surface lacks is a
   frame-shaped participant in each. That is a far smaller and strictly additive piece of work than a raw
   executor, and none of MOVED/ASK, backlog, timeouts, retry, profiling or high-integrity has to be
   reimplemented, because we never leave the path that already has them.

4. **`[Experimental]` is a staging label, not a hedge.** "We can change it later because it is
   experimental" stops being true. Public shapes - lease returns, group names, `Keys` over `Keyspace` -
   are effectively permanent from here, which raises the value of settling them now and lowers the value of
   leaving options open. SER352's 354 members stop being a progress bar and become a release gate, which
   is what the Release-build warning was asked for in the first place.

---

## Now

## Next

- [ ] **Three probes, one per layer: `EVALSHA`, `MULTI`, `HIMPORT`.** These look like three awkward
      commands and are better understood as three *different seams*, which is why doing all three settles
      the question and doing one does not.

      - **`EVALSHA` is composition too, not frame-level** - revised, and it is the better answer. The
        original plan was a frame carrying an **alternate rendering**, recovering a `NOSCRIPT` by
        re-spelling itself as `EVAL <script>`. Instead: when the script is not known to be loaded, send
        `SCRIPT LOAD` **and** `EVALSHA` as a unit, and always issue `EVALSHA`.

        The virtue is not the saved round trip. An alternate rendering would be the first exception to *a
        frame is a pure function of its arguments*, and that property is what makes frames cacheable,
        routable, replayable and blit-writable. This keeps it: both halves are ordinary frames, and the
        cleverness moves to composition, where the other two already live. **So there is no frame-level
        probe at all, and the frame abstraction is untouched by the whole vexing family.**

        It does not remove the `NOSCRIPT` path - belief goes stale on `SCRIPT FLUSH`, restart, failover, or
        a new cluster node - but recovery becomes *recompose*, not *re-render*, so that is composition too.

        **The belief is soft**, which is what makes it safe: wrongly believing it is loaded costs a
        `NOSCRIPT` and a recompose; wrongly believing it is not costs a redundant `SCRIPT LOAD`, which is
        idempotent. It can never be damagingly wrong, so it needs no careful invalidation - and none exists
        today, since the client currently tracks no loaded-script state at all.

        **Two corrections from reading the existing code.** This is not a new design: `ScriptEvalMessage`
        is *already* an `IMultiMessage` that yields a `ScriptLoadMessage` and then itself, choosing
        `EVALSHA` or `EVAL` in `WriteImpl` from whether the hash is known. And the client *does* track
        loaded scripts - `ServerEndPoint.knownScripts`, so per **endpoint** rather than per connection,
        which is the right scope because the server's script cache is server-wide. So the frame surface is
        not inventing the pattern, it is joining one; what is missing is only a frame-shaped participant.

        **A cold-start saving was proposed here and withdrawn.** `EVAL` does populate the server's script
        cache by itself - measured on 8.9.241, and documented: *"every script you execute with EVAL is
        stored in a dedicated cache that the server keeps"*. So first use could in principle be a bare
        `EVAL`, one command instead of two, with `EVALSHA` thereafter.

        Against it: from Redis 7.4 the server *"evicts scripts loaded with `EVAL` or `EVAL_RO` from the
        script cache when the cache reaches a certain size"*, least-recently-used first - and that sentence
        does not extend to `SCRIPT LOAD`. So the saving would trade a durable load for an evictable one and
        make `NOSCRIPT` more likely under pressure. Still correct, because recovery handles it, but worse
        exactly when the cache is busiest.

        Note the same page's older "Script cache semantics" section still says scripts *"are meant to stay
        indefinitely in the cache"*, which 7.4 contradicts; the command page is the newer text.

        The docs also endorse the existing shape directly: `SCRIPT LOAD` is *"useful in all the contexts
        where we want to ensure that EVALSHA doesn't fail (for instance, in a pipeline or when called from
        a MULTI/EXEC transaction)"* - which is both why `ScriptEvalMessage` is an `IMultiMessage` and
        independent confirmation of the transaction constraint below.

        **Stage it stateless first:** always pair `SCRIPT LOAD` + `EVALSHA`. Correct, one round trip, no new
        state, and it proves the composition mechanism. Belief-tracking is then a pure optimisation that
        drops the `SCRIPT LOAD`, layered on something already correct rather than being load-bearing.

        **Cluster:** `SCRIPT LOAD` names no key, so the pair must route by the `EVALSHA`'s slot - which
        composing them as one unit gives for free, and is a reason to compose rather than inject.

        **Inside a transaction it has to move outward.** Injecting `SCRIPT LOAD` *within* `MULTI` puts its
        reply into the `EXEC` array and shifts every result position, so it belongs before the `MULTI` -
        meaning the decision is the composite's, not the inner `EVALSHA`'s write. Nothing exposes that seam
        today; HIMPORT's write-lock injection is per-message. A constraint on the composite design, not a
        blocker.
      - **`MULTI` is context-level**, and needs no new caller-facing concept: a transaction is *a context
        whose executor accumulates instead of sending*. `WithExecutor` is already internal for exactly this
        - the caller picks a scope (`CreateTransaction`), the scope picks the executor. On `ExecuteAsync`
        the parked frames become one `IMultiMessage`, which is how `TransactionMessage` already works.
        `WATCH` is the hard minority: it needs a round trip before deciding what to send.
      - **`HIMPORT` is bridge-level.** It needs a connection-local preamble injected once the connection is
        known - which already exists, inside the bridge's write lock, and is what lets `HashImport` avoid
        pinning a connection at all. The frame surface just has no frame-shaped participant in it.
        `CLIENT TRACKING` negotiation wants the same mechanism.

      With `EVALSHA` folded in, all three are the same mechanism at two seams - compose, or inject once the
      connection is known. If they work, the abstraction covers the space and `Fallback<T>()` can reach
      zero. If one does not, we learn which seam is short, rather than that "some commands are awkward".


- [ ] **Teach the in-proc server `CLIENT TRACKING`** (`toys/StackExchange.Redis.Server`), for test
      isolation: the cache suite currently needs a shared 6379, where one test's `FLUSHDB` reaches every
      other test's tracking connection. Most of the seams already exist - `RespServer.Touch(db, key)` is
      already a virtual broadcast to every client on every non-readonly key access, `node.OnOutOfBand`
      already delivers pushes for pub/sub, `TypedRedisValue.Rent(n, out span, PushKind)` builds the frame,
      and the writer already handles `RespPrefix.Push when value.IsNullArray`, which is the flush shape.
      New: parse `CLIENT TRACKING ON|OFF [BCAST] [PREFIX p ...]` into per-client state, and fan out.

      **Timing is the part to get right, and it is measured rather than guessed (6.13).** Key
      invalidations are *accumulated across the write cycle* and emitted **after** the replies - two
      pipelined `SET`s produce one two-key push after both `+OK`s - so the fake needs an accumulator
      flushed at the end of a batch, not a send inside `Touch`. `FLUSHDB` is the exception: its
      `invalidate null` goes out **before** its own `+OK` - though the fake may emit it after, and that
      is a deliberate, recorded divergence rather than an oversight: the ordering only matters to the client
      doing the flushing, which has already called `OnFlush` locally, and everyone else receives it
      unsolicited where ordering means nothing.

      Per-key (non-`BCAST`) mode is nearly as cheap - `OnKey` already runs per key with a `ReadOnly` flag -
      and is worth having because it is the mode whose "server forgets the key once it has told you"
      behaviour the `NOLOOP` argument rests on.


- [ ] **`Parse(ref RespReader)`, and the row-parser collapse** (§2.2, §6.16). Now with evidence rather
      than a hunch: converting the arrays produced **16 handlers that are all "aggregate of X"** - eight
      array, eight lease - of which six differ only by a one-line projection, which is why they were
      factored onto a shared `ReadScalarLease`. With `Parse(ref RespReader)` the row parser *is* the scalar
      handler: `IRespHandler<long>` for `INCR` does `ReadInt64()`, and so does the row parser for
      `ReadOnlyLease<long>`. `double?` is character-for-character identical. So registration becomes
      **implement the element handler, get the aggregate/lease/array for free** - the natural extension of
      "registration is implementing the interface", and the story for module types.

      **Jagged vs interleaved belongs in the walker, not the row parser.** `HashEntry`/`SortedSetEntry`
      look like exceptions because a row is two interleaved elements in RESP2 and one nested array in
      RESP3 - but the old parser already hides that from its implementers entirely: `ParseArray` decides
      `isJagged` once per reply and runs one of two loops that both call the same `Parse(ref first, ref
      second, state)`. So the generic walker normalises, the row parser declares an **arity** (1 for
      scalars, 2 for pairs), and all eight collapse rather than six. Carry the escape hatch across too:
      `AllowJaggedPairs` is `protected virtual`, and `RedisStreamInterleavedProcessor` overrides it to
      false because it works on an already-flattened map.

      The shape is endemic - `HGETALL`, `ZRANGE WITHSCORES`, `XRANGE`, `CONFIG GET` - so hoisting it once
      pays on every group added, and leaving it per-handler means re-deriving the jagged check each time.

      A refactor that **deletes** code. Deliberately sequenced after the signature change: the public
      shapes were binary-breaking and time-limited, the handler internals are internal and can be
      collapsed whenever without touching a caller.

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

- [ ] **The `OBJECT` family and `DBSIZE`.** Deferred out of the `Keys` group: `OBJECT ENCODING/REFCOUNT/
      FREQ/IDLETIME` are a different command shape, better done together, and `IDLETIME` will want
      `.NeverCached()` for the same reason `PTTL` does. `DBSIZE` is an `IServer` command and belongs to
      that context, not to `Keys`.

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
- [x] MGET returns a pooled lease; the array shape moves to an internal sibling — `ae3abb1e`
- [x] Measured invalidation timing against a real server (6.13) — `eddb3b5f`
- [x] Wire `OnLocalWrite`: a write tells the cache before it is sent — `b21ad97a`
- [x] A bulk write invalidates its own arguments, not the whole cache — `bbb91af6`
- [x] Arrays off the new API: 28 returns become `ReadOnlyLease<T>`, with internal `...Array` siblings — `9625bde1`
- [x] Cacheability exclusions: `.NeverCached()` on the random readers and `HPTTL` — `28d7fa3d`
- [x] The two second opinions, both confirmed: `[H]PEXPIRETIME` cacheable (an instant does not drift),
      `DUMP` cacheable but documented as rarely worth it — this change
- [x] The `Keys` command group, and awaiting the flush `CountKeys` depended on — this change
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
