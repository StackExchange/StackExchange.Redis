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

- [ ] **Stale-while-revalidate** (§6.15). Soft/hard thresholds on `CachePolicy`, once-only refresh via an
      interlocked flag on the entry, clearing on failure with backoff. Prerequisites are in: single-flight
      is the same interlock, and `CachePolicy` already carries the lifetime. Remember the cap for the
      compounding-staleness case, and that invalidation-SWR must **not** apply to invalidations we caused
      ourselves (read-your-own-writes).

- [ ] **Cacheability metadata for the seven exclusions** (§6.9). `SRANDMEMBER`, `HRANDFIELD`,
      `ZRANDMEMBER`, the `*SCAN` family, `TTL`/`PTTL`, `TOUCH`, `PFCOUNT` all sit in
      `CommandRetryReadOnly` alongside `GET` and would be cached wrongly today. A correctness hole, and
      small. `DUMP` wants a second opinion.

- [ ] **Wire `OnFlush()` to disconnect.** It exists and nothing calls it. Comes from the same server
      documentation that gave us the TTL backstop: *"if the connection is lost, the local cache is
      flushed"*. Currently the cache would serve entries invalidated while we were not listening.

## Next

- [ ] **`Parse(ref RespReader)`** (§2.2, §6.16). Smaller prize than it looked once the outgoing-copy rule
      landed — the sharing argument moved to `ReadOnlyLease` — so it is back to being about
      **composability**: `IRespHandler<T[]>` built from `IRespHandler<T>`. Cheapest while handlers live in
      one file. Mechanical: delete two lines per handler, take the parameter.

- [ ] **Route invalidation pushes through `PhysicalConnection`** (§6.13). Two known changes, not guesses:
      a `[AsciiHash("invalidate")] Invalidate` member on `PushKind`, and handling it **before** the
      `TryMoveNextString` gate — that gate demands an inline string second element (the pub/sub channel),
      whereas an invalidation's is an array or a null, so the enum member alone changes nothing.
      `TrackingExecutor` in the tests is the known-good target to match.

- [ ] **`CLIENT TRACKING` negotiation in the real client.** RESP3-only, `BCAST`, empty prefix by default
      (§6.13). Must refuse **loudly** when RESP3 is unavailable rather than silently caching without
      invalidation.

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

- [ ] **`IServer` / `ISubscriber` contexts** still throw from `IRespTarget.Context`.

- [ ] **The retry executor** (`WithRetry`). Prerequisites in place; no design written.

- [ ] **Should the existing `ResultProcessor` path copy too?** (§6.16). Sharing there is *correct* — it is
      single-owner — so this is a policy change, not a fix, and it would strand `TryReservePayload`,
      `IPayloadReservationProvider` and `PayloadReservation` as dead code now that `ReadLease` (read-only)
      is their only remaining consumer. Own merits or not at all.

- [ ] **Module-read tracking.** Do module reads register for invalidation? A five-minute experiment
      against a real server, never run. Relevant because the docs put the whole `FT.*` family outside
      server-side tracking.

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
      wired through `TransitionalDatabase` — this change

## Decided against

- **A "buffer is shared" flag on `RespReader`**, and a second read-only reservation interface. Unnecessary
  once the *type* carries the distinction: the mutable path simply never calls `TryReservePayload`. §6.16.
- **`ReadOnlyLease<byte>` as a `struct`.** `RefCountedBuffer.Release()` is a bare decrement with no
  idempotence guard, so a copied-and-disposed-twice struct would return a live buffer to the pool while
  other holders still read it.
- **`NOLOOP` on `CLIENT TRACKING`.** In default mode the server stops tracking a key we wrote even when it
  suppresses the message, so anything whose key set we under-declare (`EVAL` with computed keys) goes
  *permanently* stale rather than briefly. §6.13.
- **Deriving `BCAST PREFIX` from `WithKeyPrefix`.** Prefixes are connection-global, must not overlap —
  context prefixes routinely nest — and cannot be removed individually. §6.13.
