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

- [ ] **`CancellationToken` moved off the context onto the call - HALF DONE, found 2026-09-16.**
      Marc: *"RangeAsync doesn't carry cancellationtoken - did we lose that somewhere? other victims?"*
      Checked: **zero** `CancellationToken` references across `RespSurface.*.cs` and `Groups/*.cs`, and
      zero on `RespContext`. So it is not a regression - the token came *off* the context as recorded, and
      went *onto* `Send`/`SendAsync`, but never reached a single one of the ~256 public group methods.
      Every one of them, uniformly, not a few stragglers.

      **The entry below argues from a signature that does not exist** - `db.Strings.GetAsync(key, flags,
      token)` - which is how a half-finished change came to be ticked off. Left unticked now.

      **The deadline is not "now", it is before SER010 comes off.** Adding an optional parameter to an
      existing method is a binary break (AGENTS.md), so after the experimental attributes are removed
      every one of those methods needs a permanent overload instead. While experimental it is free. That
      puts it in the same set as the `.Interpolated` namespace: cheap now, impossible later, and worth
      deciding together rather than one at a time.

      **CORRECTION - my "what can cancellation even mean here" objection was wrong.** I argued that a RESP
      request cannot be recalled, so the only honest cancellation is *stop waiting*, which would desync
      unless the pipeline tracked the abandoned reply. Marc: *"the cancellation isn't about recall; it is
      about (1) intercepting unsent things - retries, moves, etc, and (2) allowing the caller to get about
      their day, whatever happens to the task."*

      Both are real, and neither desyncs:

      1. **Unsent work is genuinely cancellable** - the backlog, a retry, a re-dispatch after MOVED/ASK.
         Nothing has gone to the server, so cancelling is cancelling, not a euphemism.
      2. **Abandoning the await costs the connection nothing.** The pipeline already matches replies to
         requests - it has to - so the reply still arrives, is still parsed, and the result is simply
         dropped with nobody waiting. My desync claim confused "nobody is awaiting this" with "nobody is
         reading the socket".

      **And it is already built:** Marc has cancellation working fully in the unmerged v3 spike. So this is
      not an open design question with an uncertain answer - it is a known implementation waiting to be
      merged, which is why putting the parameter on the signatures now is the right call rather than a
      promise we might not keep.

      **Done for Streams as the template, 2026-09-16:** all 14 group methods take
      `CancellationToken cancellationToken = default` as the last parameter and thread it to the send.
      Until the pipeline lands, an already-cancelled token is honoured and a merely-cancellable one throws
      `NotImplementedException` naming the reason - the resolution this queue already reached for
      `Send`/`SendAsync`, now reaching the surface it was always meant to reach. The other 8 groups follow.

- [x] **(the rejected idea, kept)** Recorded here
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

      **Status 2026-09-16: `XRANGE`/`XREVRANGE` have moved**, on the deferred shape - `RespReply` +
      `Streams.RespRangeReply` + `RespStreamEntry` + `RespNameValueEntry`, with `ToArray()` serving
      `IDatabase.StreamRange` off the same parse. SER352 106. The rest of the stream reads (`XREAD`,
      `XREADGROUP`, `XCLAIM`, `XAUTOCLAIM`, `XPENDING`, `XINFO`) now have a pattern to follow rather than
      a shape to decide.

      ### One parse, two readers - DECIDED 2026-09-16

      **Both shapes start from a `RespReader` over the leased buffer, so neither needs new parse code.**
      `RespPayload.GetReader()` is already `new RespReader(Span)`, and the eager parse already exists and
      already takes `ref RespReader`. So:

      ```csharp
      // the shim handler: IRespHandler<T> is handed a ref RespReader by the pipeline
      StreamEntry[] IRespHandler<StreamEntry[]>.Parse(ref RespReader reader)
          => ParseRedisStreamEntries(ref reader, RedisProtocol.Resp3);

      // ToArray(): a reader over the same buffer, the same call
      public StreamEntry[] ToArray()
      {
          var reader = Live.GetReader();
          reader.MoveNext();
          return ParseRedisStreamEntries(ref reader, RedisProtocol.Resp3);
      }
      ```

      **The rule, stated generally: do not implement eager in terms of lazy.** Projecting the old shape by
      walking the typed view would capture a window per entry, build a sub-reader per level and re-read
      frame headers at each step - paying for laziness that is discarded immediately. One forward pass is
      what the eager shape wants, and it is what the existing parse already does.

      So the shim and `To*()` are **not two implementations to keep in sync** - they are two *call sites* of
      one function, differing only in where the reader comes from. The array shape never acquires a second
      parse, and the two are byte-identical by construction rather than by review.

      **Prerequisite, done 2026-09-16:** the three stream parses were `protected [internal] static` on the
      generic `StreamProcessorBase<T>` and none of them used `T`, so reaching one read as
      `StreamProcessorBase<StreamEntry[]>.ParseRedisStreamEntries(...)` - naming a type purely to get at a
      static. They now sit on the non-generic `ResultProcessor`, beside `TryParseArrayInfo`. The base class
      was empty afterwards and is gone; its four processors derive from `ResultProcessor<T>` directly.

      ### The root the caller holds - DECIDED 2026-09-16

      **One disposable class at the root, uncounted struct views inside it.** That is not a new rule; it is
      today's `ReadOnlyLease<RespValue>` exactly - class root owning the buffer, struct windows pointing
      into it, disposal once at the top. The deferred walk extends it from flat to nested.

      **The root is concrete per reply shape, not generic**, and nested with the entities:

      ```csharp
      public static class Streams
      {
          public sealed class RespRangeReply : IDisposable
          {
              private RespPayload? _payload;
              public RespAggregate<RespStreamEntry> Entries => new(Live, ...);
              public void Dispose() => Interlocked.Exchange(ref _payload, null)?.Release();
              private RespPayload Live => _payload ?? throw new ObjectDisposedException(...);
          }

          public readonly struct RespStreamEntry           // uncounted window, copied freely
          {
              public RespValue Id { get; }
              public RespAggregate<RespNameValue> Fields { get; }   // same payload, no disposal of its own
          }
      }
      ```

      giving the call site Marc asked for, with no `.Value` in the way:

      ```csharp
      using var result = await ctx.Streams.RangeAsync(key, ...);
      foreach (var entry in result.Entries)
          foreach (var field in entry.Fields) { ... }
      ```

      **Why a class and not a struct**, since combining the lease and the view looked wrong at first: the
      shipped `ReadOnlyLease<T>` is a **sealed class** whose `Dispose` is
      `Interlocked.Exchange(ref _buffer, null)`, so double-disposal is safe by construction. As a struct the
      root would be copyable, which is precisely the hazard `RespValue` rules out - *"a struct copy cannot
      increment a reference count, so counting per value would be a leak or a double-free waiting to
      happen"*. So `in result.Entries` cannot work, but not because lease and view cannot combine: because
      `in` wants a struct and the root has to be a class.

      **`<T>` lives only where it is a real container** (`RespAggregate<T>`). The payload stays untyped
      because it is the **cache entry**, shared between callers who parse it as different things; the root
      is typed by being concrete. Allocation is parity with today - one object per call, which is what
      `ReadOnlyLease<T>` already costs - and it is what removes the N+1 arrays, so it is a straight win.

      Cost: one reply type per shape, roughly **8** across the 19 methods, since `StreamEntry[]` x6 and
      `RedisStream[]` x6 collapse to two.

      ### A shared root: `RespReply` - DECIDED 2026-09-16

      **One abstract base for every deferred-view root**, holding the payload, guarding it, and giving it
      back exactly once. Concrete replies add only the typed windows.

      The base saves about ten lines per reply, which on its own would not justify a fifth `Resp*` name.
      What justifies it is that **every root in the family has the same hazard**: a window that outlives
      the lease. With a base that is one lifetime contract, one disposal implementation, and one place for
      the `EveryAccessorDiesWithTheReply` sweep - so a new reply shape inherits the sweep instead of
      someone remembering to write it.

      **External derivation is supported** (Marc, 2026-09-16): a client building on this one - NRedisStack
      and friends - defines its own reply shapes. So the constructor is `protected`, not `private
      protected`.

      **What that costs, and the rule that pays for it.** Once outside code can derive, the shape is
      frozen: an `abstract` member added later breaks every derived type. So none will be added - *"we
      won't - we'd add a virtual that throws with a 'supported' twin, or a `Try*`"* (Marc). That is the
      `Stream.CanSeek`/`Seek` model, precedented for twenty-five years, and it trades compile-time
      totality for the ability to evolve at all, which is the right way round for a base other people
      derive from. One `protected virtual OnDisposed()` is defined **now** rather than added later;
      `Dispose()` itself is non-virtual and `Interlocked`-exchanged, so double disposal cannot
      double-release a pooled buffer.

      **Rejected: deriving from `RespPayload` directly.** Tempting - no new type, no extra field, and a
      root genuinely *is* those bytes retained. But `RespPayload`'s public surface is the *cache* protocol
      (`TryRetain`/`Release`/`Span`/`Create`), and a `RespRangeReply` offering callers `.Release()`
      alongside `.Dispose()` is a refcount footgun for no gain. Composition also leaves room for a root
      over something that is not a payload.

      **The retain contract, which is the part that matters.** The pipeline retains; the constructor
      receives a reference it *owns*; the base's `Dispose` releases it. External code never calls
      `TryRetain`/`Release` - which matters because `TryRetain` can **fail** (eviction won the race), and
      a constructor is the worst possible place to discover that. Failure stays in the cache lookup,
      before any reply object exists. `RespReplyHandler<TReply>` is where that happens, and losing the
      race falls back to a copy rather than resurrecting a count from zero.

      **`RespReplyHandler<TReply>` is the one public door onto retention.** `IRespPayloadHandler` stays
      internal because retaining is safe only for a result that cannot write through the buffer; the
      `where TReply : RespReply` constraint *is* that judgement expressed in the type system, so the door
      can be opened without handing the privilege out generally.

      **Rejected: `T : new()` + an `Init` handover.** Raised as the efficient shape (Marc), and it is not:
      `new T()` under a constraint does not compile to a `newobj`, it goes through
      `Activator.CreateInstance<T>` - no faster than invoking a cached delegate. And it costs two-phase
      construction: an object that exists before it has a buffer, so every accessor needs an
      initialised-guard for a state the type otherwise never has, and "walk once in the constructor"
      becomes "walk in `Init`". The `CompareExchange` was right, but it guards a hazard the constraint
      itself introduced.

      **Still open:** a root over a *slice* of a shared buffer (several replies over one pipelined batch).
      `RespPayload` already carries offset/length internally, but externally only the copying `Create` is
      reachable. Left internal until something asks.

      ### Nested captures were pointing at the wrong bytes - FIXED 2026-09-16

      **Found while building `RespStreamEntry`, and it would have been a silent data bug.** A window
      records an offset into the buffer its *owner* holds, and is bracketed by sampling
      `RespReader.BytesConsumed`. But walking an aggregate means reading a **slice** of that buffer, so
      offsets restarted at zero and anything captured during a walk recorded the distance from the
      *aggregate* while claiming to be a distance from the *payload*. Well-formed, resolvable, wrong bytes
      - no exception.

      It never showed up in the prototype because that projection read eagerly (`ReadString`,
      `AggregateLength`) and captured nothing, and it does not bite at the top level, where the enclosing
      start is zero.

      **The fix needed nothing new conceptually:** `RespReader._positionBase` already exists to keep
      offsets absolute as a reader moves between the segments of a sequence, so a slice-aware constructor
      seeds it. Everything else is relative arithmetic and is unaffected.

      Pinned by `RespAggregateTests.AWindowCapturedInsideANestedWalkPointsAtTheOwnersBytes`, and the guard
      was **verified load-bearing by mutation**: seeding `positionBase: 0` compiles and fails exactly that
      test and no other.

      ### Projections are handed a reader positioned *before* the element - DECIDED 2026-09-16

      The original `RespAggregate<T>` projection got a reader positioned **on** its child. That reads
      fine, but it structurally cannot **capture**: an element's start cannot be recovered once it has
      been read past, so `RespAggregate<RespValue>` - the MGET shape - was not expressible, and neither
      was a stream entry's `Id`.

      Both enumerators now use `MoveNextRaw`, so a projection receives the reader positioned *before* its
      element - **the same position `TryCaptureNext` expects**, which is what lets the two compose.
      Attributes are still skipped, by `TryMoveNext`, wherever the projection makes its move. Cost: a
      read-only projection opens with `MoveNext()`. Worth it, and it removes an inconsistency rather than
      adding one.

      ### Pairs are a different walk: `RespPairAggregate<T>` - DECIDED 2026-09-16

      A pair is two **siblings**, and the enumerator hands a projection a reader trimmed to one sub-tree -
      so a projection over `RespAggregate<T>` structurally cannot reach the second half. Adding a stride
      would not be enough either, because of jaggedness. Hence a separate type, a `PairProjection`
      delegate taking two readers (deliberately the same shape as the eager pair parser), and a walk that
      handles both wire shapes.

      **The jagged/interleaved decision is shared, not copied a third time.** `IsAllJaggedPairsReader` was
      a private static on the generic `ValuePairInterleavedProcessorBase<T>` that never used `T` - the
      same smell already fixed for the stream parses. It is now `RespReader.IsAllJaggedPairs()`, and both
      the eager processor and the deferred window call it. Only the *policy* - whether jagged is permitted
      - stays with the caller, because content is what the bytes are and policy is what the command
      allows.

      **The shape is decided once, at capture, and stored.** Detection is O(n) in the children, so
      re-deciding per pair would make a walk quadratic. It is the one piece of parse state these windows
      carry that cannot be re-read cheaply.

      **`Count` counts pairs here**, which is not in tension with `RespAggregate<T>.Count` counting
      children: there, halving a map would have made RESP2 and RESP3 disagree about the same reply; here
      both wire shapes agree on the number of pairs, which is why they can share one type at all.

      **The deferred stream path always permits jagged**, where the eager path gates on protocol version.
      Safe because a stream entry's fields are scalars and can never look jagged - pinned by
      `RespRangeReplyTests.ScalarFieldsAreNotJagged` - and because the existing comment already concedes
      jaggedness is not really a RESP3 thing.

      ### Policy, not protocol, through the stream parses - FIXED 2026-09-16

      Marc, reading `ToArray()`: *"why Resp3?"* - and the honest answer was that it was the only way to
      spell "permit jagged field pairs" in a signature that takes a `RedisProtocol`.

      **The protocol was never doing anything else.** Threaded through `ParseRedisStreamEntries` ->
      `ParseRedisStreamEntry` -> `ParseStreamEntryValues`, it reaches exactly one reader:
      `ValuePairInterleavedProcessorBase.AllowJaggedPairs(protocol)`. Nothing in those parses looks at it
      otherwise. So a reply object - which holds a *buffer*, not a connection, and has no protocol
      anywhere reachable from `RespContext`, `RespPayload` or `IRespExecutor` - had to name a protocol
      version it was not really claiming, with the reasoning living only in a test.

      Now the three parses (and `ParseArray`) take `bool allowJaggedFields`, with protocol-taking wrappers
      that convert **once**, via `AllowJaggedStreamFields` -> the processor's own virtual, so the policy
      keeps a single home instead of being restated at eight call sites.

      **And the mutation run found the policy had no test at all.** Making `AllowJaggedStreamFields`
      return `false` unconditionally broke nothing: no stream test had ever fed a jagged field list. That
      is now pinned - and pinning it corrected a wrong assumption of mine in the process. Refusing jagged
      does **not** read `[[f,v],[g,w]]` as one oddly-shaped field; it asks the reader for a scalar, gets
      an array, and throws. Which settles the deferred path's unconditional `true` more firmly than the
      original argument did: permitting jagged cannot turn a working parse into a differently-valued one,
      because the alternative was never a different value - it was an exception.

      ### Capture or read? A window is bigger than a number - DECIDED 2026-09-16

      Marc, on `RespStreamEntry`: *"it looks like we parse out the 'free' bits - longs, timespans etc, and
      defer on the aggregate; I think that's fine. I assume for string-like types we'd defer to
      RespValue."* Yes, and the rule is arithmetic rather than taste:

      **Capture a window when the alternative is copying bytes or allocating. Read outright when the value
      is fixed-size and smaller than the window that would describe it.** Measured:
      `RespValue` **24** bytes, `RespAggregate<T>` **32**, `RespPairAggregate<T>` **32** - against
      `long` 8, `int` 4, `TimeSpan?` 16. So deferring a fixed-size number *grows* the struct to avoid
      parsing four bytes; deferring a string-like value replaces a copy and an allocation. Pinned by
      `RespRangeReplyTests.AWindowCostsMoreThanTheNumbersItWouldDescribe`.

      **Store the window vs re-derive it on access:** both work now that a projection is handed a
      positioned-before reader. Storing costs struct size (`RespStreamEntry` is **80** bytes, against
      `StreamEntry`'s 48) and pays the capture once; re-deriving keeps the struct at one window (~24) and
      pays a header parse per access. **Measured, neither is material at these sizes** - walking ids only
      and walking everything came out the same within noise on 1000 entries x 10 fields - so this is a
      clarity call, not a performance one. Store when the member is usually wanted; re-derive only if a
      shape acquires enough optional members to bloat the struct.

      **A worry that measured as nothing.** Capturing `Fields` runs `IsAllJaggedPairs`, which is O(children)
      - so eager capture looked like it made the walk O(total fields) whether or not fields were read.
      It does not: the test short-circuits on the first child that is not a 2-element aggregate, and a
      real field list starts with a bulk string. Forcing `allowJagged: false` measured 1,274us against
      1,289us - noise. The scan is only O(n) when the answer is "jagged", which is when you wanted it.

      For scale while the numbers are here: 1000 entries x 10 fields, deferred full walk **~1.2ms**
      against `ToArray()` **~2.1ms**, and the deferred walk allocates nothing beyond the reply.

      ### `RespNameValueEntry` is not a stream type - MOVED 2026-09-16

      Marc: *"probably common enough to move out of Streams."* Agreed, and it is now top-level
      `StackExchange.Redis.RespNameValueEntry`.

      A name/value run is what `HGETALL`, `CONFIG GET`, `XINFO`, a stream entry's fields and several
      `CLIENT` replies all are. **The nesting convention exists to keep group-*specific* entities out of a
      shared namespace**, and something every group can use is not one of those - so nesting it under
      `Streams` would have meant hashes reaching for `Streams.RespNameValueEntry`, which reads as a
      mistake. It also matches where the materialised counterpart already lives: `NameValueEntry` is
      top-level too.

      The rule that falls out, for the shapes still to move: **nest what a group owns, hoist what the wire
      shares.** A `ToHashEntry()` twin belongs on this type when the hash commands move - same pair on the
      wire, only the materialised type differs.

      ### The per-group file layout, piloted on Streams - DONE 2026-09-16

      Marc: *"I would like to end up with Streams in 3 (or more) partials... I think we should try to get
      one looking 'just so', as a baseline."* Done, and it is the template:

      ```
      src/StackExchange.Redis/Groups/
          Streams.cs           // the group type, the .Streams accessor, the empty anchor partial
          Streams.Methods.cs   // the commands
          Streams.Types.cs     // RespRangeReply, RespStreamEntry
      ```

      Namespace `StackExchange.Redis` - **out of the staging `Interpolated` area**, which is where all of
      this is going. Costs callers nothing: extension methods bind by namespace, and a caller in
      `StackExchange.Redis.Tests` (or `.Interpolated`) finds `StackExchange.Redis` by walking outward, so
      no `using` was added anywhere. `Compile Update="Groups\Streams.*.cs" DependentUpon="Streams.cs"`
      nests them in the IDE, as the repo already does for `BitFieldOperation.*` and `HotKeys.*`.

      **The accessor cannot live in the group class** - `CS0542`, a member named `Streams` inside a class
      named `Streams` - so it hangs off `RespDatabaseExtensions`, a partial that each group file
      contributes its own accessor to. That means adding a group stays one file and the accessor list
      cannot fall out of step with what exists.

      **The empty `partial class Streams` in `Streams.cs` earns its place**: it makes the file named after
      the group the one to open first, and it is where the class-level attributes live.

      **A real gain from the split, found by doing it.** `RespSurface` carried a class-wide `RS0026`
      suppression ("do not add multiple overloads with optional parameters") whose justification had to
      reason about *every command in the library at once* - the overloads are fine because each takes a
      different group as its first parameter. Per group, the same suppression is a much narrower claim: a
      dozen methods, one receiver, differing in a parameter that has no default. A suppression you can
      actually check.

      **And the down-level shim test caught the move**, which is what it is for: it reflects over the
      accessor host, so relocating one group's accessor made the counts disagree. It now scans both hosts
      while the migration runs, and `RespSurface` drops off that list when the last group leaves.

      **Remaining to migrate: 14 groups.** Each is the same three-file move plus its accessor; none of them
      needs a decision, so this can happen alongside the command work rather than as a big-bang rename.

      ### The ValueTuple rule, and a gap in the guard - 2026-09-16

      Marc, on `RangeAsync`: *"does that force value-tuple to be ref'd? that has a netfx gotcha that we
      have unit tests to prevent."* Good instinct, and the answer is **no** - checked rather than assumed.

      `var (first, second) = cond ? (min, max) : (max, min)` emits **no** `System.ValueTuple` type
      reference: Roslyn turns it into branches with direct assignments. Verified by reading the metadata
      of all six built assemblies - net461, net472, netstandard2.0, net6.0, net8.0, net10.0 - every one
      clean.

      **Rewritten as two locals anyway.** Relying on an optimisation to stay inside a rule is a footgun
      for whoever edits that line next, and the alternative costs nothing.

      **The finding worth keeping is about the guard, not the line.**
      `SanityChecks.ValueTupleNotReferenced` inspects `typeof(RedisValue).Assembly.Location` - *the one
      build the test process happened to load*. On Linux CI that is net10.0/net8.0; the net481 leg covers
      net472. So **net461 and netstandard2.0 are never scanned by it on any platform** - which is exactly
      backwards, since net461 is the framework the rule exists for.

      Not widened here, because the honest fix has a trade-off to weigh: reaching the sibling TFM outputs
      from a test means walking from the test's output directory back into the source tree, which is
      fragile and wrong for a packaged run. The robust version is a build-time check over
      `@(IntermediateAssembly)` in the library project, or a check over the produced `.nupkg`. Worth doing,
      but as its own decision rather than smuggled in beside a stream command.

      ### Operand struct vs Compose/Append - RULE, 2026-09-16

      Marc, on `CountOperand`: *"is that used in many places? we also have the option of using the
      multi-step pattern... if we use it *lots* it probably moves up out of streams?"*

      **Used once** - only `RangeAsync`. So it stays put; hoisting a type with one caller buys nothing.

      **The two mechanisms, and when each wins.** The multi-step pattern is `Compose` / `Append` /
      `Complete`, and its real shape is not three lines - the composed handler owns a rented buffer, so
      every existing user wraps the appends in `try`/`catch { cmd.Dispose(); throw; }` before completing.
      Roughly:

      | | call site | on a faulting argument |
      |---|---|---|
      | operand struct | one expression inside the interpolated string | drops the rented buffer |
      | Compose/Append | ~12 lines incl. the try/catch, **every time** | returns it, if the try/catch is remembered |

      So: **an operand struct for one or two optional tokens; Compose/Append when a command has several
      independent optional groups.** That is already what the codebase does without having said so -
      `INCREX` (lower bound, upper bound, options, expiry), `GEOADD` and `BITFIELD` compose; the
      single-token cases use operands. Writing the rule down so the next one is not a coin toss.

      **Correction - the interpolated path is NOT exception-safe.** This entry first claimed the operand
      struct was "exception-safe by construction". Marc: *"can we check that? I don't think it is; assume
      that any operand evaluation could fail, and recall that they are not, IIRC, all evaluated before it
      starts calling AppendLiteral/AppendFormatted."* Correct, and measured:

      - The handler rents in its **constructor**; the compiler lowers the holes to calls made after that
        and before the method the handler is passed to, so a throw in the middle skips the rest and
        nothing is left in a position to return the buffer. Pinned by
        `RespHandlerFaultTests.ArgumentsAreWrittenOneAtATime`, which shows an operand after the faulting
        one never runs.
      - An `ArrayPool` bucket fingerprint confirms it: a successful render leaves the pool unchanged, a
        throwing one loses a 256-byte array. (The first version of that probe watched a 256-byte bucket
        while the handler rented 128, so it could not have seen the leak, and passed. A planted-leak check
        is now part of the method - a probe that cannot detect a planted leak is not evidence.)

      **And that is fine** (Marc): *"it is exactly what DefaultInterpolatedStringHandler does today."*
      Measured too - `$"...{x}..."` where `x.ToString()` throws loses a 256-char array by the same probe.
      So this is the language's existing bargain rather than something this library invented, and a fault
      while building a command is already exceptional. The point of recording it is that nobody should
      claim the write path is exception-safe; it is not, deliberately.

      **Why it could not reasonably be otherwise** (Marc): *"if the compiler evaluated everything to the
      stack first, it might be - but that would be super nasty for the poor unsuspecting stack."* Quite:
      the only lowering that would make this safe is one that materialises every hole into a temporary
      before appending anything, and that is a language-wide choice, not one this library could make. It
      would also undo the reason the handler pattern exists at all - appending as it goes is precisely
      what stops every intermediate value having to be live at once, which is the cost the old
      `string.Format` path paid. So the interleaving is the feature, and dropping a rented buffer on a
      fault is the price the whole language already pays for it.

      The pool-identity measurement is **not** kept as an assertion: it reads `ArrayPool` internals and
      would flake under parallel runs. The evaluation-order test is what stays, because that is the fact
      that makes the drop unavoidable.

      **Does the correction change the rule? No - but the reason matters.** *"so does that change our
      policy on the single-use CountOperand? or not?"* (Marc). It does not, and not because the fault path
      is rare:

      - **The drop belongs to the interpolated form, not to the operand.** `CountOperand` neither causes
        it nor could prevent it - any hole in the same string faulting loses the buffer just the same. So
        safety cannot discriminate between "operand plus interpolated" and "no operand plus interpolated";
        it only discriminates between *interpolated* and *Compose*, which is a per-command choice made
        before the question of operands arises.
      - **And the drop is accepted**, so safety is not a tiebreaker at all. That leaves ergonomics, where
        one expression beats twelve lines for one or two optional tokens. Unchanged: `CountOperand` stays
        in Streams, single caller and all.

      **One refinement, from checking rather than assuming.** It is tempting to say the interpolated form
      only ever drops something small. It does not: the handler grows by renting a bigger array and
      returning the old one, so exactly **one** buffer is dropped - but it is whatever the command had
      grown to by the point of failure, which for a large variadic command is not 256 bytes. That does not
      change the rule, but it does mean the existing use of `Compose` on the big variadic commands
      (`GEOADD`, `BITFIELD`, `SORT`) is worth keeping deliberately rather than by habit: those are exactly
      the commands where a dropped buffer would cost something. So the fuller rule:

      > Operand struct for one or two optional tokens on a command of bounded size; `Compose`/`Append`
      > when a command has several independent optional groups **or** when its buffer grows with the
      > caller's input.

      **The finding worth acting on eventually is not the count, it is a divergence.** There are three
      mechanisms in play for "an optional token and a number": `CountOperand` (absent when `null`),
      `LimitOperand` (absent when `<= 0`), and hand-rolled `AppendFormatted(RespLiterals.Count)` inside
      bespoke operands in Geospatial and SortedSets. Each is locally defensible - they follow their own
      parameter shapes, `int? count = null` against `int limit = 0` - but two spellings of *absent* for the
      same idea is the kind of thing that eventually gets copied to a command where `0` is meaningful.

      **Trigger for hoisting:** when a third independent optional-token site appears, hoist **one** operand
      rather than a family, and settle on `null` as the single absent convention - `int?` cannot confuse
      "zero" with "unset", where `int` has to.

      ### Scan: optional "token + integer" is everywhere, spelled five ways - 2026-09-16

      Marc: *"can you do a scan for other precedents of optional 'someprefix integer' pairs... I'm
      wondering if there's a case for a more general PrefixInt32(RespLiteral, int?)."* There is, and it is
      much stronger than the `CountOperand`-alone answer suggested. **Eight sites on the new surface, five
      different spellings of "absent":**

      | Site | Token | Value | absent when |
      |---|---|---|---|
      | `Streams.CountOperand` | `COUNT` | `int?` | `null` |
      | `Streams.TrimOperand` (inner) | `LIMIT` | `long?` | `!HasValue` |
      | `Strings` INCREX (Compose) | `LBOUND`/`UBOUND` | `long?`/`double?` | `!HasValue` |
      | `Arrays.LimitOperand` | `LIMIT` | `int` | `<= 0` |
      | `Sets.RespLimit` | `LIMIT` | `long` | `<= 0` |
      | `Keys.DatabaseOperand` | `DB` | `int` | `< 0` |
      | `Geospatial` GEORADIUS (inline) | `COUNT` | `int` (default -1) | `<= 0` |
      | `Geospatial` GEOSEARCH (inline) | `COUNT` | `int` (default -1) | `< 0` |

      **The last two are the same token in the same file with different boundaries**, so `count: 0` is
      silently dropped by one and written as `COUNT 0` by the other - and the server rejects `COUNT 0`.
      Worth a decision on its own account; found by this scan rather than by looking for it.

      **Future demand:** the old surface still has 27 uses of these literals to move - `COUNT` x10,
      `LIMIT` x8, `IDLE` x3, `RANK` x2, `MAXLEN` x2, `DB` x2 - most of them optional.

      **Shapes that are related but must NOT be forced into the same type:** `SortedSets.RespLimitRange`
      (`LIMIT offset count` - token plus *two* numbers, all-or-nothing), `Arrays.OptionalValue` (a value
      with no token), and `AGGREGATE COUNT` (token plus token, no number at all).

      **Proposed:**

      ```csharp
      internal readonly struct RespPrefixedInt64(RespFragment token, long? value) : IRespArgument
      {
          public void WriteTo(scoped ref RespRequestBuilder handler)
          {
              if (value is not long actual) return;   // absent: no tokens, no count
              handler.AppendFormatted(token);
              handler.AppendFormatted((RedisValue)actual);
          }
      }
      ```

      `long?` rather than `int?`, because the lifted implicit conversion means an `int?` caller still
      binds - so one type covers `COUNT`, `LIMIT`, `DB`, `RANK`, `MAXLEN` and both INCREX bounds, instead
      of an Int32 and an Int64 twin. `RespFragment` is what `RespLiterals.*` already are.

      ### RESOLVED: `RespFragment.When` - DONE 2026-09-16

      Marc landed a better answer than the operand: **put the condition on the token, and let the value
      speak for itself.**

      ```csharp
      $"{command}{key}{first}{second}{RespLiterals.Count.When(count)}{count}"
      ```

      *"super clear to read - I know what that is outputting, when"* (Marc), and it keeps the token as the
      **preformatted `RespFragment`**, which the operand could not: a fragment is ref-like and cannot be a
      field, so an operand had to carry the token as a `RedisValue` and re-frame it at write time.

      Two pieces:

      - `RespFragment.When(bool)` - `condition ? this : default`. A `default` fragment writes no bytes and
        counts no arguments, so this simply names the idiom already spelled `cond ? RespLiterals.X :
        default` in **eight** places. Optional modifier tokens are common, so this earns its keep on the
        boolean sites alone.
      - `RespFragment.When<T>(T? value) where T : struct` plus `RespRequestBuilder.AppendFormatted(long?)`
        and `(double?)`, which write **nothing** when null. Taking the *value* rather than a bool is what
        stops the two halves disagreeing: both read the same `count`, so a token without its value - a
        malformed command, not merely a different one - takes two different variables to write.

      **No `int?` overload is needed**, and that is worth knowing rather than assuming: the lifted
      conversion `int?` -> `long?` is a *standard* conversion and beats the user-defined one to
      `RedisValue`, so an `int?` hole binds to the nullable overload and writes nothing when null. Pinned
      by `RespSurfaceKeysTests.ANullableHoleWritesNothingRatherThanAnEmptyArgument`, because the wrong
      binding would write an *empty* argument, leave the frame well-formed, and have the server read a
      different command.

      **A deliberate asymmetry, recorded so it is not "fixed" by accident:** a null `RedisValue` writes an
      empty argument; a null `long?` writes nothing. `RedisValue.Null` is a *value* - the protocol's nil -
      whereas a null of a value type is the absence of one, and an absent argument is written by not
      writing it.

      **What the normalisation actually bought**, beyond one idiom: every sentinel is gone from the new
      surface, so `LIMIT 0` and `DB 0` are expressible where they previously were not - the old spellings
      had to sacrifice a number to mean "absent", and five sites sacrificed different ones.

      **Not converted:** Geospatial's two `COUNT` sites, which build through `Compose` rather than an
      interpolated string. They are already single-sourced (the token and value are written together
      inside one `if`), so only their sentinels differ - and that `>= 0` / `> 0` split is inherited from
      the old surface, which has it too. Left alone rather than churned; the divergence is recorded above.

      **Rejected on the way, and why** - `{count:withprefix}` with the handler *backtracking* over the
      token it had already written. Implementable, and cheaper than it sounds if undo is only legal
      immediately after a fragment (so "no prefix to undo" throws rather than silently eating a key). But
      it costs a magic format string with no compile-time check, and a new invisible mechanism, to save
      writing `count` twice - where `When` writes it twice *visibly*, which is the property that makes the
      line readable.

      **Superseded:** the `RespPrefixedInt64` operand sketched below. It was built and then removed; it
      lost the preformatted token, which was the whole point.

      **The type is trivial; the work and the decision are the normalisation.** Four of those eight sites
      express "absent" with a sentinel because their *public parameter* says `int limit = 0` or
      `int count = -1`. Converting them to one convention means changing those signatures to `int?` -
      which is the right shape (it cannot confuse zero with unset) and is still free while the surface is
      experimental, but it is an API decision rather than a refactor. **Doing half would be the worst
      outcome**, since the whole value is in there being one convention.

      ### Retry categories: keep the table, guard the gaps - DECIDED 2026-09-16

      Marc proposed replacing `WithDefaultCategory(command)` with an explicit `WithRetryCategory(...)` at
      every call site, on the grounds that if we are touching every file anyway, each command should spell
      out the stance we think it has. **Investigated and rejected**, on evidence:

      - The convention was already written down, and its reason is stronger than reviewability: *"two
        writers agreeing on the bytes and disagreeing on whether a command is safe to replay is the kind
        of divergence nothing would catch."* While the old `Message` path and the new surface coexist,
        both read the same table; ~250 hand-written literals could drift from it, and from each other,
        silently.
      - **The refinement already happens.** 256 send sites, 260 `WithDefaultCategory`, and **18** explicit
        `WithRetryCategory` refinements - exactly the arguments-change-the-answer cases (`SORT ... STORE`,
        `GETEX` with a TTL, `SET` under NX, `SCAN` past the first cursor). The table's own comments say
        those are *"raised to a write where we can see the args"*, and the 18 sites are where they are
        seen.
      - So the sweep would have been ~240 restatements of the table plus 18 refinements that already
        exist. The restatements carry all the drift risk and none of the information.

      **Ad-hoc `Execute` keeps the inference too** (Marc): *"if they do stupid things: they should have
      given us the hint."* Agreed for compatibility - but the hazard is real and inverted from the obvious
      reading: a name that **parses** is the dangerous one, because the lookup succeeds and returns a
      confident answer, where an unrecognised name falls to `CommandRetryNever` and is safe.
      `Execute("XREAD", "BLOCK", 0, ...)` is treated as replayable *and cacheable* today. Documented on
      `RespContext.ExecuteAsync` rather than changed, because "they should have told us" only holds if the
      need for a hint is discoverable.

      ### Every command must declare a category - FIXED 2026-09-16

      Marc asked for a `Debug.Assert` where the table falls through to "assume never", on the grounds that
      a known command reaching it means something upstream failed to categorise it. **It would have caught
      four commands immediately**, which is the argument for doing more than assert:

      `SDIFFCARD` and `SUNIONCARD` were missing while their siblings `SDIFF`, `SINTER`, `SINTERCARD` and
      `SUNION` all sat in the read-only group - and `SUNIONCARD` is on the new surface **today**, so set
      union-cardinality was silently neither retried nor cached while intersection-cardinality was both.
      Also missing: `LMOVEM` (now with `LMOVE`, write-accumulating) and `HIMPORT`.

      **`HIMPORT` is spelled out at its current value rather than guessed upward.** Unlike `HSET`, its
      safety is not decided by the keyspace alone: it sets fields from a field set *prepared on the
      connection*, so a replay after a reconnect only works if the preamble goes with it.

      **Marc: that is a TODO, not a property.** *"It is our job to make it replayable, but that's
      deferred."* So `CommandRetryNever` here is a placeholder for work not yet done, and should be raised
      once the preamble travels with the retry. Queued below.

      ### Make the retry path prove itself: break the connection mid-group - QUEUED 2026-09-16

      Two things the suite does not currently force, and neither will be believable by inspection:

      1. **A retry group whose connection dies part-way.** This is what would catch `HIMPORT` replaying
         without its prepared field set - and, more generally, any command whose correctness depends on
         connection-local state that a resend does not carry. The preamble machinery (`IRespPreambleGate`,
         claim-on-write vs confirm-on-reply) was built for exactly this and has never been made to
         survive a reconnect under test.
      2. **A transaction across the same break.** There may already be coverage on the old surface; the
         core changes completely in the new format, so whatever exists will need rewriting rather than
         porting.

      **Timing: when `RedisDatabase` is swapped out** (Marc), not before - writing these against the old
      core would mean writing them twice, and the second one is the one that matters.

      **The table can now say "I don't know".** `TryGetDefaultCategory` returns `CommandFlags?`, because
      folded together a missing command and one deliberately categorised `CommandRetryNever` are the same
      value and nothing can tell a decision from an omission. `WithDefaultCategory` coalesces to `Never`,
      so a gap is still safe - it just stops being invisible.

      **The sweep is the guard, not the assert** (Marc: *"our CI tests run in Release, IIRC"*). Correct -
      `Debug.Assert` compiles out there, and in any case it needs the command to be *executed* by a test
      that happens to exist. `CommandCategoryTests` enumerates the enum instead: every command declares a
      category, every declared category is a real rung of the ladder, a gap still lands on `Never`, and
      the caller's own category always wins. Verified load-bearing by mutation - removing `SUNIONCARD`
      fails the sweep by name.

      **Also done:** the convention moved out of a comment in `RespSurface.Strings.cs` to the surface root
      (`RespSurface.cs`), beside the RS0026 rationale, since it is the house rule for the 14 groups still
      to migrate.

      ### .NET 11 runtime-native async: wired for discovery, cannot ship - 2026-09-16

      Prompted by the transitional shim (`async Task<StreamEntry[]>` over a `using` + `await` + project)
      and Marc's instinct that a hand-rolled `ValueTask<T>.ContinueWith` would be work in exactly the area
      the runtime is about to do better.

      **Both knobs are required, and neither works alone.** `<Features>runtime-async=on</Features>` *and*
      an assembly-level `[RuntimeAsyncMethodGeneration(true)]` - which is present in CoreLib but
      **absent from the RC1 reference pack**, so it is declared locally, the `IsExternalInit`
      arrangement. Verified on clean builds; an intermediate conclusion that the attribute alone sufficed
      turned out to be an artifact of a stale `obj/`, which is worth remembering for anything measured
      this way.

      **Measured on the shape Marc asked for** - a count, `ValueTask<long>`, nothing materialised, since
      `RedisValue` allocates for anything over eight bytes and would have polluted it. 2M iterations, an
      awaiter that suspends and resumes inline so the state-machine box is isolated from thread-pool
      scheduling:

      | | off | on |
      |---|---|---|
      | sync-complete | 0 B, 40.4 ns | 0 B, **19.6 ns** |
      | suspend x1 | 160 B, 74.4 ns | 198 B, 102.5 ns |
      | suspend x3 | 176 B, 132 ns | 234 B, 114 ns |

      The sync-completing path is **2.05x faster at zero allocation**, and that is the case a cache hit
      and a buffered pipelined reply both take. The suspending path measured *worse*; that harness resumes
      inline from `OnCompleted` rather than from real I/O, so it is **not** evidence yet - it needs a
      count command against a live server before it means anything.

      **Binary size: UNRESOLVED.** Marc asked, expecting something SlimFast-shaped. A same-TFM
      on-versus-off comparison came out at +512 bytes with identical `strings | grep d__` counts, which
      contradicts an earlier reading of the same thing, so `strings` is not a sound proxy at this scale.
      Answering it properly needs a metadata-based count of `AsyncStateMachineAttribute`, not a symbol
      grep. Not built; recorded so the next person does not repeat the bad measurement.

      **How it is wired, and why it cannot ship.** `net11.0` is appended only under
      `/p:IncludePreviewTargets=true`, **and** never while `Packing`:

      ```xml
      <TargetFrameworks Condition="'$(IncludePreviewTargets)' == 'true' and '$(Packing)' != 'true'">$(TargetFrameworks);net11.0</TargetFrameworks>
      ```

      **The Packing test has to be part of that same condition.** Expressing it as a separate
      `<IncludePreviewTargets Condition="'$(Packing)'=='true'">false</IncludePreviewTargets>` looks right,
      builds, and **put net11.0 in the .nupkg** - because `/p:IncludePreviewTargets=true` is a *global*
      property and a project-level assignment cannot override one. Verified by packing with the opt-in
      forced on and checking the package contents: 0 net11.0 entries.

      **Keyed on an opt-in, not on `Configuration`**, because Release builds are exactly what benchmarking
      needs. And the opt-in is what makes the merge date a non-question: the target is not there unless
      somebody asks for it.

      **The cost to be aware of:** `global.json` now says `allowPrerelease: true`, which means *every*
      build - including the shipping TFMs - uses the .NET 11 RC compiler, not just the preview target.
      The full Release build and suite pass under it, but that is a bigger consequence than adding a TFM
      and should be revisited before any actual release build.

      ### Measured: the machinery, with the socket removed - 2026-09-16

      `StreamRangeMachineryBenchmarks`, a fake executor handing back one pre-built reply (retained per
      call, so the harness allocates nothing). Empty **and** 1000x10, inline-completion **and** forced
      suspension. Short jobs, in-process toolchain - indicative, not publication-grade.

      **Why not a count** (Marc): a count is one await through the surface; only an aggregate reply
      exercises the shim's shape. **Why empty AND large**: empty alone flatters the array paths, since the
      deferred shape still allocates its reply object. **Why in-process**: an empty XRANGE round trip is
      tens of microseconds and would bury a tens-of-nanoseconds difference entirely - so these numbers
      measure machinery and must never be compared with server-based ones.

      | shape | entries | suspend | net10 | net11 off | net11 on |
      |---|---|---|---|---|---|
      | Deferred | 0 | no | 124.6ns / 104B | 125.0ns / 104B | **114.1ns** / 104B |
      | Deferred | 0 | yes | 819.5ns / 536B | 3013.7ns / 536B | 3128.4ns / **488B** |
      | DeferredWalk | 1000 | no | 1135us / **186B** | 1052us / 186B | 1046us / 186B |
      | TransitionalArray | 1000 | no | 1401us / **392,210B** | 1390us / 392,209B | 1380us / 392,209B |

      **1. The deferred view is the big win, and it is independent of any of this.** At 1000x10 the walk
      allocates **186 B** against the array shape's **392,210 B** - about 2,100x - and is ~19% faster. The
      design's claim, now measured through the real API rather than a prototype.

      **2. Runtime async is worth much less here than the microbenchmark suggested.** ~9% on the
      inline-completion path and ~48 B saved per suspension - not the 2x a tight loop showed, because
      through the real surface the protocol work dominates the machinery. Real, but not a reason on its
      own.

      **3. The control column paid for itself immediately.** Suspension went 819ns -> 3014ns from net10 to
      net11 **with runtime async OFF** - so the dramatic regression is .NET 11 (or the harness on it), NOT
      runtime async. Without that column the obvious and wrong conclusion was right there. It also
      matches, and explains, the "suspend measured worse" result from the earlier scratch harness, which
      had no control.

      ### CORRECTION: the suspend rows above were measuring the harness - 2026-09-16

      Marc, on the table: *"3rd vs 4th row, net10 column is suspicious - is this table all just noise?"*
      It was not noise. It was worse: **systematic**. The suspending walk measured ~8% *faster* than the
      inline one, and that inversion survived a full job at roughly **twenty standard errors** - 1,152,157
      +-3,916 against 1,061,410 +-4,468. Suspension cannot make work faster, so the harness was wrong.

      **Cause:** the fake executor suspended with `Task.Yield()`, which moves the continuation to a
      thread-pool thread - so everything after the await ran in a different threading and GC context. It
      was not "the same work plus a suspension", it was different work somewhere else.

      **Fixed** with an awaiter that suspends and resumes inline, forcing the state-machine box to exist
      without a thread hop. Re-measured on net10, full job, now monotone everywhere:

      | shape | entries | inline | suspend | delta |
      |---|---|---|---|---|
      | Deferred | 0 | 121.5ns | 168.3ns | +46.8 |
      | DeferredWalk | 0 | 153.7ns | 203.3ns | +49.6 |
      | TransitionalArray | 0 | 131.3ns | 176.0ns | +44.7 |
      | DeferredWalk | 1000 | 1,146,700ns | 1,152,187ns | +5,487 |
      | TransitionalArray | 1000 | 1,400,119ns | 1,399,993ns | -126 |

      **Suspension costs a flat ~47ns and one box**, agreeing to within 5ns across three differently
      shaped benchmarks - agreement the old numbers never showed. The old figure was 819-940ns, so about
      **80% of it was thread-pool scheduling**, not the state machine.

      **Everything net11 above is therefore void** - on, off, and the "3.7x regression" all sat on the
      broken suspend path and must be re-run against the fixed harness.

      **What survives untouched:** the deferred-view result, which was never in the suspend dimension -
      1,147us and *zero* Gen0 against 1,400us and 23.4 Gen0 at 1000x10. And the allocation column
      generally, which is counted rather than timed.

      **The lesson worth keeping** (Marc): *"it's almost like accurately measuring asynchronous code with
      context/thread-switching might somehow be nuanced and brittle."* The tell was an ordering that could
      not be true, not a number that looked odd - which is why the inline/suspend pair is worth keeping in
      every future row: it is a self-check, not just a data point.

      **The earlier note that regression is NOT a finding** - 3.7x on `Task.Yield()`-based suspension with identical
      allocations is too large to believe from a short in-process job on an RC runtime. It needs
      reproducing outside BenchmarkDotNet before it is worth anyone's attention upstream.

      **Still to do:** the old `RedisDatabase` row, which cannot take an `IRespExecutor` at all - it needs
      the in-process managed server, and therefore its own table, since the regimes are not comparable.
      And the cache-on/cache-off split for rows 2 and 3.

      ### Runtime async, measured properly - CONCLUSION 2026-09-16

      Fixed harness (inline-resuming awaiter), full jobs, all three configs, plus a `DeferredRead` row
      that converts every field exactly as `ToNameValueEntry` does - so it is the apples-to-apples partner
      for `TransitionalArray` rather than the traversal-only floor `DeferredWalk` measures.

      **Runtime async on vs off, both net11, empty reply - consistent across all four shapes:**

      | | inline | suspending |
      |---|---|---|
      | off | 122.4 / 156.6 / 159.7 / 131.8 ns, 272B | |
      | on | 112.2 / 144.4 / 146.0 / 120.8 ns, 496B | |
      | verdict | **~8% faster** | **~22% slower, +224B** |

      Errors under 2ns throughout, and all four shapes agree - so both halves are real, not noise.

      **1. It helps the inline path and hurts the suspending one.** ~8% off inline completion; ~22% and
      +224 bytes onto every suspension. For this library that is the wrong way round: a real Redis call
      *waits on a socket*, so suspending is the common case, and the inline win applies only to cache hits
      and already-buffered pipelined replies.

      **Conclusion: not worth adopting on RC1 evidence.** Re-test at GA. The wiring stays because it costs
      nothing switched off and makes re-testing a one-line build flag.

      **2. .NET 11 itself is neutral** - net10 and net11-off agree to ~1% on every row. Which finally
      disposes of the "3.7x suspension regression" reported earlier: that was entirely the `Task.Yield()`
      harness, and the control column now proves it from both directions.

      **3. At scale none of it matters** - every 1000-entry row moves less than 2%, because protocol work
      dominates the machinery by three orders of magnitude.

      ### CORRECTION: the deferred view is not faster, it is smaller - 2026-09-16

      The earlier "18% faster and 2,100x less memory" compared `DeferredWalk` against
      `TransitionalArray`, which is not a fair pairing: the walk only traverses, while the array path
      converts every value. `DeferredRead` does the same conversions and does not store them:

      | net11 on, 1000x10, inline | time | allocated |
      |---|---|---|
      | DeferredRead | 1,441,171 ns | **186 B** |
      | TransitionalArray | 1,376,975 ns | 392,208 B |

      **So the deferred shape is ~5% SLOWER** (9.6% on net10), **for ~2,100x less memory.** That is still
      a good trade and it is the trade the design was making - one forward pass is what the eager shape
      wants, which the design notes already said - but "faster and smaller" was wrong and the corrected
      claim is narrower: *you stop paying for materialisation you did not need, and the walk itself costs
      a few percent more than the pass it replaced.*

      `DeferredWalk` remains useful as the floor: a consumer that only needs structure, not values, pays
      1,022us against 1,377us and allocates nothing.

      ### The frame already knew its command - DONE 2026-09-16

      Marc: *"is that cheaply from the original store, or by parsing? because if cheaply: presumably we
      already had that, and could have done that from Send today?"* Cheaply - `RespFrame.Command` is a
      stored property, written when the command hole is appended. So yes, we already had it, and `Send`
      could have applied the retry category from the beginning.

      **Verified safe before doing it**: 227 call sites name a command literally, and a scripted check for
      "declares a category for a command it does not render" found **zero** real mismatches. (One flag was
      a six-line lookback catching a neighbouring method: `CountFlags` declares `PFCOUNT` for the `PFCOUNT`
      site, with `PFMERGE` above it.) The remaining ~33 pass a `command` variable, which matches by
      construction.

      **So `WithDefaultCategory(request.Command)` now happens in the four send entry points**, and **255
      call-site calls are gone.** What this buys beyond deletion:

      - **A new command cannot arrive without a category**, which was the real risk - and it retires the
        analyzer idea, since you cannot forget a thing you no longer write.
      - **Ordering becomes structural.** The cache reads flags to decide whether a reply may be served or
        stored, so the category has to be settled before `PermitsCaching` sees it. At the call site that
        was convention; in `SendAsync` it is the order of the statements.
      - The 18 argument-dependent refinements are untouched and still win, because `WithRetryCategory` is
        caller-wins.
      - Ad-hoc `Execute` is unchanged: `frame.Command` is the parsed command for a recognised name and
        `UNKNOWN` (-> `Never`) otherwise, which is exactly the compat Marc asked for.

      **Four standalone assignments kept deliberately** - `CountFlags`, the BITFIELD selector, `SORT`, and
      `ZRANGESTORE`. Each reads `flags` locally after setting the category (feature probes, routing
      demotion), and the duplicate call in `Send` is a no-op because the category is already set. Changing
      those wants individual review, not a regex.

      **A test caught the version bump rather than this change**: `ExceptionFactoryTests.CanGetVersion`
      pinned the major to `[2-3]`, so v4 failed it. Widened to any major - a version assertion that must be
      edited every major release is asserting the wrong thing; what matters is that `GetLibVersion`
      returns something version-shaped, since it appears in every connection exception message.

      ### XRANGE as the template: a command factory and a handler - DONE 2026-09-16

      Marc's two moves, taken together, on the one command:

      **1. The command text is composed once.** `RangeCommand(in RespContext, ...)` returns a `RespFrame`
      and is the only place that decides XRANGE-vs-XREVRANGE, swaps the bounds, writes the optional count
      and validates. *"OurMagicCommandThing"* turned out to need no new type at all: `RespContext.Render`
      already returns `RespFrame`, `SendAsync(ref RespFrame, ...)` already consumes one.

      This is not hypothetical duplication: **44 distinct command texts are currently written more than
      once on this surface, 48 redundant copies** - `ARSCAN` appears identically in `ScanAsync` and
      `ScanArray`, and so on. Exactly what `RedisDatabase`'s message factories exist to prevent, already
      reproduced.

      **2. The array shape is served by a handler, not by projecting the reply.** `RangeArray` sends with
      a `StreamEntriesHandler` that calls the same `ParseRedisStreamEntries`; the transitional shim is now
      one expression with no `async`, no `using`, and no reply object.

      **Rejected on the way there** - a custom `ContinueWith`. Marc raised three options; all three lose:
      the baseline helper saves nothing, `AsTask().ContinueWith()` allocates *more* (a Task, a continuation
      object, and the result Task) with scheduler hazards on top, and a pooled `IValueTaskSource` still
      has to end in `.AsTask()` because `IDatabase` demands `Task<T>` - so it buys maybe 20-40 bytes for a
      token-versioned, thread-safe, recycle-on-`GetResult` primitive. The answer was not a better
      continuation, it was not needing one.

      **Measured, `HandlerArray` against `TransitionalArray`:**

      | | inline | suspending |
      |---|---|---|
      | handler | 105.0ns / **48B** | 151.0ns / **224B** |
      | projection | 121.5ns / 104B | 165.8ns / 280B |

      **~14% faster and a flat 56 B less**, on every row. That 56 B is the reply object; the harness
      cannot see the other half, because both benchmark methods are a single `async Task<int>` and the
      second async layer only existed in the real shim. Adding the separately measured ~120 B per
      suspending layer: **~176 B per call on a real round trip.**

      **What it costs:** `ToArray()` loses the incidental coverage it had from being how the shim worked -
      the "exercised by the whole existing stream suite" argument in the one-parse-two-readers entry no
      longer applies to it. It keeps `RespRangeReplyTests`, and both paths still call one function, so
      they still cannot drift; only the breadth of coverage changes.

      **Next:** the same two moves on the other 43 duplicated command texts.

      ### Two kinds of `ref`, and why the frame keeps its - DECIDED 2026-09-16

      Marc: *"do we need that? I assume we mostly do it to claim ownership by nuking the old value... but
      it doesn't actually protect us"* - `var y = x; SendAsync(ref x);` and the alias survives. True, and
      the question separates into two cases that happen to share a keyword.

      **`ref RespRequestBuilder` - structural, nothing to decide.** The compiler builds the handler from
      the interpolated string and passes it by reference; that is how `$"..."` binds to
      `RespRequestBuilder` at all. It is also a `ref struct` whose `Complete()` moves ownership out, which
      `RespAppend.Append` depends on - its remarks already record the `CS8350`/`CS8352` reason it cannot
      be an instance method. Invisible at the call site, and not droppable.

      **`ref RespFrame` - deliberate, and kept.** `Detach()` sets `_buffer = null`, and with `ref` that
      reaches the caller's variable, so a spent frame *stays* spent. Without it the caller's copy still
      points at a buffer now owned by a lease, and the second use **double-returns a pooled buffer** - the
      invisible-corruption class, where the symptom surfaces somewhere unrelated.

      **It detects rather than enforces, and the distinction is the point.** An alias is a deliberate
      copy; what `ref` catches is the accidental shape - the same variable used twice:

      ```csharp
      var frame = RangeCommand(...);
      for (var attempt = 0; attempt < 3; attempt++)
          await ctx.SendAsync(ref frame, flags, handler, default);   // 2nd iteration throws, correctly
      ```

      A retry loop is exactly what someone writes without thinking, and by value it double-frees silently.
      `RespFrame`'s own `Dispose`/`Detach` remarks already state the struct-copy limit, so the boundary is
      documented; documentation *instead of* `ref` would be strictly weaker, because it catches nothing.

      **The cost is smaller than it looks.** `ref` needs an lvalue, so the factory pattern must hoist -
      but only the ~44 command factories ever hold a frame. Every ordinary call site uses the interpolated
      overload and never sees one.

      **Name the local `cmd`, not `req`.** `RespRequest` is a different type in this codebase - it is what
      `Detach()` produces and what reaches the executor - so a local called `req` holding a `RespFrame`
      would be a false cousin of it. `cmd` also matches what the factories are called.

      **The rule that goes with it:** compute the flags **before** rendering, so nothing in the argument
      list can throw between renting the buffer and handing it to the send. That window is the one real
      cost of the hoist, and it closes by ordering rather than by types. It holds everywhere today - the
      255 inline category calls that used to sit in the argument list are gone, and the four remaining
      flag computations happen before the render.

      ### `RespFrame` -> `RespRequestFrame` - RENAMED 2026-09-16

      Marc, on the local being `cmd`: *"'command' and 'request' have more semantic meaning than
      'frame'"* - and then the sharper point: **the wrong two types looked like siblings.**

      | | names the | |
      |---|---|---|
      | `RespCommand` | the verb | *"XRANGE"* |
      | `RespRequestFrame` | the verb plus its arguments, composed and owned | *"XRANGE 1 4 COUNT 10"* |
      | `RespRequest` | the same, ref-counted and ready to dispatch | |

      **`RespCommand` stays, and the distinction is Marc's:** *"XRANGE is a command; XRANGE 1 4 NOLOOP
      AUTO is a request."* `FLUSHALL` is arguably both, but that is a degenerate instance rather than a
      counterexample - the types still differ, one being a name and the other a rendered payload with an
      argument count and key marks.

      **`RespRequest` stays too, on stability rather than aesthetics:** 65 of its references are in tests,
      mostly the 21 fake executors implementing `IRespExecutor` - which is also the interface an outside
      adopter implements. The cheap name to move was the one nobody outside implements.

      **Rejected: `RespOwnedRequest`.** It was the obvious pairing until the code said otherwise -
      `RespRequest` holds `RefCountedBuffer? _lease` where **null means borrowed**, so a `RespRequest` can
      itself own. Ownership is not the axis that separates them; dispatch-readiness is.

      **A bonus the rename found:** `RespFrameWriter` and `RespFrameScanner` use "frame" in its correct
      wire-structure sense, so the interpolated `RespFrame` was the odd use of the word among its own
      neighbours. 97 references across 22 files, none in the fakes.

      **And the local is `cmd`, not `req`**, because `RespRequest` is a real and different type here, so a
      `req` holding a request *frame* would be a false cousin of it.

      ### The send surface, laid out - and a claim I got wrong - 2026-09-16

      Marc asked to see the whole dispatch shape in one place. **Seven entry points**, splitting in two:

      | | returns | request form | handler | token |
      |---|---|---|---|---|
      | `Send<T>` | `T` | `ref RespRequestFrame` | required | required |
      | `SendAsync<T>` | `ValueTask<T>` | `ref RespRequestFrame` | required | required |
      | `SendAsync<T>` | `ValueTask<T>` | interpolated | optional | defaulted |
      | `SendAsync` | `ValueTask` | interpolated | none | **was absent - now added** |
      | `Send<T>` | `T` | interpolated | optional | defaulted |
      | `SendWithPreambleAsync<T>` x2 | `ValueTask<T>` | preamble + `ref` frame | required | defaulted |

      **Frame form is the plumbing** - everything explicit, nothing defaulted, which is what the command
      factories call. **Interpolated form is the call site.** The two preamble overloads exist for
      `SCRIPT LOAD` + `EVALSHA` adjacency and differ by *who owns the preamble*: a freshly rendered frame
      (`ref`) versus an already-detached, shared one (`RespRequest`) - the cached `SCRIPT LOAD`, which must
      not be consumed by a single send.

      **The delegate/interface mix is not arbitrary - it splits on whether the pipeline must *interrogate*
      the thing or merely *call* it.** `IRespHandler<T>` is an interface because the pipeline type-tests it
      for `IRespPayloadHandler<T>` to decide whether a result may retain the buffer; `IRespArgument` is one
      because it is used under a generic constraint so a struct does not box. `Projection` and
      `PairProjection` are delegates because they take `ref RespReader`, which an interface method cannot,
      and because they vary per call site rather than per shape. `Func<RespPayload, TReply>` is a delegate
      because a cached `static readonly` factory is all it needs.

      **Fixed: the no-reply overload had no `CancellationToken`.** Its docs argue three deliberate choices
      and say nothing about cancellation; it passed `default` inward. Same oversight as the surface-wide
      one. Four methods were affected - `MergeAsync`, `SetAsync`, `SetByIndexAsync`, `TrimAsync`.

      **NOT done, because I was wrong: deleting the sync `Send` pair.** I reported "zero call sites
      anywhere" and Marc agreed to remove them on that basis. **There are 24, in tests** -
      `RespEndToEndTests`, `RespClientCacheTests` - and my grep missed them because it required `Send<` or
      `Context.Send(` while the tests write `ctx.Send(...)`. So the sync path is neither dead nor
      untested: the cache tests are exactly what exercises it. The deletion was reverted and the premise
      corrected.

      **What remains true** is the narrower observation: the sync frame overload carries ~93 lines of cache
      probe/fill/stampede logic against the async one's 47, and **no production code calls it** - only
      tests. Whether a public synchronous dispatch path is something v4 wants is a real question; it is
      just not the open-and-shut one I presented.

      ### Where a bespoke parse lives: one handler per group - DECIDED 2026-09-16

      Prompted by *"can the handler be a lambda?"*, which turned into a better question once the numbers
      were on the table. **Three tiers, and the top one is the overwhelming majority:**

      | tier | when | sites |
      |---|---|---|
      | **registry** - `SendAsync<T>($"...", flags)` | the result type already has a handler | **224** |
      | **named handler** | a bespoke parse | **33** |
      | lambda | bespoke and unshared | does not exist |

      **Lambdas: declined.** `IRespHandler<T>` is an interface, so a lambda needs a new delegate-taking
      overload, and something must present it to the pipeline as a handler: a class adapter allocates per
      call - the exact cost this design removes - and a struct adapter meets a `handler is
      IRespPayloadHandler<T>` test whose boxing behaviour would want measuring. A delegate overload could
      legitimately skip that test, since a handler holding only a `ref RespReader` cannot retain the
      buffer - but that is a parallel `Parse` path, which is more machinery than the ~100 lines it saves.
      Of the 19 handler types, four implement *two* `IRespHandler<T>` interfaces on one instance and one
      is `RespReplyHandler<TReply>`; none of those five could be a lambda anyway.

      **Merging `StreamEntriesHandler` with `RangeReplyHandler`: declined.** Mechanically fine - the type
      test resolves correctly for each `TResult`, and dual-interface handlers are precedented four times.
      But **the two do not share a parse**: one calls `ParseRedisStreamEntries`, the other does not parse
      at all - it retains the payload and hands back a deferred view. The four precedents are the nullable
      and non-nullable spellings of *one* parse, which is the case that justifies sharing an instance.
      Merging would also mean either duplicating the `TryRetain`/lost-the-race-so-copy logic that
      `RespReplyHandler<TReply>` exists to hold, or hiding that type inside the merged one - and it is the
      **public door** an outside adopter uses for their own reply shapes, so bypassing it would stop the
      library eating its own dog food.

      **Registering `StreamEntry[]` in `RespHandlers`: declined** (Marc): *"I'd rather not put exotic
      (meaning: group-specific) handlers into RespHandlers."* Right - the shared registry is for types any
      command might answer with, and filling it with shapes only one group can produce makes it a dumping
      ground.

      **Done instead: one handler class per group.** `StreamEntriesHandler` becomes
      `StreamTypesHandler`, which will grow the rest of the stream exotics - `XCLAIM`, `XREAD` and
      `XREADGROUP` all answer `StreamEntry[]`, and the `XINFO` shapes are still to come. Explicit
      interface implementations **from the first one**, because every `IRespHandler<T>.Parse` has the same
      parameter list and differs only in return type, which C# cannot overload on - so writing the first
      implicitly would force churning it when the second arrives. Named static accessors so call sites
      need no cast, matching how `Float32Handler` and friends already read.

      ### `RespKey`: a borrowed key - DONE 2026-09-16

      Prompted by PRs #2578 (`ReadOnlyMemory<byte>` for key prefixes) and #2844 (allocation-free ad-hoc
      `Execute`), both of which foundered on the same rock. Reading the **intent** rather than the diffs:
      *the library should not tax you for not using its own types.*

      **Why those could not have it and this can.** #2844's blocker was
      *"key-prefixing becomes untenable when argument roles are ambiguous"* - with `Execute(cmd,
      object[])` nothing knows which argument is a key. Here **overload resolution is the role
      declaration**: `AppendFormatted(RedisKey)` applies the prefix, marks for invalidation and folds the
      slot; a value hole does none of it. #2578's blocker was lifetime - a `RedisKey` *stores* its bytes.
      The writer **copies each hole into its rented buffer before `AppendFormatted` returns**, so a
      borrowed key never has to outlive the call.

      **Three inputs, one field.** `ReadOnlyMemory<byte>` collapses into the span case, because the only
      reason to keep a `Memory` distinct from its `Span` is to outlive the call and nothing here does; a
      `char` span is reinterpreted with `MemoryMarshal.AsBytes` and a flag. That is ~24 bytes against ~40
      for the obvious two-spans-and-a-discriminator layout. **The flag is explicit rather than inferred
      from emptiness, because an empty key is legal in Redis** - inferring would work in every test until
      somebody stored under `""`.

      **Constructors, not `AsKey()` extension methods** (Marc: *"I worry that these extension methods
      pollute and confuse normal usage a little"*). An extension would offer itself on every `string` and
      span in any file importing `StackExchange.Redis`, which is nearly all of them. Four characters at
      the call site against not touching a type everybody already uses.

      **And a correction:** I had said `$"{someString}"` was already fine for keys. It is not - a bare
      string binds to `AppendFormatted(RedisValue)` through the implicit conversion, so it gets no prefix,
      no mark and no slot. Silent, and exactly the failure the type exists to prevent.

      **Down-level:** `Encoding.GetBytes(ReadOnlySpan<char>, Span<byte>)` is supplied everywhere by
      `System.Memory`, but `GetByteCount(ReadOnlySpan<char>)` is not - so net461/net472/netstandard2.0
      take the `char*` overload rather than allocating a `char[]` to ask the question.

      **Pinned by `RespKeyTests`:** all three sources produce byte-identical frames to the owned key
      (including the empty key and non-ASCII, where the UTF-16 reinterpretation would show); the context
      key prefix is applied where a value hole gets none; and the cluster slot folds identically - that
      last one needing `new RespContext(serverType: ServerType.Cluster)`, since slot folding is skipped
      off-cluster and the assertion would otherwise pass at `-1` without testing anything.

      **CORRECTION - the dynamic case is covered too** (Marc: *"dynamic works via the composed
      builder"*). I had drawn the line at dynamic-versus-fixed, reasoning that a run-time-sized argument
      list forced the array form, where a ref struct cannot go. Wrong: `Compose`/`Append` builds **in
      place**, and every `Append` is an interpolated hole that consumes immediately, so a borrowed key is
      as welcome there as in a fixed interpolation. Pinned by
      `RespKeyTests.ADynamicArgumentListTakesBorrowedKeys`.

      **The real line is *build in place* versus *hand over a collection*:**

      | shape | spans? | |
      |---|---|---|
      | fixed interpolation | yes | `$"{cmd}{new RespKey(k)}"` |
      | dynamic, built in place | **yes** | `Compose` + `Append` in a loop |
      | a pre-built argument collection | no | `ReadOnlyMemory<RedisKeyOrValue>` has to store them |

      So `RespKey` serves everything except handing over an argument list you already built - which is the
      one case that genuinely needs the storage it is paying for. That closes #2844's intent rather than
      an adjacent one.

      ### `RespCommandHandler` -> `RespRequestBuilder` - RENAMED 2026-09-16

      Marc: *"intent over arcane language specifics."* The old name was wrong on **both** axes, which is
      why it reads worse than `RespFrame` did:

      - **"Handler"** named the C# mechanism - the interpolated-string-handler pattern - rather than the
        job. And interpolation is only *one* way to drive the type; `Compose`/`Append`/`Complete` is the
        other, and that is a builder by any reading. The `[InterpolatedStringHandler]` attribute still sits
        on the type, so the pattern is one click away for anyone who needs it.
      - **"Command"** was the wrong noun by the distinction already settled here: a command is the verb
        (`XRANGE`), a request is the verb plus its arguments. This type accumulates verb *and* arguments,
        so it was never building a command.

      The progression now reads as the three states it actually has:

      ```
      RespRequestBuilder --Complete()--> RespRequestFrame --Detach()--> RespRequest
      ```

      accumulating -> owned and complete -> dispatched and shareable.

      **"Builder" is existing house vocabulary**, not an import: `CircuitBreaker.Builder`,
      `HealthCheck.Builder` and `MultiGroupOptions.Builder` already mean "accumulates, then produces". 136
      references across 31 files, free while SER010 is experimental.

      ### Layering: what could move to RESPite - RAISED 2026-09-16

      Marc: *"we tried very hard to make RESPite agnostic... if any of these pieces can live in there, it
      may be preferable."* Agreed, and the audit says most of it already does:

      - **Already in RESPite**, correctly: `RespReader`, `RespValue`, `RespAggregate<T>`,
        `RespPairAggregate<T>`, `PairProjection`, `IsAllJaggedPairs`, the slice-aware constructor,
        `RefCountedBuffer`.
      - **Could move, and should**: `RespPayload`. Every line of it is pooled bytes and reference counts;
        it imports only RESPite namespaces. Its **only** tie to this library is the internal
        `ShareAsResult()`, and that inverts trivially because `RespResult.Share` already takes the
        primitives rather than the payload. `RespReply` follows it, since its only tie is the constructor
        parameter type.
      - **Cannot move**: `ToLease<T>` (needs `ReadOnlyLease<T>`, shipped SE.Redis API - moving it is a
        type-identity break), and the typed replies themselves, which mean Redis things.

      **Not done today**, deliberately: `RespPayload` has 203 references across 37 files, and that diff
      would drown the feature it is attached to. `RespReply` is therefore parked in namespace
      `StackExchange.Redis` - which is where the entity types were already going - so it is in the right
      namespace now and follows `RespPayload` whenever that moves.

      ### The `.Interpolated` namespace does not survive - RAISED 2026-09-16

      Marc: *"we have an extra `.Interpolated` namespace that I don't think makes sense at all - I think
      we've used it by default... I don't think that survives on the 'real' API."* Agreed. It names the
      **implementation mechanism** - interpolated string handlers - rather than anything a caller cares
      about, and the entity types are already going to plain `StackExchange.Redis` (that is what makes the
      group accessors work without a second `using`). Having half the surface in one namespace and half in
      another is worse than either.

      Not fixed today - it is a rename touching most of the spike - but it is the kind of thing that
      cannot be fixed after shipping, so it is on the list **before** the experimental attributes come
      off. New types are being placed in `StackExchange.Redis` from now on, so the eventual move shrinks.

      ### Shipped types cannot be nested, so the twins coexist - 2026-09-16

      Marc asked whether `ListPopResult` becomes structurally inner to a `static class Lists`. For the
      **new** window twin, yes: `Lists.RespListPopResult`, exactly as `Streams.RespStreamEntry`. For the
      **shipped** `ListPopResult`, no - it is in `PublicAPI.Shipped.txt`, and nesting changes its metadata
      name, which is a binary *and* source break for every caller.

      That is not a compromise, it is the arrangement the `Resp` prefix was chosen for: both names exist
      at once, `using static` stays unambiguous, and the old type can be demoted later (delete the `this`)
      without a binary break.

      **One thing this does open up**, worth deciding separately: each group's *extension methods* could
      live in its own static class (`Streams`, `Lists`, ...) rather than in the single partial
      `RespSurface`, leaving `RespSurface` holding only the group accessor properties. `CS0542` forces
      that split anyway - a member named `Streams` cannot live in a class named `Streams` - so the
      accessors have to be somewhere else regardless.

      ### The way back to the old shapes: `To*()`

      For *"I accept the overhead, but I have existing code paths that want the old shape"* (Marc,
      2026-09-16) - a projection at the root **and** at the leaves:

      ```csharp
      StreamEntry[] old = result.ToArray();      // root: materialise the whole reply
      StreamEntry one = entry.ToStreamEntry();   // leaf: materialise one
      ```

      **`To*` and not `As*`, because the codebase already separates them by cost**: `As*` is cheap and hands
      back something you own (`RespValue.AsInt64`, `AsBoolean`); `To*` materialises
      (`ReadOnlyLease<T>.ToArray`). A caller reading `ToArray()` is being told it costs, which is exactly
      the signal the rejected implicit conversion would have suppressed. **This is that capability, made
      explicit** - the same escape hatch, with the price visible.

      **And it is not a sidecar: it is how the old API is implemented.** `TransitionalDatabase` has to
      satisfy `IDatabase.StreamRange`'s `StreamEntry[]`, and it does so by calling exactly this projection.
      So the array form becomes *walk + materialise* over one parse rather than a second parse that can
      drift - the same lesson `ScriptEvalMessage` taught - and the escape hatch is exercised by the whole
      existing test suite rather than by whoever remembers to call it.

      ### Where the new types live, and what they are called - DECIDED 2026-09-15/16

      **Nested in a per-group static class, with the `Resp` prefix**: `StackExchange.Redis.Streams.RespStreamEntry`.
      The namespace stays `StackExchange.Redis`, so **no new `using`** - extension methods are found by
      namespace, and `Streams` is a *class* in the one everybody already imports.

      **Nesting is the feature** (Marc): it keeps the new types tidy, contained and localised instead of
      polluting a shared namespace. Most usage is `var`, so the qualified name really only surfaces on
      parameters, where `Streams.RespStreamEntry` is plenty clear; anyone wanting it shorter writes
      `using static StackExchange.Redis.Streams;`.

      **The `Resp` prefix, because the decision was already made once.** `RespValue` is the window
      counterpart to `RedisValue` and its own docs say so, so `Redis*` -> `Resp*` is an established
      transformation rather than a new convention, and `Resp*` is already how the new surface names itself
      (28 types). Rejected: **`Reader`** - actively wrong, since `RespValue` is documented as *"the storable
      counterpart to `RespReader`"*, a bookmark rather than a walker; **`Window`** - accurate but names the
      mechanism, which is what doc comments are for; **`View`** - fine, but a second convention where one
      already exists.

      **The two choices are not redundant - they cover different failures, and together they close both.**
      Verified by compiling each shape:
      - `extension(...)` property `Streams` cannot live in class `Streams`: **CS0542**, member names cannot
        match their enclosing type. So the accessors stay in their own class (today `RespSurface`, already
        partial across 15 files). That is a constraint, not a preference.
      - Nesting *alone*, reusing shipped simple names, breaks `using static`: `StreamEntry` becomes
        **CS0104**, ambiguous between the shipped type and the nested one. Loud rather than silent, but it
        takes the escape hatch away.
      - Nesting **plus** the prefix compiles clean in every combination tried, `using static` included - so
        the prefix is what makes the escape hatch usable, and nesting is what keeps the namespace tidy.

      **One name to break the pattern on:** `RedisStream` -> `RespStream` would sit one letter from
      `RespStreams`, the command group, while being an entirely different kind of thing. It is really
      *(key, entries)*, so `RespNamedStream` (or `RespStreamResult`) says what it is and cannot be misread.

      **No implicit conversions from the new shape to the old.** Raised as a migration aid and rejected:
      an implicit `RespAggregate<T>` -> `StreamEntry[]` hides a 55KB allocation and an O(n) walk behind an
      invisible conversion, fires where nobody asked (overload resolution, `var` chains, collection
      initialisers), and - worst - breaks the disposal contract, since the converted-from root is then
      typically never disposed and the pooled buffer goes to the GC instead of back to the pool, which is
      invisible from outside. It also contradicts a convention already in force: `RespValue` distinguishes
      `As*` ("hands back something you own") from `Frame`/`TryGetSpan` ("lend you the bytes"), and a named
      `ToArray()` sits inside that convention where an implicit operator sits outside it and is silent about
      which side it is on. Migration is driven by the new surface being complete and discoverable, and by
      old commands being demotable later (delete the `this`) without a binary break.

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

      - **Do NOT halve a map's count to report pairs** - raised 2026-09-16 for RESP2/RESP3 consistency, and
        measured to already be the case. The same `HGETALL` reply is `*4` on RESP2 and `%2` on RESP3, and
        the reader **already normalises**: both report **4** and both walk 4 elements. The protocol shows up
        in `Prefix` and nowhere else. Halving for maps would make RESP3 say 2 where RESP2 says 4 - inventing
        the difference it was meant to remove - and would break the invariant that matters more here, since
        we expose no `Read()`-call-count API: **`Count` is what the enumerator yields.** Pairs belong to a
        pairwise *projection*, where `T` is the pair and both shapes give 2.
        Pinned by `RespAggregateTests.Resp2AndResp3AgreeOnCountAndWalk`.

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
