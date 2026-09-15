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

- [x] **`*Async` suffixes on the new surface.** MSFT review, 2026-09-15; done the same day. Checked whether the suffix would
      be redundant: the surface is async-only (22 `ValueTask` returns in Strings alone, zero sync twins), so
      the *compiler* never needs it - but the justification is the reader, not the compiler, and
      `db.Strings.Get(key)` sitting beside `db.StringGetAsync(key)` makes you check a return type to know
      what you are holding. Mechanical across 11 groups, free while SER010 is experimental, expensive after.
      Agreed, and done in the same pass as the cancellation change below, since both touch every signature.

- [x] **`CancellationToken` moved off the context onto the call** - see the Done entry. Recorded here
      because one idea was raised and rejected with evidence: having the **interpolated string handler**
      take the token via `[InterpolatedStringHandlerArgument]`, so `ThrowIfCancellationRequested` runs
      before anything is formatted.

      It works mechanically, but forces the token **before** the interpolated string at every call site -
      `CS8950`, "Reorder the arguments to move 'cancellationToken' before 'handler'" - which is the opposite
      of the convention being asked for. Note the declaration compiles fine and only the *call* fails
      (`CS8947` warns at the declaration), so "it compiled" is not evidence here.

      Rejected because the surface does not need it: `db.Strings.GetAsync(key, flags, token)` has no
      interpolated parameter at the call site - the interpolation is inside the method body, where a
      pre-format check is free and needs no voodoo. The tension only ever applied to the ad-hoc
      `SendAsync($"...", flags, handler, token)` escape hatch, and there it would trade the convention for
      one rent-and-dispose on a call that is about to throw anyway.

- [x] **(superseded) Move `CancellationToken` off the context.** MSFT review; done 2026-09-15.
      Conventional, per-call, and it shrinks the context (see the sizing item). **But it is two decisions,
      not one:** the token currently *cancels nothing*. `RespMessageExecutor.SendAsync` says so - "the
      existing pipeline has no cancellation; the token is observed by the caller's await". On a context that
      reads as configuration; on every method signature it reads as a promise. So either wire it into the
      pipeline or document plainly that it is observed at the await - do not ship per-call tokens that do
      not cancel.

      **Resolved by taking neither branch.** A token that cannot cancel is not documented away and not
      silently observed: `Send`/`SendAsync` take one, and a token that *can* be cancelled throws
      `NotImplementedException` naming the reason. An already-cancelled token is honoured, because that one
      genuinely can be - and it recycles the command first, so the obvious no-op does not leak a pooled
      buffer. Wiring it into the pipeline is the real fix and belongs with the `Message` refactor.

- [ ] **Degraded state: may the cache prefer stale to offline?** MSFT review. Today it does the opposite,
      and deliberately: `OnConnectionFailed` calls `ClientCache.OnFlush()` *first*, synchronously, because
      "server-assisted invalidation only works while we are listening, so anything that changed during the
      gap is never announced and an entry that survives it is stale with nothing left in the system that
      will ever say so".

      That reasoning is about an **unannounced** gap. A planned maintenance is different in kind, and the
      library already has the signal: `AzureNotificationType` gives `NodeMaintenanceScheduled` ->
      `NodeMaintenanceStarting` (~20s) -> `NodeMaintenanceStart` (<5s) -> ended. The window is announced on
      both sides and therefore **bounded**, which is the property an unplanned drop lacks.

      **Suggested framing: the flush is not skipped, it is deferred to the end of the announced window.**
      During a signalled window, serve stale knowingly rather than going offline; when the window closes,
      flush and rebuild. That keeps the invariant - nothing survives an un-listened gap indefinitely - while
      buying the availability the review is asking for. Requirements: opt-in, a bounded maximum stale age
      distinct from TTL, and no path by which an entry outlives the window. Stale-while-revalidate and grace
      periods already exist to build on.

      Open: whether an unsignalled drop *during* a signalled window reverts to flush-immediately (probably
      yes - the signal said what would happen, and this is not it).

- [x] **Down-level consumers: a `Downlevel` namespace of method-shims. Investigated and built 2026-09-15.**

      The commands are already classic `this in` extension methods, so they bind everywhere. Only the
      **group accessors** (`db.Strings`) are extension-block properties, and those need **C# 14**. Shipped:
      the properties stay in `StackExchange.Redis.Interpolated` always, and method-shims (`db.Strings()`)
      live in an opt-in `StackExchange.Redis.Interpolated.Downlevel.RespGroups` - 22 of them, one per group
      per receiver (`IRespKeyspaceTarget` and `in RespContext`).

      **Measured across four real toolchains** - not by pinning `LangVersion`, which is not the same thing
      (see the warning below). Each built a consumer with the shim *and* the property both in scope:

      | toolchain | langver | result |
      | --- | --- | --- |
      | Mono msbuild 16.10, net472 | 7.3 | succeeded |
      | .NET SDK 6.0.428, netstandard2.0 | 10 | succeeded |
      | .NET SDK 8.0.425, net8.0 | 12 | succeeded |
      | .NET SDK 11 preview, net10.0 | 14 | **CS9339**, ambiguous |

      So: **extension-block metadata is inert to a down-level compiler** - not merely unusable, invisible.
      Having the properties always in scope costs those consumers nothing.

      The three failure modes are all compile-time, all actionable, and none can misbehave at run time -
      both spellings construct the same value over the same context:

      - up-level importing `Downlevel` -> `CS9339`, naming both members. Up-level implies a modern SDK, so
        an analyzer can always catch this one.
      - down-level *without* `Downlevel` -> `CS1061`, which already ends "are you missing a using directive
        or an assembly reference?". **The native message is the fix**, so the analyzer is a nicety here
        rather than load-bearing - which is the answer to "does our analyzer even load on an old SDK".
      - down-level *with* `Downlevel`, writing `db.Strings` -> `CS0119` "is a method", i.e. add the parens.

      **`[OverloadResolutionPriority]` does not help** - tested. `CS9339` is extension *member lookup*
      between a property and a method group, which never reaches overload resolution.

      **No reshuffle was needed** - an earlier draft of this entry claimed one, and the probes disproved it.
      Only the *shims* move to their own namespace; the accessors, the commands and the shared types
      (`RespStrings` and friends) all stay where they are. A down-level consumer imports **both** namespaces
      - which is exactly the combination all four toolchains above compiled - and only an up-level consumer
      must leave `Downlevel` alone. So the cost per new command group is one shim line per receiver, not a
      public move.

      **Generating the shims was raised and declined** (2026-09-15), so it does not get re-proposed:
      **generators cost build time on every consumer build**, and analyzers already account for ~40% of a
      clean build here - which is why they are limited to one TFM. Paying that on every build to save
      writing one line per group is the wrong trade.

      The shims are written by hand and **kept honest by a unit test** rather than a generator:
      `RespDownlevelShimTests` asserts that every group accessor on each receiver has a matching `Downlevel`
      shim, and that a shim composes with the commands that hang off it. Same shape as
      `RespTargetSplitTests.NoGroupBindsToTheBareTarget` - the rule is enforced, the build stays fast, and a
      missing shim fails a test rather than silently shipping. (Verified by mutation: deleting one shim
      *and* its API entry - deleting only the shim does not compile, so it proves nothing - fails exactly
      that one test.)

      **Do not validate this by pinning `LangVersion`.** A modern compiler at `/langversion:12` reports
      `CS9202`+`CS9339` where a *real* C# 12 compiler succeeds: it still sees the metadata and then refuses
      the feature, where an old compiler never sees it. The emulation is stricter than reality, so anyone
      reproducing it that way will find failures no real consumer has. (Noted because the first round of
      evidence here was exactly that mistake - and worse, a `Microsoft.Net.Compilers.Toolset` pin meant to
      give a genuine old compiler silently never engaged, so the results were the modern compiler all
      along. Docker images of the real SDKs are the honest instrument.)

      Probe artefact worth knowing: the net472 consumer needs `Microsoft.Bcl.AsyncInterfaces` and
      `System.Memory` at the library's pinned versions to bind `ValueTask`; real consumers get those
      transitively, but a netfx consumer pinning older ones hits `CS1705` before any of this matters.

- [ ] **`WATCH`/`MULTI` is BLOCKED on the `Message` refactor — do not start it first.** The measurement is
      taken (`0ac297fe`): a condition makes `ExecuteAsync` block the *calling* thread for two round trips
      (sync 508ms vs 2ms without), because the expansion is enumerated inside a sync `WriteMessageInsideLock`
      and waits there on `Monitor.Wait`. The target is "release the thread, keep the connection reserved",
      and the reservation half is already expressible: `_singleWriter` is an `AwaitableMutex`, not
      thread-affine, so it can be held across an `await`.

      **What blocks it is the completion, and that is the refactor's to give.** The pulse goes away when
      `Message` moves onto a poolable core with `IValueTaskSource` - which *is* an awaitable completion,
      correctly armed, for free. Building an awaitable pulse-replacement now would entrench the very thing
      being deleted, including its awkward "arm the monitor before sending so the pulse cannot be missed"
      property, which would then have to be un-entrenched.

      Left to do once the refactor lands: an async expansion for `TransactionMessage` (the other four
      implementors never wait), an async `WriteMessageInsideLock` for the two async call sites of four - the
      sync write and the backlog drain stay as they are, and a sync `Execute()` caller has a thread to block
      by definition. Re-measure against the 508ms.

      **Rejected, and worth not re-deriving:** doing the condition check *before* taking the write lock, so
      nothing has to await inside it. `WATCH` and the conditions are sent before `MULTI` anyway, so it looks
      free - but `EXEC`/`DISCARD`/`UNWATCH` are connection-global, so another transaction completing on the
      same connection between our `WATCH` and our `MULTI` would silently clear our watch. The lock is what
      makes the watch mean anything.

- [ ] **The `IMultiMessage` map is complete, and finishing it found a bug.** There are exactly five:
      `TransactionMessage`, `ScriptEvalMessage`, `ScriptEvaluateMessage`, `StringGetWithExpiryMessage`,
      `FramePairMessage` - plus `HashImport`, which is *not* one (its preamble is injected by the bridge).
      All six are now pinned by `MultiMessageInTransactionTests`; the file previously covered five of them
      and read as covering all, because the two script paths look like duplicates and only one was tested.

      **The gap was load-bearing.** `ScriptEvalMessage.WriteImpl` branched on two cases where there are
      three: a hash it resolved, a hash the **caller** supplied, and a body. The middle case fell into the
      last, which is unreachable only while the expansion always runs - and inside a transaction it never
      does, because `QueuedMessage` is not an `IMultiMessage` and never asks. So
      `tran.ScriptEvaluateRespAsync(someSha1, ...)` sent `EVAL <the hash text>`, and the server tried to
      compile it as Lua: *"ERR Error compiling script (new function)"* instead of `NOSCRIPT`. A misleading
      error, and not the command the caller asked for. `ScriptEvaluateMessage` does not have the bug
      because it keeps the caller's hash in its own `hexHash` field and checks that first.

      Fixed, and the same fix covers the non-transaction triggers (`NoScriptCache`, or a `CommandMap` with
      `SCRIPT` disabled), which reach the same branch. **The lesson is the one `ScriptLoadPairingTests`
      already recorded**: these two classes carry separate copies of one rule, so a test that exercises
      either one alone proves nothing about the other.

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

        **Split the two facts `knownScripts` currently conflates**, because they have different scopes and
        lifetimes:

        ```
        global, immutable      script body  ->  { sha, rendered SCRIPT LOAD frame }
        per-endpoint, soft     endpoint     ->  which shas it is believed to hold
        ```

        The rendering is endpoint-independent - the bytes of `SCRIPT LOAD <body>` and the SHA are the same
        everywhere - so today's per-endpoint table re-encodes and re-hashes the body once per endpoint. The
        frame half **never invalidates**, being a pure function of the body; only the belief is soft, and it
        already has `FlushScriptCache` and the `RunId` check.

        **The registry bounds itself via the invariant above.** Retaining a rendered frame pins a pooled
        buffer, so an application generating scripts per call would grow it without limit - but that is
        exactly the population that is already declaring itself transient with `NoScriptCache`. Do not admit
        those, and the flag becomes one signal meaning "transient" on both sides: the server puts it in the
        evictable pool, we do not retain its frame. `NoScriptCache` therefore gets no re-encoding saving,
        deliberately; a caller running such a script hot should stop using the flag.

        It is a **request** cache, not a response cache - superficially like `RespClientCache` but with
        opposite semantics (never invalidated, keyed by body rather than by frame). Keep it separate and
        small rather than generalising one to serve both.

        **Stage 1 and 1b are done** (`10f7b7c1`, and this change): the pair is composed and sent as a unit,
        and `RespScriptCache` renders each script once, keeping an exactly-sized array rather than the
        pooled rent it was made from - a rent held indefinitely is not merely wasteful, it is permanently
        removed from the pool. `NoScriptCache` now behaves as documented on this surface too: a bare `EVAL`
        with the body, and nothing retained.

        **Stage 1c is the write-time belief check** that skips the preamble when the endpoint is already
        believed to hold the script. It should consult `ServerEndPoint.knownScripts` rather than starting a
        second registry, and it belongs at write time for the reasons above - a resend after `NOSCRIPT`, a
        reconnect or a `MOVED` must re-decide, and that is what makes the retry terminate. Note this is also
        an *inspection*-time concern under the two-phase split logged below, so the two want designing
        together rather than in sequence. Correct, one round trip, no new
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

        **Probed 2026-09-15, against a real server (`RespHashImportProbeTests`). It is not a third seam.**
        `FramePairMessage` + `IRespPreambleGate` already covers it, with **no new mechanism**: `GetMessages`
        is called from `WriteMessageInsideLock` with the `PhysicalConnection` in hand, which is exactly the
        "once the connection is known, inside the write lock" the bridge injection has. So the difference
        between this and `EVALSHA` is *only the gate's scope* - connection versus endpoint - which the gate
        interface already anticipated in its own doc comment, having taken a `PhysicalConnection` from the
        start. Two `HIMPORT SET`s through the frame surface inject one `HIMPORT PREPARE` and both land.

        **What the probe did find is a second axis the interface does not name: *when* the belief is
        recorded.** `ScriptLoadGate` confirms in `OnEstablished`, on the reply, because the effect is the
        server's and only the reply proves it. A field-set cannot afford that: every import issued before
        the first `PREPARE`'s reply lands still reads "not prepared". Measured under a contended burst of 8:
        **confirm-on-reply injects 8 preambles, claim-on-write injects 1.** Both are correct - `PREPARE` is
        idempotent - so it is a cost difference that is invisible without counting, which is why the
        existing bridge code claims at write time (`TryAddPreparedFieldSet`) and is right to.

        The claim is safe precisely where it is made: `IsNeeded` runs inside the write lock, so
        test-and-set there is the only place where "has this connection prepared it?" and "write it" are
        one decision. A claim that is then not written dies with the connection, which starts empty.

        **Two smaller findings, both worth keeping.**
        - *An exception thrown from `OnEstablished` is swallowed.* The first draft of the probe "proved"
          the callback was never invoked by throwing from it - and passed. It is invoked; the throw was
          contained to the preamble message, which nobody awaits. Counting is the honest instrument, and
          the swallowing is worth a look on its own terms.
        - *A burst has to be made to contend - and cannot be made to contend reliably.* Issuing the 8 sends
          from one loop showed 1 preamble either way, because each reply landed while the next frame was
          still being rendered; a `Barrier` fixed that and gave 8-versus-1, repeatably, in isolation. It
          then failed under full-suite load, where the first reply again beat the other tasks to `IsNeeded`.
          So the *measurement* stands and is recorded here, but the test asserts only the deterministic half
          (claim-on-write injects exactly one) and logs the other. Asserting a race outcome makes a test
          depend on machine load rather than on the library, which is worth one correction to avoid.

        **Done 2026-09-15: the bridge hook is gone.** `HashImportSetMessage` is an `IMultiMessage` whose
        `GetMessages` claims the field-set on the connection and yields `[PREPARE, this]`, or returns `null`
        when it is already prepared; `HashImportDiscardMessage` is one too, using the same hook purely to
        learn which connection it landed on so it can drop the id. `PrepareFieldSetInsideWriteLock` and the
        `if (cmd is RedisCommand.HIMPORT)` line in the write loop are deleted. One mechanism, not two.

        **The migration was not free, and the reason is worth keeping.** Batches *support* `HashImport`
        today ("an ordered pipeline with no EXEC aggregate"), so `CanWriteWithoutExpansion => false` would
        have been a regression if a batch wrapped its inner operations the way a transaction does. It does
        not: `RedisBatch` keeps a plain `List<Message>` and writes each through `WriteMessageTakingWriteLock`,
        which expands. Only `RedisTransaction` wraps in `QueuedMessage`, and `GetHashImportMessage` already
        refuses transactions with a better message. Checking that *before* changing the flag is what kept
        this from being a silent break of a supported scenario.

        **The dedup had no test, and is invisible without one.** Every existing `HashImport` test passes
        whether the `PREPARE` goes once per connection or once per import, because `PREPARE` is idempotent -
        the same "invisible without counting" property the probe measured. The server counts it for us:
        `INFO commandstats` reports `himport|prepare` and `himport|set` separately. The assertion is
        relational rather than an equality, because commandstats is server-wide and the class runs once per
        protocol, so a concurrent sibling inflates both numbers together - but *injected per import the two
        counts are equal*, which is exactly the regression. Verified by mutation: dropping the
        `TryAddPreparedFieldSet` check reports "injected 5 time(s) for 5 SET(s)".

      With `EVALSHA` folded in, all three are the same mechanism at two seams - compose, or inject once the
      connection is known. If they work, the abstraction covers the space and `Fallback<T>()` can reach
      zero. If one does not, we learn which seam is short, rather than that "some commands are awkward".

      **Standing after two of the three probes:** `EVALSHA` and `HIMPORT` both land on the *same* seam -
      compose a pair, gate the preamble at write time - so the count is down to **two** mechanisms, not
      three, and the second one (`MULTI`, accumulate) is the one still blocked on the `Message` refactor.
      The abstraction has not needed a new concept yet; it has needed one more axis on an existing one.


- [x] **`Parse(ref RespReader)`, and the row-parser collapse** (§2.2, §6.16). **Done**, though not as
      described - see the correction at the end, which is the more useful half of this entry. Now with evidence rather
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

      **Correction, 2026-09-15, after reading the handlers rather than remembering them.** The premise
      *"with `Parse(ref RespReader)` the row parser **is** the scalar handler"* is **false**, and building on
      it would have changed behaviour silently. A top-level handler carries reply-level semantics that are
      wrong per element:

      - `IRespHandler<bool>` reads nil as **false** - a conditional `SET` that did not write, a `GETEX` on a
        missing key. The element projection is a bare `ReadBoolean()`.
      - `IRespHandler<long?>` **unwraps a unit aggregate** (`if (reader.IsAggregate) reader.MoveNext()`),
        because a one-operation `BITFIELD` still replies `*1`. Inside an aggregate that is nonsense.

      Of the element types with an aggregate form, only `long` and `RedisValue` have a scalar handler whose
      body matches the element projection. So "implement the element handler, get the aggregate free" is the
      wrong shape by exactly the amount those two differ, and the honest rule is the entry's own title:
      **one projection per element type, every aggregate form derived from it** - which does not involve the
      scalar handlers at all.

      **The half that was real is done.** The duplication was never scalar-vs-aggregate; it was
      array-vs-lease: seven element types (`long`, `bool`, `ExpireResult`, `PersistResult`, `double?`,
      `GeoPosition?`, `string?`) had the same projection written twice, up to 250 lines apart and sometimes
      in different files, and `long?` had it **three** times - the third inside `Lease<long?>`'s own walk.
      They all agreed, checked one by one, but nothing made them, and a pair that disagreed would be
      invisible: both calls succeed and return the length the caller expected. They now share
      `RespHandlers.Elements`, and `RespAggregateFormTests` reads one reply through both forms and compares
      element by element, so the next type that writes its own lambda gets caught (verified by mutation:
      flipping one array projection fails exactly that pair's test).

      **The pair types, and the second thing this entry got wrong.** `HashEntry`/`SortedSetEntry` needed no
      arity-2 walker, because **the walker already exists**: `ValuePairInterleavedProcessorBase<T>.ParseArray`
      decides jagged-versus-interleaved from the reply's *content*, once per reply, and runs one of two
      loops that both call the same per-row parse. What was actually missing was two lines of derivation -
      `ReadPairArray` and `ReadPairLease` - so a pair type now declares one processor and gets both
      aggregate forms, exactly as a scalar type declares one `Elements` projection and gets both.

      That is the whole item closed: **one shape per element type, every aggregate form derived from it**,
      for scalars and pairs alike. `RespAggregateFormTests` covers the pair types in *both* wire shapes,
      since which one arrives is decided by content rather than by the negotiated protocol and so both are
      reachable on either connection. Verified by mutation: passing `Resp2` instead of `Resp3` to one of the
      two forms fails exactly the two jagged cases.

      A refactor that **deletes** code. Deliberately sequenced after the signature change: the public
      shapes were binary-breaking and time-limited, the handler internals are internal and can be
      collapsed whenever without touching a caller.

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

- [ ] **The stream reads, and the shape question they all wait on.** The scalar half of the group has
      moved (`XLEN`, `XACK`, `XDEL`, `XDELEX`, `XGROUP CREATE/DESTROY/SETID/DELCONSUMER`, `XTRIM` both
      strategies); SER352 178 -> 154. What is left is blocked on one decision, not on effort.

      **`StreamEntry` holds `NameValueEntry[]`, and it appears in three different contexts** - which is the
      constraint, and is why the answer is not simply "make it a lease":
      - a **direct array result**: `XRANGE`, `XREAD` single-stream, `XREADGROUP` (3 overloads), `XCLAIM`;
      - **nested in a composite**: `RedisStream.Entries`, `StreamAutoClaimResult.ClaimedEntries`;
      - a **singular field**: `StreamInfo.FirstEntry` / `LastEntry`, one entry each.

      So any lease-shaped replacement has to work as a standalone value too, not only as an element of a
      pooled run - and `StreamEntry`'s *public constructor* takes the array, so the shape is in the shipped
      surface rather than just the property.

      **Leading candidate, and it is the wire shape rather than an analogy** (Marc, 2026-09-15): *a window
      over one shared buffer, each entry a sub-buffer - an aggregate frame and its children.* Checked
      against a real server rather than assumed. `XRANGE` of two entries:

      ```txt
      *2                                  the run
        *2                                one entry
          $15 1789506073259-0             id
          *4 $2 f1 $2 v1 $2 g1 $2 w1      interleaved name/value run
        *2 ...
      ```

      An aggregate whose children are aggregates, each `[scalar id, aggregate of interleaved scalars]`,
      every leaf a bulk string in one contiguous buffer. It generalises what exists rather than inventing:
      `RespValue` is already *a scalar reply as a window*, and the missing piece is *an aggregate reply as a
      window* that enumerates children as windows. The interleaved inner run is already handled -
      `ValuePairInterleavedProcessorBase` decides interleaved-versus-jagged from content.

      **And it dissolves the objection recorded above.** `StreamInfo.FirstEntry` being singular does *not*
      rule the window model out: `XINFO STREAM` carries the entry inline in the same shape
      (`first-entry` -> `*2 | $15 <id> | ...`), so a standalone entry is a window over the `XINFO` reply
      exactly as an element is a window over the `XRANGE` reply. The real constraint is narrower - the
      window must be able to **own or share** the lease - and `ReadOnlyLease<T>`'s secondary slot already
      does that; it is what `MGET` needed when values became windows.

      Alternatives, kept so they are not re-proposed: a `RespStreamEntry` over a
      `ReadOnlyLease<NameValueEntry>` (one lease per entry, so the allocation moves rather than goes); or
      keep `StreamEntry` and accept the per-entry allocation as debt - which matches `ListPopResult` today
      but hurts most here, since bulk reads are the normal usage rather than the exception.

      **Parked deliberately, 2026-09-15**, along with the other composites still holding arrays
      (`ListPopResult`, `SortedSetPopResult`, `LCSMatchResult`): left as-is for now, to be reconsidered as
      one decision rather than five, since they all turn on the same question.

      `StreamConfigure` is the same question pointing the other way - a composite on the **input** side
      (`StreamConfiguration`). `StreamAdd` and `StreamNegativeAcknowledge` wait on `NameValueEntry` and the
      nack mode respectively.

      `TransitionalCoverageTests` lists each of these by name rather than excluding `Stream*` wholesale, so
      adding a read without its shape decision fails the test instead of quietly widening the gap.

- [ ] **Composite results that still hold arrays. PARKED 2026-09-15 - decide all of them at once.**
      `ListPopResult`, `SortedSetPopResult`, `LCSMatchResult`, and on the stream side `StreamEntry`,
      `RedisStream`, `StreamAutoClaimResult`, `StreamPendingInfo`. Every one is a composite whose *fields*
      are arrays, so "no arrays on the new surface" is true of the signatures and not yet true underneath.

      They are parked together on purpose: the answer is one shape question, not seven, and answering it
      per-type is how a surface acquires five near-identical representations it cannot retire. The leading
      candidate is recorded on the stream-reads entry above - an aggregate reply as a **window** over the
      one reply buffer, children as sub-windows, which is a description of the bytes rather than a design
      (checked against a real server). `RespValue` already does this for the scalar case, and
      `ReadOnlyLease<T>`'s secondary slot already lets a window own or share the lease it points into.

      Nothing is blocked on this: the affected commands are either already shipped with arrays (old surface,
      staying) or deliberately not yet moved (stream reads). Pick it up when there is appetite for a shape
      decision rather than a transcription.

      ### The proposal, and what prototyping it actually showed

      **Where this comes from** (Marc, 2026-09-15): the Cap'n Proto position - *the fastest deserialize is
      the one you do not do; you just provide an on-demand view.* That is the thought behind the shape, and
      it is worth writing down because it also says where the analogy stops.

      What carries over: one contiguous buffer, views rather than copies, nothing materialised up front,
      and the reader pays only for the fields actually touched. All four are true here, and the capture
      measurement (free) is exactly that premise holding.

      What does **not** carry over: Cap'n Proto earns O(1) field access by designing the *wire format* for
      it - fixed-width slots, pointer offsets, an arena laid out for random access. RESP has none of that.
      It is a forward-only stream of variable-length frames with no offset table, so reaching element N
      means walking elements 0..N-1. So what this design can deliver is **lazy** parse - deferred, paid once
      per read - and not **zero** parse. For a single forward pass those are the same thing; for repeated or
      random access, lazy is strictly worse than materialising once, which is the real reason the indexer is
      a trap and enumeration is the honest API.

      **The time half, since that is what Cap'n Proto's claim is really about:** measured, on the nested
      shape, forward-only - roughly **2x** faster and **zero** allocation against 55KB (table below). The
      allocation half is the decisive one; the 2x is at the limit of the method used and is recorded as
      directional rather than asserted.

      **Shape** (Marc, 2026-09-15): a disposable root owning the leased contiguous buffer, and a
      `readonly struct RespAggregate<T>` holding a *slice* of it plus a projection - "think `RespValue` but
      with an enumerator", with custom leaf types decomposing by walking. The gnarly shapes become deferred
      walks rather than materialised arrays.

      **It is buildable on what exists, and that was checked rather than assumed.**
      - `RespValue` is already `(object? owner, int start, int length)`, already documents *"it holds the
        frame, not the payload"*, and already has exactly this ownership model: *"Uncounted, deliberately...
        the owner holds the single reference and these are views valid for as long as it is - one disposal
        on the owner, none on the values inside it."* The only thing making it scalar-only is one
        `reader.DemandScalar()` at capture.
      - The capture primitive needs no new reader machinery: `MoveNext()` + `SkipChildren()` bracketed by
        `BytesConsumed` yields exactly one sub-tree, and `SkipChildren` is already non-recursive. Verified
        against the real `XRANGE` shape: each window came back as `*2|$3|1-1|*2|$1|f|$1|v|`, independently
        readable as a complete frame.
      - Payloads are contiguous (`RespPayload.Span` slices one lease), so `(owner, start, length)` addresses
        any sub-tree, at any depth.

      **The measurement corrects the motivation.** Prototyped in the test project (it needs nothing
      non-public) over a 1000-element MGET reply - `RespAggregateProtoTests`:

      | arm | bytes |
      | --- | --- |
      | payload alone | 80 |
      | payload + capture | 80 |
      | deferred walk | 696 |
      | lease of windows, indexed | 736 |

      So **capturing is free**, and the deferred walk beats the lease by *40 bytes per 1000 elements* - not
      the several hundred that "MGET without even a lease or a write-to-array" implies. The reason is that
      the array is **pooled**, so it was already nearly free; the 40B is the lease object.

      > **Correction.** This entry first attributed the ~600B both arms share to "the walk". That was wrong:
      > a bare child walk over the same 1000-element reply allocates **0 bytes** (measured). The reader is
      > allocation-free, as it is meant to be; the ~600B was the prototype's own scaffolding - the
      > `RespPayload` wrapper and the `Projection<T>` delegate path - not the enumeration.

      **The nested case is where it pays, and by much more.** `RespAggregateTimingTests`, an `XRANGE` reply
      of 200 entries x 5 fields, read forward once with `foreach` - which is what callers do, and the only
      pattern where a deferred walk can win at all:

      | arm | time | allocation |
      | --- | --- | --- |
      | materialise into `StreamEntry[]`, then read | ~146-166us | **55,224 B** |
      | deferred walk | ~73-75us | **0 B** |

      The allocation result is unambiguous and not sensitive to how it was measured: **55KB against nothing**,
      because materialising a nested reply is N+1 arrays plus a `RedisValue` per field, and a walk is none of
      that. The time result - a shade over **2x** - is directionally stable across runs but sits right at the
      edge of what a stopwatch loop should be allowed to claim; asserting it would want BenchmarkDotNet. The
      test therefore asserts the allocation and logs the time.

      **So the case for this is not flat MGET** (40B), it is:
      - **nested shapes**, where the measurement above is the whole argument;
      - **one field out of a big reply**, where materialising everything to read one entry is waste.

      For flat, indexed, single-pass reads the lease is already the right answer and should stay.

      **Constraints found while exploring, all of which shape the API:**
      - **DECIDED: no indexer** (Marc, 2026-09-15) - *"it makes a promise we can't keep"*. RESP is
        forward-only with variable-length elements and no offset table, so `agg[i]` is O(i), and a
        `readonly struct` cannot memoise a cursor to soften it (a copy would lose it anyway). `for (i..)
        agg[i]` would be quietly O(n^2) on a type that looks like a list - and looking like a list is
        precisely the problem, because the shape of the API is what makes the promise. Enumeration is the
        offered access; a caller who needs indexing materialises, explicitly, and pays for it visibly.

      - **`Count` is fine** (Marc, 2026-09-15): *"streaming basically doesn't exist"*. `AggregateLength()`
        reads the count out of the header, O(1); only a **streamed** aggregate (`*?` ... `.`) has no count
        there and falls back to a walk, and no server in practice sends one. The fallback stays because it
        is correct, not because it is expected.

        Worth being clear why this is not the indexer decision inverted, since the two look similar. The
        indexer is O(n) on **every reply that exists**; `Count` is O(1) on every reply that exists and O(n)
        only on a shape nobody emits. One is a promise broken in the normal case, the other in a case that
        does not arise - so `Count` is offered plainly, without hedging in the signature.
      - **The root should not be generic.** Nesting proves it: one root, a `RespAggregate<StreamEntry>` over
        it, and inside each entry a `RespAggregate<NameValueEntry>` over the *same* root. Different `T`,
        same owner - so `RespRoot` (non-generic, `IDisposable`) plus `RespAggregate<T>`.
      - **The factory takes `(owner, start, length)`, not `ReadOnlyMemory<byte>`** - a memory cannot address
        a non-array owner without a `MemoryManager`, which is why `RespValue` uses the triple. And prefer a
        struct projection (`where TProj : struct, IRespProjection<T>`) over a delegate: constrained call, no
        indirect dispatch per element.
      - **Deferring the walk defers the errors.** A malformed reply currently throws at parse time, near the
        send; lazily it throws at access time, possibly after the root is disposed, where it becomes
        `ObjectDisposedException` instead. A deliberate change, not a detail.
      - **Holding one entry pins the whole reply buffer.** For a large `XRANGE` where the caller keeps one
        field that is a big retention, so the `As*` convention from `RespValue` - *"hands back something you
        own... safe to keep after the buffer has gone"* - has to carry across as the escape hatch.
      - **Pairwise children need a helper** (Marc): something that takes the span and yields the next
        element *pairwise*, handling both the linear/interleaved and jagged forms. This must not be a third
        implementation of that rule - `ValuePairInterleavedProcessorBase.ParseArray` already decides
        jagged-versus-interleaved from the reply's *content*, once per reply, and the walker should expose
        that as a pair enumerator rather than re-derive it.

- [ ] **`ARGREP`, the one array command left.** `ArrayGrepRequest` is a mutable builder whose `Predicate`
      subclasses render themselves through the **old** `MessageWriter`, so moving it means deciding how a
      caller-supplied builder writes into the new handler - the same question `StreamConfigure` asks from
      the input side. Everything else in the family moved 2026-09-15.

- [ ] **More command groups**, in `RespSurface.<Group>.cs` + `TransitionalDatabase.<Group>.cs` pairs.
      Mechanical now; `Strings` and `Bitmaps` are the worked examples. SER352 counts what is left (312).
      **Both halves, and the `[InlineData]`, or it does not count as done** - `Keys` and `Scripts` were
      written with only the surface half, which left ~66 members generated and, worse, unwatched:
      `EveryMemberOfAMovedGroupIsImplemented` is honest about the prefixes it is handed and silent about
      the ones it is not. `EveryImplementedMemberBelongsToATestedGroup` now closes that.

- [x] **The `OBJECT` family.** Done 2026-09-15, on the `Keys` group, and the coverage test's four
      exclusions are deleted so it is now held to the same standard as the rest. SER352: 186 -> 178.

      **Cacheability splits three-one, on a sharper rule than "it is a read".** The question is *can this
      answer change without the key being written?*, because a write to the key is the only thing
      invalidation reports. `REFCOUNT` moves when *other* keys share an integer; `FREQ` moves on every
      read - caching it would freeze the very number reading it is meant to observe; `IDLETIME` moves with
      the clock. All three `.NeverCached()`. `ENCODING` only changes when the value does, which *is* a
      write, so it is cacheable and serves as the control in the test, exactly as `PEXPIRETIME` does for
      `PTTL`.

      **Two unit traps, one of which bit.** `IDLETIME` is **seconds** where every other `TimeSpan` on this
      surface is milliseconds, so it names `RespHandlers.TimeSpanFromSeconds` rather than taking the default
      for `TimeSpan?` - wrong by a factor of a thousand and entirely plausible-looking otherwise. And the
      two handlers differ in more than the unit: `IDLETIME` answers a **nil** bulk string for a missing key,
      where `PTTL` answers `-2` and never nils, so reading it as an integer throws *"Invalid format parsing
      BulkString as Int64"* the first time anyone asks about a key that is not there. That was found by the
      end-to-end test comparing against `IDatabase` - which has answered this correctly for years and is the
      cheapest oracle available - rather than by reasoning about the reply shape.

- [ ] **`DBSIZE`.** Split out of the entry above: it is an `IServer` command and belongs to that context,
      not to `Keys`.

## Later / decide first

- [ ] **Locking waits for transactions.** Decided 2026-09-15: the group does not move yet, and not because
      of effort - `LockTake` is `SET key value NX PX` and `LockQuery` is `GET key`, both already
      expressible. `LockRelease` and `LockExtend` are the problem, and each has three branches:

      1. **Atomic:** `DELEX key IFEQ token` / `SET key token PX ms IFEQ token`. One frame, and already
         renderable - but `SetWithValueCheck`/`DeleteWithValueCheck` are **8.4 RC1**, so today this is the
         rare path rather than the common one.
      2. **A `WATCH`/`MULTI` transaction:** `Condition.StringEqual` plus `KeyDelete`/`KeyExpire`. What
         nearly every deployment actually gets, and the new surface cannot render it: a transaction is not
         a frame.
      3. **Neither** (no `MULTI`, e.g. twemproxy): degrades to `DEL`/`EXPIRE` with the token unenforced.

      So this is the HIMPORT shape - a command whose *fallback* needs connection-affine multi-command
      state - and it moves when `RespContext` can express a transaction, which is the same unlock HIMPORT
      and the `Condition` API are waiting on. `LockingTests` exists, so a half-move would fail the re-run
      rather than skip quietly.

      **A Lua fallback was considered and not taken.** `EVALSHA <get-compare-delete>` is self-contained,
      one round trip, and needs no watch state - the EVALSHA-shaped answer that lets a command move where
      HIMPORT could not. It was declined because it changes behaviour for deployments that have `MULTI`
      but not scripting, and locking is the wrong place to spend that: the commands are tiny and the
      transaction unlock is coming anyway.

- [ ] **Two-phase replies: inspect on the reader, parse at the consumer.** A future direction rather than
      a task - logged now because it changes what a "handler" is, and several things below will otherwise
      be built against the wrong shape.

      The plan: gut `Message` onto an `IValueTaskSource` with a poolable core, defer parsing to whoever
      awaits, and let the frame advertise only the task state (faulted versus completed). Scripts are the
      sharpest case: the reply would not be looked at until the `await`, and for fire-and-forget never.

      **That forces a privileged first look** - not a handler, an *inspection* that runs on the reader
      thread and returns a verdict: `complete` (default handling), `reissue`, and at least one more. It is
      not a new concept so much as an existing one made explicit: today's `SetResult` already does both
      jobs, with `NoteIfScriptUnavailable` and the error probe being the inspection half.

      Five things to pin before building it:

      - **Parse is deferrable; inspection is not.** Fire-and-forget proves it: a F+F `EVALSHA` answered
        with `NOSCRIPT` has no consumer to notice, so deferring the look would leave the endpoint's belief
        uncleared and every later call failing. It works today only because the processor runs on the reader
        thread whether or not anyone awaits.
      - **Ordering is contract, not detail.** Redirects first: a `NOSCRIPT` on a command that also needs
        `-MOVED` handling must redirect before anything else, because the script may exist on the right
        node. A fixed chain, not a bag of handlers.
      - **Every verdict must say who owns the payload afterwards.** `reissue` drops it; `complete` retains
        it until the consumer parses. Reference counting already supports both, but the enum has to make
        the obligation explicit or it will leak one way and double-free the other.
      - **A third verdict is needed**: "consumed for its effect on the connection, caller gets the
        default" - which is `HELLO`, `INFO`, `CONFIG`, `CLIENT TRACKING`. That is the convergence point for
        the side-effect commands, and the reason this belongs in the same design as the write-time
        injection seam rather than beside it.
      - **Pooling plus IVTS is where the sharp edges are**, not the parsing: a reused message must carry no
        residue past completion, and "inspect said reissue" followed by a late duplicate reply is the
        classic double-complete. Version tokens matter more here than anywhere else.

      **One interaction with the cache:** storing currently happens on the reply path. If parse defers, the
      store belongs in *inspect* - otherwise a reply nobody awaits promptly, or at all, never populates the
      cache, and a fire-and-forget read silently stops warming it.

      **The split is already ~94% observed, which makes this bounded.** `SetResult` versus `SetResultCore`
      is the existing proxy for the two jobs, and counting them:

      | | count | becomes |
      | --- | --- | --- |
      | override `SetResultCore` only | **89** | pure parse - deferred wholesale, untouched |
      | override `SetResult` | **6** | the inspection population |

      And the six sort into the verdicts rather than resisting them:

      - `ScriptResultProcessor` - `NoteIfScriptUnavailable`. The only true **reissue** in the library.
      - `AutoConfigureProcessor`, `TracerProcessor` - read the reply for its effect on the *connection*
        (server version and features; `SetLatency`). The third verdict is therefore not speculative: it has
        two occupants before `HELLO`/`CONFIG`/`CLIENT TRACKING` arrive.
      - `TransactionProcessor`, `HashImportProcessor` - composites and injected preambles, which is the
        seam the three probes above are already about.
      - `RespResultProcessor` - a **false positive, and the best argument for the change**. It overrides
        the outer method only to capture the raw frame *before* `MovePastBof()` advances the reader: it
        exists because parsing is eager. Under deferred parse, "capture the undecoded bytes and decide
        later" is the default, so most of it stops having a reason to exist.

      Net: 89 move unchanged, 3 need a verdict, 2 are composite work already planned, 1 gets deleted.


- [ ] **Per-context `CachePolicy` override** (`WithCachePolicy`). The other half of the options/policy
      split: policy settings are read-time, so they can vary per call, and the override rides in the
      context's service slot exactly as `MaxCacheAgeService` does. `WithMaxCacheAge` stays as the
      ergonomic spelling of the common case rather than being subsumed.
      One wrinkle: `InvalidationGracePeriod` is not purely read-time. Whether grace is on gates a
      timestamp *write* in the invalidation path, which sees every key the server mentions. So arming it
      belongs on `CacheOptions` and only the duration can vary per context.

- [ ] **`ISubscriber`'s context** still throws from `IRespTarget.Context`. `IServer`'s is wired (below);
      this one wants its own executor question answered first - a subscriber connection is a different
      bridge, not just a different endpoint.

- [x] **`IRespTarget` on `IDatabaseAsync`**, so `IBatch`/`ITransaction` offer the keyspace groups by name.
      **Done 2026-09-15.** `tran.Strings.SetAsync(...)` and `batch.Strings.GetAsync(...)` bind directly; the
      cast the tests used is gone.
      Unblocked by the target split, and the probe says the hard part is already done: a batch's and a
      transaction's context both queue rather than send, because their executor's target is the batch and
      `ExecuteAsync` is overridden to queue. Pinned in `RespEndToEndTests`.

      ~~**One blocker, and it is specific:**~~ **Resolved 2026-09-15 - the blocker was already fixed and the
      entry had not caught up.** `Scripts.Evaluate` composes a pair, and a `SCRIPT LOAD` injected inside
      `MULTI` would put its reply into the `EXEC` array and shift every result position. The second of the
      two options here is what shipped, and by structure rather than by a sixth hand-written guard:
      `QueuedMessage` default-refuses anything whose `CanWriteWithoutExpansion` is false, and
      `FramePairMessage` says exactly that - so the pair throws `NotSupportedException` naming the
      positional array. Pinned by `MultiMessageInTransactionTests.TheFrameSurfacesComposedPairIsRefused`,
      which also reaches the scenario today via `((IRespTarget)tran).Context`, so it is not
      unreachable-by-accident either.

      **The refusal is worth more than it looks.** Flipping `CanWriteWithoutExpansion` to `true` does not
      make that test fail - it makes it **hang indefinitely**. The pair's result box is on the
      `FramePairMessage`, and only messages yielded from `GetMessages` are enqueued for a reply, so with the
      expansion dropped it is written but never enqueued and the caller's task never completes. A
      permanently pending task is worse than a wrong answer.

      So this item is **unblocked**; what remains is the public-surface decision below.

      **The compatibility question was raised and answered: go ahead.** Adding `IRespKeyspaceTarget` to
      `IDatabaseAsync` makes `Context` a required member for anyone implementing `IDatabaseAsync`, `IBatch`
      or `ITransaction`, mocks and wrappers included. The answer, and the reasoning worth keeping: extending
      this interface family has historically been *the only* way to add functionality here, so it is a
      known and accepted problem - **and it is the problem this work exists to fix.** Every addition after
      this one is an extension member on the context, so this is meant to be the last time.

      **Three implementers needed the member, and one of them must not forward.**
      - `KeyPrefixed<TInner>` clones with the prefix - the one-line write half of key-prefixing, moved from
        `KeyPrefixedDatabase` onto the shared base, so a prefixed **batch** and **transaction** get a
        context too rather than only a prefixed database. That is new behaviour, and pinned.
      - `RetryDatabase` and `RetryTransaction` **throw**. Forwarding the inner context would compile, read
        naturally and be wrong: commands composed from it go through the *inner* executor, so the group
        surface would drop the retry - invisibly, since the command still succeeds whenever nothing fails.
        Retry arrives on this surface as a retry *executor*, which is the open item below.

      **Worth knowing for next time: `PublicAPI.Unshipped.txt` did not change.** `Context` is inherited
      rather than redeclared, and the analyzer tracks members rather than base-interface lists - so a
      required-member break on a shipped interface is invisible to the API tracker. A test catches it; the
      file does not.

- [ ] **The retry executor** (`WithRetry`). Prerequisites in place; no design written.

- [ ] **Should the existing `ResultProcessor` path copy too?** (§6.16). Sharing there is *correct* — it is
      single-owner — so this is a policy change, not a fix, and it would strand `TryReservePayload`,
      `IPayloadReservationProvider` and `PayloadReservation` as dead code now that `ReadLease` (read-only)
      is their only remaining consumer. Own merits or not at all.

- [ ] **Module-read tracking.** Do module reads register for invalidation? A five-minute experiment
      against a real server, never run. Relevant because the docs put the whole `FT.*` family outside
      server-side tracking — though a prefix list that names data keys already excludes index names, so
      the exposure now requires someone to have declared a prefix covering them.

- [ ] **`RespContext` sizing.** Currently **40** bytes, down from 48 when cancellation left the context. `CachePolicy` rides on the cache and the freshness
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
- [x] `CLIENT TRACKING` negotiated by the handshake, and taught to the in-proc server — `75933c61`.
      Closes both items at once, because they only prove anything together: the real-server tests used to
      send the command themselves, so they exercised the server and said nothing about the client. Three
      findings worth keeping:
      **(a)** the RESP3 check must test the negotiated *intent*, not `connection.Protocol` — `HELLO` is
      written no-flush/fire-and-forget, so its reply has not landed and the protocol is still unknown at
      that point in the handshake. Gating on it threw on every connection and the command was never sent.
      **(b)** "write, read, assert it cached" is a race: our own write is announced back to us (no
      `NOLOOP`) and can land mid-read, where refusing the fill is correct. Tests read until one sticks.
      **(c)** asserting what the *client* cached cannot see a wrong `PREFIX` on the wire — dropping it only
      means being told about more keys than you asked for. The in-proc server exposes what it negotiated
      so the test can check the wire, which is the only thing that kills that mutant.
- [x] `RespClientCache` is internal; the public opt-out is `RespContext.WithoutCache()`.
      Closes the hole above by removing the ability to mint one: the type was public by default rather
      than by design, and the numbers said so - one non-test caller (`RedisDatabase` passing
      `multiplexer.ClientCache`, itself internal), 99 test constructions, and **no way for a caller to
      obtain an instance at all**. So 43 public API entries served nobody, while the one thing they did
      enable - `new RespClientCache()` attached by hand - was the unsound case.

      The argument for keeping it public was the diagnostic counters (`Stored`, `RefusedRaced`,
      `RefusedNotTracked` ...), which exist so "why is this stale?" and "why is nothing being cached?" are
      answerable without a debugger. That argument does not survive contact: with the muxer's property
      internal, nobody outside could read them anyway. Going internal loses nothing that existed, and
      internal -> public later is additive where the reverse is a break - so it is the reversible direction
      to sit in while the spike is a spike. **Re-exposing the counters needs a designed home**, and that is
      the open question this leaves behind, not the visibility.

- [x] Command groups bind to `IRespKeyspaceTarget`, not `IRespTarget` — `3abb43e2`.
      `IRedis` carried `IRespTarget`, so `IDatabase`, `IServer` and `ISubscriber` all offered `Strings`,
      `Hashes`, `Keys` and `Scripts`: `server.Strings.Get(key)` compiled. Discoverability aimed straight at
      a cliff. `IRespTarget` moves down to the three interfaces individually, `IRespKeyspaceTarget` and
      `IRespServerTarget` derive from it, and the eight groups bind to the former.

      **The routing is not what needed splitting**, which was the surprise: a batch's context queues and a
      server's pins to one endpoint already, because the executor's target is the batch/server and
      `ExecuteAsync` is overridden on both. The split is about which commands are *offered*. Free to do now
      only because `IRespTarget` is unshipped (SER010); after it ships, moving it is a break.
- [x] `IServer`'s context is wired — `4bfc05f4`. Handover for the first server group: bind it to
      `IRespServerTarget`, and take the database number explicitly the way `IServer`'s own members do -
      the context carries `-1`, so a database-scoped command fails at construction rather than silently
      running against database 0. No cache is attached, deliberately: invalidation is reported by key and
      server commands are keyless, so nothing could ever invalidate a cached `INFO`.

      **Found while wiring it, and worth more than the wiring:** the ad-hoc `ExecuteAsync(string, ...)`
      handler never assigned `_command`, so every ad-hoc frame carried `RedisCommand.NONE` - the enum's
      zero - rather than the parsed command or an honest `UNKNOWN`. The pipeline reads `Command` to decide
      `IsPrimaryOnly`, so **an ad-hoc write could be routed to a replica**; it also fails
      `RequiresDatabase`, which is how a server context surfaced it. Fixed by resolving the identity
      alongside the bytes. Invisible until now because a database context has `db >= 0`, where the check
      does not fire.
- [x] **The inspect/parse split** — `ea26ce61`, `a86e563b`, `7d521699`, `d338591e`. `SetResult` always did
      two jobs; `Inspect` is now the first, and it can direct as well as record - `Complete` or `Reissue`,
      with `NotYet` reserved for `WATCH`. A `Reissue` re-writes the message and returns `false` from
      `SetResult` ("re-issued, do not complete"), which is not new machinery: it is what `MOVED` has always
      done from this same read path.

      **The retry stopped being opt-in.** It was eight hand-written `catch (RedisServerException) when
      (msg.IsScriptUnavailable)` sites, so a path that did not know to catch got nothing - which is exactly
      why the frame path surfaced a raw `NOSCRIPT` and left the stale belief in place, failing identically
      for ever. All eight are gone; every path retries because the pipeline does.

      Three things worth keeping:
      **(a)** not `ServerSelectionStrategy.TryResend`, despite that being `MOVED`'s vehicle - it is about
      *redirects*, refuses a message with no hash slot (a keyless script has none), and sets
      asking/no-redirect on the way through.
      **(b)** the buffer must outlive a reissue - a message about to be written again still needs its
      rendered arguments - and that release is keyed on the *verdict*, not on "was this a NOSCRIPT", because
      a second NOSCRIPT is not retried and does need to release.
      **(c)** the retry-once guard reads the sticky flag *before* noting. Only load-bearing when the caller
      supplied a **hash**: given a body the retry sends `EVAL` with it, so there is never a second
      `NOSCRIPT`. My first mutation of that guard survived for exactly that reason - the uncovered case was
      the hash one, and with it covered the unguarded version loops for ever.
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

- **Errors as values on the new surface.** Decided 2026-09-15: a top-level error throws, as everywhere else
  in this library, unless somebody turns up with a concrete need. Errors-as-values makes every caller
  responsible for remembering to check, and forgetting is **silent** - the same failure class this design
  refuses for keyless cache entries, undeclared retry categories and stale script beliefs. A second error
  model, opt-in for correctness, would be inconsistent in the expensive direction.

  The line is already drawn where it belongs: nested errors inside an `EXEC` array *are* data in
  `RedisResult`; only the top level throws. That is the difference between "this operation failed" and "one
  element of this aggregate failed", not an oversight.

  **This is `redis.call` vs `redis.pcall`**, and that is the precedent rather than an analogy: `call`
  aborts and propagates, `pcall` hands the error back as a value with an `err` field - and Redis made
  `call` the default and `pcall` the thing you deliberately ask for. Same answer, arrived at by the people
  who had to live with both.

  **A deferral, not a door closing**, and the analogy gives it its shape: `RespResult` already stores the
  `Prefix` and the raw frame *including* the prefix bytes, so `-ERR` is representable today - the processor
  simply routes errors to the failing path. So if the need arrives it is an additive, **per-call** opt-in,
  chosen at the call site the way you choose `pcall` - never a global mode and never a changed return type
  on `ExecuteResp`. That is the only version where "remember to check" is not a silent hazard: the person
  who gets a value back is the person who asked for one.


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

- **Deriving `BCAST PREFIX` from `AppendKeyPrefix`.** Prefixes are connection-global, must not overlap —
  context prefixes routinely nest — and cannot be removed individually. §6.13.
