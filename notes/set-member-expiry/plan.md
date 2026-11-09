---
feature: set-member-expiry
client: se-redis
hld: {path: none, sha: none}
tracks: [redis/redis#15938]
target_version: "8.12"
decision_class: C
conventions:
  - .claude/skills/implement-resp-command/SKILL.md#Steps
  - .claude/skills/implement-resp-command/SKILL.md#Tests - the two layers that matter
  - AGENTS.md#Public API tracking (important — easy to trip over)
estimated_size: medium
integration_targets: []
unit_targets: [RoundTripUnitTests.SetMemberExpiryRoundTrip, ResultProcessorUnitTests.BasicAggregates, KeyNotificationUnitTests]
open_questions: 6
---

# Set-member expiration: StackExchange.Redis implementation plan

> **Revised in review (PR #3340):** the add-with-expiry surface shipped as **`SetAdd` overloads taking
> `Expiration`** — `SetAdd(key, value, Expiration, When, flags)` and `SetAdd(key, values, Expiration, When, flags)`,
> both returning `bool` (+ async) — not the four-per-arity `SetAddAndSetExpiry` shape planned below.
> `Expiration` already encodes `EX`/`PX`/`EXAT`/`PXAT`/`KEEPTTL`, which is exactly `SADDEX`'s expiration block,
> so the `TimeSpan?`/`DateTime`/`keepTtl` splits collapse; `Expiration.Persist` is rejected
> (`SADDEX` has no `PERSIST`; that is `SetMemberPersist`'s job). The overloads always send `SADDEX` -
> plain `SADD` is not an equivalent fallback, because the bare `SADDEX` form discards an existing member's
> TTL where `SADD` leaves existing members untouched (R.2). Everything below the surface - the message
> shapes, requirement ids, tests and open questions - is unchanged; read `SetAddAndSetExpiry` as
> `SetAdd(..., Expiration, ...)` (internally `SetAddWithExpiryMessage`).

No shared client HLD exists for this feature. The server facts below come from redis/redis#15938
("Set member expiration", merged to `unstable` as `a76a3689a`, shipped in 8.12-rc2, which reports
`redis_version:8.11.241`), from the command JSONs at that commit
(`src/commands/saddex.json`, `sexpire.json` and family), and from direct observation against an
8.12-rc2 build (the `setexpiry` demo in the demos repo). Requirement ids `R.x` are assigned here.

## 1. Summary

Callers get per-member expiration on sets — the hash-field-TTL idea (7.4's `HEXPIRE`) brought to
sets — as a mirror of the existing `HashField*` family:

- `SetAddAndSetExpiry` (`SADDEX`): add members and/or (re)set their expiration in one atomic call,
  with `MNX`/`MXX` batch conditions and `KEEPTTL`;
- `SetMemberExpire` (`SEXPIRE`/`SPEXPIRE`/`SEXPIREAT`/`SPEXPIREAT`, picked by precision) returning
  `ExpireResult[]`;
- `SetMemberGetTimeToLive` (`SPTTL`) and `SetMemberGetExpireDateTime` (`SPEXPIRETIME`) returning
  `long[]`;
- `SetMemberPersist` (`SPERSIST`) returning `PersistResult[]`;
- `KeyNotificationType.SExpired` so the 8.12 `sexpired` notification (including on the new subkey
  channels, where the payload names the lapsed member) parses as a known type rather than
  `Unknown`. **No new payload parsing is needed**: `KeyNotification.GetSubKeys()` on `main`
  already parses every subkey channel kind, including the length-prefixed member list.

Decision class **C** (new core commands). Analogue traced end to end: the hash-field expiry
family — `IDatabase.HashFieldExpire` (both overloads), `HashFieldGetTimeToLive`,
`HashFieldGetExpireDateTime`, `HashFieldPersist` and their message plumbing
(`RedisDatabase.HashFieldExpireExecute`, `GetHashFieldExpireMessage`,
`PickExpireCommandByPrecision`/`PickExpireAtCommandByPrecision`, `HashFieldExecute`), the
processors (`ResultProcessor.ExpireResultArray`, `PersistResultArray`, `Int64Array`), the
isolation forwards in `KeyPrefixedDatabase`/`KeyPrefixed`, and for `SADDEX` the `HSETEX` binding
(`HashFieldSetAndSetExpiry` + `HashFieldSetAndSetExpiryMessage`, whose `FNX`/`FXX` have exactly
the same all-or-nothing batch semantics as `MNX`/`MXX`). Version gating follows
`RedisFeatures.HashImport`; experimental gating follows the `SER011`/`Experiments.Server_8_12`
gate that #3261 (BLESS, also 8.12) introduces.

`estimated_size: medium`: eighteen interface members (nine sync + nine async), one shared
execute/message-builder group, eighteen isolation forwards, eight enum members plus their
`IsPrimaryOnly` cases, three literals, a version constant and property, the experimental gate,
public-API lines, one notification enum member, one round-trip test class, one live test class,
notification test rows, and coverage-ledger edits. No new result processors, no new `APITypes/`.

## 2. Requirement coverage

| Id | Server fact | Where (file, symbol) | Proving test | Note |
|---|---|---|---|---|
| R.1 | `SADDEX key [MNX\|MXX] [EX s\|PX ms\|EXAT ts\|PXAT ts\|KEEPTTL] MEMBERS n member...` (arity -5, `WRITE DENYOOM FAST`); reply 0 = nothing changed / condition not met, 1 = at least one member added, updated or deleted | `IDatabase.SetAddAndSetExpiry` (4 overloads), `RedisDatabase.SetAddAndSetExpiryMessage` | `SetMemberExpiryRoundTrip.SetAddAndSetExpiry_*`; live `SetMemberExpiryTests.SetAddAndSetExpiry*` | Returns `bool` (see open question 3) |
| R.2 | `SADDEX` without an expiry token **discards** an existing member's TTL; `KEEPTTL` keeps it (t_set.c: "without KEEPTTL, its expiration is discarded") | `Expiration.CreateOrKeepTtl(expiry, keepTtl)`, as `HashFieldSetAndSetExpiry` | `SetAddAndSetExpiry_NoExpiry_SendsBareForm`, `KeepTtl_SendsToken`; live `SetAddAndSetExpiryKeepTtl` | XML docs call the discard out |
| R.3 | `MNX` = only if **none** of the members exist; `MXX` = only if **all** exist; all-or-nothing across the batch (one offender vetoes the whole call, reply 0) | `When.NotExists` → `MNX`, `When.Exists` → `MXX` in `SetAddAndSetExpiryMessage` | `SetAddAndSetExpiry_Mnx_SendsToken` / `_Mxx_SendsToken`; live `SetAddAndSetExpiryWhen` | Mirrors `HSETEX` `FNX`/`FXX` exactly (see open question 2) |
| R.4 | `SEXPIRE`/`SPEXPIRE`/`SEXPIREAT`/`SPEXPIREAT key t [NX\|XX\|GT\|LT] MEMBERS n member...` (arity -6, `WRITE FAST`); per-member reply codes, in order: -2 no such member, 0 condition not met, 1 set, 2 deleted (expiry already past) | `IDatabase.SetMemberExpire` (TimeSpan + DateTime), `GetSetMemberExpireMessage`, `PickSetMemberExpireCommandByPrecision`/`PickSetMemberExpireAtCommandByPrecision` | `SetMemberExpire_*` round-trips; live `SetMemberExpire`, `SetMemberExpireConditions`, `SetMemberExpireNoKey` | Reply codes match `ExpireResult` (2/1/0/-2) member for member |
| R.5 | Precision: seconds-granular input uses `SEXPIRE`/`SEXPIREAT`, else `SPEXPIRE`/`SPEXPIREAT` | the pick functions, as `PickExpireCommandByPrecision` | `SetMemberExpire_SecondsPrecision` vs `_MillisecondsPrecision` round-trips | Same `milliseconds % 1000` rule as hash |
| R.6 | `GT` loses to a member with **no** expiry (no expiry counts as infinite → reply 0); `NX`/`XX`/`GT`/`LT` conditions are per member | `ExpireWhen` passed through, `when.ToLiteral()` | live `SetMemberExpireConditions` (`GT` vs persisted member, observed `[0]`) | Same semantics as key-level `EXPIRE` and `HEXPIRE` |
| R.7 | `SPTTL key MEMBERS n member...` (`READONLY FAST`): per-member TTL ms; -1 no expiry, -2 no such member | `IDatabase.SetMemberGetTimeToLive` → `SetMemberExecute(RedisCommand.SPTTL, ...)`, `ResultProcessor.Int64Array` | `SetMemberGetTimeToLive_SendsMembersBlock`; live `SetMemberGetTimeToLive` | Replica-eligible in `IsPrimaryOnly` |
| R.8 | `SPEXPIRETIME key MEMBERS n member...` (`READONLY FAST`): per-member absolute expiry, Unix ms; -1/-2 as above | `IDatabase.SetMemberGetExpireDateTime` → `SetMemberExecute(RedisCommand.SPEXPIRETIME, ...)` | round-trip + live `SetMemberGetExpireDateTime` | Replica-eligible |
| R.9 | `SPERSIST key MEMBERS n member...` (`WRITE FAST`): per member 1 cleared, -1 had none, -2 no such member | `IDatabase.SetMemberPersist` → `SetMemberExecute(RedisCommand.SPERSIST, ...)`, `ResultProcessor.PersistResultArray` | round-trip + live `SetMemberPersist` | Codes match `PersistResult` (1/-1/-2) |
| R.10 | Seconds-precision read twins `STTL`/`SEXPIRETIME` exist server-side | n/a | n/a | Not exposed, mirroring hash (no `HTTL` in `RedisCommand`; the client reads ms and converts). Coverage ledger rows become `not-applicable` |
| R.11 | New notification event `sexpired` fires (classic and subkey channels) when a member lapses; subkey payload names key + members | `KeyNotificationType.SExpired` (`[AsciiHash("sexpired")]`) | `KeyNotificationUnitTests` rows (parse + `GetSubKeys`) | Payload parsing already complete on `main` (`KeyNotification.SubKeys.cs`); only the enum entry is missing |
| R.12 | Expiring members replicate as explicit deletes; commands behave as writes for routing | `IsPrimaryOnly`: `SADDEX`, `SEXPIRE`, `SEXPIREAT`, `SPEXPIRE`, `SPEXPIREAT`, `SPERSIST` primary-only; `SPTTL`, `SPEXPIRETIME` replica-eligible | n/a (routing switch is exhaustive; build proves it) | From the `WRITE`/`READONLY` flags |
| NF.1 | 8.12 is a release candidate; the surface may change | `[Experimental(Experiments.Server_8_12)]` on every new public member | build (`SER011` NoWarn in root `Directory.Build.props`) | Same gate as #3261 (open question 1) |

## 3. Public API to add

Stable or experimental: **experimental**, `[Experimental(Experiments.Server_8_12, UrlFormat = Experiments.UrlFormat)]` —
8.12 is unreleased and #3261 establishes `SER011` as the 8.12 gate.

### `IDatabase` (sync; async twins on `IDatabaseAsync` with `<inheritdoc/>`, returning `Task<T>`)

```csharp
// SADDEX — grounded in HashFieldSetAndSetExpiry (HSETEX), the same four-overload shape
bool SetAddAndSetExpiry(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);
bool SetAddAndSetExpiry(RedisKey key, RedisValue value, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None);
bool SetAddAndSetExpiry(RedisKey key, RedisValue[] values, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None);
bool SetAddAndSetExpiry(RedisKey key, RedisValue[] values, DateTime expiry, When when = When.Always, CommandFlags flags = CommandFlags.None);

// SEXPIRE family — grounded in HashFieldExpire
ExpireResult[] SetMemberExpire(RedisKey key, RedisValue[] members, TimeSpan expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);
ExpireResult[] SetMemberExpire(RedisKey key, RedisValue[] members, DateTime expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None);

// SPEXPIRETIME / SPTTL / SPERSIST — grounded in HashFieldGetExpireDateTime / HashFieldGetTimeToLive / HashFieldPersist
long[] SetMemberGetExpireDateTime(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);
long[] SetMemberGetTimeToLive(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);
PersistResult[] SetMemberPersist(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);
```

The full XML docs mirror the hash siblings' (`<list type="table">` of reply codes), with the set
specifics added: the `SADDEX` docs state the bare-form TTL discard (R.2) and that `MNX`/`MXX` are
all-or-nothing across the batch (R.3); `SetMemberExpire` documents that a member with no expiry
counts as infinite for `GT` (R.6).

### `KeyNotificationType`

```csharp
[AsciiHash("sexpired")]
SExpired = 1006, // side-effect block, after KeyMiss = 1005
```

### `RedisFeatures`

```csharp
v8_12_0 = new Version(8, 11, 241); // 8.12 RC2 is version 8.11.241

/// <summary>Is set-member expiration (<c>SADDEX</c>, <c>SEXPIRE</c>, ...) available?</summary>
public bool SetMemberExpiry => Version.IsAtLeast(v8_12_0);
```

### Back-compat

Purely additive: new interface members (so every implementor gains them: `RedisDatabase`,
`KeyPrefixedDatabase`, `KeyPrefixed`), one `RedisFeatures` property, eight internal `RedisCommand`
members, one `KeyNotificationType` member (append-only enum; see open question 6 for the value).
Nothing shipped changes; no overload collides with the existing `SetAdd` pair (different method
name, so no RS0026 concern). All public-API lines land in `PublicAPI.Unshipped.txt`, the
experimental ones prefixed `[SER011]` as #3261's are; the `KeyNotificationType.SExpired` and
`RedisFeatures` lines are bare, matching #3261's treatment of its notification values.

## 4. Files to change

| Path | add/edit | What |
|---|---|---|
| `src/StackExchange.Redis/Enums/RedisCommand.cs` | edit | `SADDEX` (after `SADD`), `SEXPIRE`, `SEXPIREAT` (after `SETRANGE`), `SPERSIST`, `SPEXPIRE`, `SPEXPIREAT`, `SPEXPIRETIME` (before `SPOP`), `SPTTL` (after `SPOP`); `IsPrimaryOnly` cases per R.12 |
| `src/StackExchange.Redis/RedisLiterals.cs` | edit | `MEMBERS`, `MNX`, `MXX` (`NX`/`XX`/`GT`/`LT`/`KEEPTTL` tokens come via `ExpireWhen.ToLiteral()` and `Expiration`) |
| `src/StackExchange.Redis/Interfaces/IDatabase.cs` | edit | the nine sync members, `SetAddAndSetExpiry` after `SetAdd`, the `SetMember*` block before `SetMembers` |
| `src/StackExchange.Redis/Interfaces/IDatabaseAsync.cs` | edit | the nine async twins with `<inheritdoc/>` |
| `src/StackExchange.Redis/RedisDatabase.cs` | edit | implementations next to `SetAdd`; `SetAddAndSetExpiryMessage` (single + array, mirroring `HashFieldSetAndSetExpiryMessage`), `SetMemberExpireExecute` + `GetSetMemberExpireMessage` + the two pick functions (mirroring the hash versions), `SetMemberExecute` (mirroring `HashFieldExecute`, `MEMBERS` token) |
| `src/StackExchange.Redis/KeyspaceIsolation/KeyPrefixedDatabase.cs` | edit | nine sync forwards with `ToInner(key)` |
| `src/StackExchange.Redis/KeyspaceIsolation/KeyPrefixed.cs` | edit | nine async forwards with `ToInner(key)` |
| `src/StackExchange.Redis/KeyNotificationType.cs` | edit | `SExpired = 1006` with `[AsciiHash("sexpired")]` (metadata `TryParse`/`TryFormat` are source-generated from the attribute) |
| `src/StackExchange.Redis/RedisFeatures.cs` | edit | `v8_12_0` constant and `SetMemberExpiry` property |
| `src/RESPite/Shared/Experiments.cs` | edit | `Server_8_12 = "SER011"`, identical text to #3261 so the merge is clean |
| `Directory.Build.props` | edit | `SER011` in `<NoWarn>`, identical to #3261 |
| `docs/exp/SER011.md` | add | same page #3261 adds, with the set-member-expiry bullet; on merge the page lists both features |
| `src/StackExchange.Redis/PublicAPI/PublicAPI.Unshipped.txt` | edit | the eighteen interface members (`[SER011]`-prefixed), `RedisFeatures.SetMemberExpiry.get`, `KeyNotificationType.SExpired` |
| `docs/CommandCoverage.md` | edit | delete the `SADDEX`/`SEXPIRE`/`SEXPIREAT`/`SPERSIST`/`SPEXPIRE`/`SPEXPIREAT`/`SPEXPIRETIME`/`SPTTL` rows; `SEXPIRETIME` → `not-applicable` (R.10) |
| `tests/StackExchange.Redis.Tests/RoundTripUnitTests/SetMemberExpiryRoundTrip.cs` | add | exact-bytes tests via the internal message builders (section 6) |
| `tests/StackExchange.Redis.Tests/SetMemberExpiryTests.cs` | add | live tests gated `Create(require: RedisFeatures.v8_12_0)`, mirroring `HashFieldTests.cs` |
| `tests/StackExchange.Redis.Tests/KeyNotificationUnitTests.cs` | edit | `sexpired` parse row next to the `hexpired` one; a `__subkeyevent@*__:sexpired` `GetSubKeys` case |
| `tests/RedisConfigs/.docker/Redis/Dockerfile` | edit | `CLIENT_LIBS_TEST_IMAGE=redislabs/client-libs-test:8.12-rc2`, identical to #3261/#3315 |
| `tests/RedisConfigs/docker-compose.yml` | edit | the same image as the `CLIENT_LIBS_TEST_IMAGE` fallback, identical to #3261/#3315 |

Nothing outside this table is touched. No new `ResultProcessor`, no `APITypes/`, no analyzer row
(step 5), no `docs/index.md` page (section 7).

## 5. Ordered implementation steps

1. **Enum, routing, literals.** The eight `RedisCommand` members and their `IsPrimaryOnly` cases;
   `RedisLiterals.MEMBERS`/`MNX`/`MXX`. Check: build `src/StackExchange.Redis`. Done when it
   builds and both `IsPrimaryOnly` branches list the right commands.
2. **Version gate and experiment.** `RedisFeatures.v8_12_0` + `SetMemberExpiry`;
   `Experiments.Server_8_12`; `NoWarn`; `docs/exp/SER011.md` — all with #3261's exact text where
   shared. Done when `Experiments.Server_8_12` resolves.
3. **Message builders.** `SetAddAndSetExpiryMessage` (single-member fast path via `Message.Create`
   with up to six trailing values; array path building one `RedisValue[]`, token order
   `[MNX|MXX] {expiry} MEMBERS n member...`), `GetSetMemberExpireMessage` (token order
   `{expiry} [NX|XX|GT|LT] MEMBERS n member...`, retry category via
   `flags.WithCategory(when.AsRetryCategory())` as the hash version), `SetMemberExecute`.
   Done when all builders compile; they are `internal` for the round-trip tests.
4. **Interfaces, implementations, isolation, public API.** The eighteen members everywhere;
   `PublicAPI.Unshipped.txt` lines. Check: `dotnet build Build.csproj -c Release /p:CI=true`.
   Done when RS0016 is silent.
5. **Transaction analyzer (Step 9): no.** Per-member TTL on a set did not exist before 8.12, so no
   `MULTI`/`WATCH` shape could have composed it — `SADD` + `KeyExpire` expires the *key*, which
   `SADDEX` does not replace. No `TransactionAnalyzer` row; this reasoning recorded here.
6. **Notification type.** `KeyNotificationType.SExpired` + the `KeyNotificationUnitTests` rows.
   Done when the parse row and the subkey-payload case pass.
7. **Round-trip tests** (section 6). Check: the filtered `dotnet test`. Done when every byte
   string matches.
8. **Live tests, written not run** (section 6) + the Dockerfile bump so they can run against the
   8.12-rc2 topology. Done when the class builds and skips with the version message on older
   servers.
9. **Ledger.** The `docs/CommandCoverage.md` edits.

## 6. Test plan

### Result processors (`ResultProcessorUnitTests`)

All three processors are reused and already covered: `ResultProcessor.ExpireResultArray` and
`PersistResultArray` in `BasicAggregates`, `Int64Array` likewise (RESP2 `*`, streaming, RESP3,
empty, null). No new processor, no new processor tests.

### Exact bytes (`RoundTripUnitTests/SetMemberExpiryRoundTrip.cs`)

Via `new RedisDatabase(null!, 0, null)` + the internal builders, as `BitPositionRoundTrip` does.

| Test | Call | Expected request |
|---|---|---|
| `SetAddAndSetExpiry_Ex` | `(k, "m", 15s)` | `*7 SADDEX k EX 15 MEMBERS 1 m` |
| `SetAddAndSetExpiry_Px` | `(k, "m", 1500ms)` | `*7 SADDEX k PX 1500 MEMBERS 1 m` |
| `SetAddAndSetExpiry_KeepTtl` | `(k, "m", null, keepTtl: true)` | `*6 SADDEX k KEEPTTL MEMBERS 1 m` |
| `SetAddAndSetExpiry_Bare` | `(k, "m", null)` | `*5 SADDEX k MEMBERS 1 m` |
| `SetAddAndSetExpiry_Mnx` | `(k, values, 15s, when: NotExists)` | `*9 SADDEX k MNX EX 15 MEMBERS 2 a b` |
| `SetAddAndSetExpiry_Mxx` | `(k, values, 15s, when: Exists)` | `... MXX ...` |
| `SetAddAndSetExpiry_ExAt` | `(k, "m", DateTime utc, whole seconds)` | `*7 SADDEX k EXAT <unix-s> MEMBERS 1 m` |
| `SetMemberExpire_Seconds` | `(k, [a,b], 90s)` | `*8 SEXPIRE k 90 MEMBERS 2 a b` |
| `SetMemberExpire_Millis` | `(k, [a], 1500ms)` | `*7 SPEXPIRE k 1500 MEMBERS 1 a` |
| `SetMemberExpire_Gt` | `(k, [a], 90s, GreaterThanCurrentExpiry)` | `*8 SEXPIRE k 90 GT MEMBERS 1 a` |
| `SetMemberExpire_At` | `(k, [a], DateTime, whole s / fractional)` | `SEXPIREAT` / `SPEXPIREAT` |
| `SetMemberGetTimeToLive` | `(k, [a,b])` | `*6 SPTTL k MEMBERS 2 a b`, reply `*2 :9500 :-1` → `[9500, -1]` |
| `SetMemberGetExpireDateTime` | `(k, [a])` | `*5 SPEXPIRETIME k MEMBERS 1 a` |
| `SetMemberPersist` | `(k, [a,b])` | `*6 SPERSIST k MEMBERS 2 a b`, reply `*2 :1 :-2` → `[Success, NoSuchField]` |

(Abbreviated here; the test file asserts full RESP with length prefixes.)

### Live tests (`SetMemberExpiryTests.cs`, `Create(require: RedisFeatures.v8_12_0)`)

Mirroring `HashFieldTests.cs` scenario for scenario; **run and passing** (34 = 17 × RESP2/RESP3)
against the rebuilt test topology (`redislabs/client-libs-test:8.12-rc2`, reporting
`redis_version:8.11.241`); they skip with the standard version message on older servers. Covered:
add + TTL read-back (relative and absolute, with the `HashFieldTests` clock-skew
tolerance for the `DateTime` overloads); `NoSuchField` on a missing key; conditions including
`GT` losing to a persisted member (`[ExpireResult.ConditionNotMet]`); expiry-in-the-past →
`ExpireResult.Due` and the member gone; `SADDEX` bare-form TTL discard vs `KEEPTTL`; `MNX` veto
(one existing member → `false`, nothing added); `SPERSIST` → `[PersistResult.Success]` then
`[PersistResult.NoExpiryToRemove]`; `SetAddAndSetExpiry` + `SetMemberGetTimeToLive` through
`WithKeyPrefix` (isolation).

Commands:

```
dotnet test --project tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter-class "*SetMemberExpiry*"
dotnet test --project tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter-class "*KeyNotificationUnitTests*"
```

No analyzer change, so `StackExchange.Redis.Build.Tests` is not run.

## 7. Docs, changelog, public-API files

- `docs/<Feature>.md`: **no** — the hash-field family has no page either; the XML docs carry the
  reply-code tables. A combined "member/field expiration" page could come later.
- `docs/exp/SER011.md`: **yes** (add; merges with #3261's copy).
- `PublicAPI.Unshipped.txt`: as section 3. `PublicAPI.Shipped.txt` untouched.
- `docs/CommandCoverage.md`: rows deleted / `SEXPIRETIME` re-marked.
- `docs/ReleaseNotes.md` is frozen at 3.0; not edited.

## 8. Behaviour against older servers

No client-side version check on the call path, matching every sibling. Below 8.12 the server
replies `ERR unknown command 'SADDEX'` (etc.), surfacing as `RedisServerException`.
`RedisFeatures.SetMemberExpiry` lets callers branch beforehand. Live tests skip through
`Create(require: RedisFeatures.v8_12_0)`.

## 9. Risks and open questions

1. **Ordering with #3261/#3315.** All three 8.12 branches add `Experiments.Server_8_12`,
   `RedisFeatures.v8_12_0`, the `NoWarn` entry, `docs/exp/SER011.md` and the Dockerfile bump.
   Default: identical text everywhere so whichever lands first wins and the rest merge clean
   (the SER011.md bullet lists are the only real merge, and it is additive).
2. **`SADDEX` condition enum.** Reuses `When` (`NotExists` → `MNX`, `Exists` → `MXX`), exactly as
   `HashFieldSetAndSetExpiry` maps `When` to `FNX`/`FXX` — which have the same all-or-nothing
   batch semantics. Alternative considered: a dedicated `SetAddWhen` enum spelling out
   "all new"/"all existing". Default: reuse `When` for sibling symmetry; the XML docs carry the
   batch semantics.
3. **`SADDEX` return type.** The reply schema is strictly 0/1 ("at least one member was added,
   updated or deleted"), so `bool` via `ResultProcessor.Boolean`. The `HSETEX` sibling returns
   `RedisValue`, but `SetAdd(key, value)`'s `bool` is the closer precedent for a 0/1 set write.
4. **`ExpireResult`/`PersistResult` naming.** Their `NoSuchField` member (-2) now also means "no
   such set member". Default: reuse the enums unchanged — the wire codes match exactly, and a
   parallel enum pair would fragment the surface; the XML docs on the set members say "member".
5. **`RedisFeatures.v8_12_0` value.** `new Version(8, 11, 241)` — 8.12-rc2 reports
   `redis_version:8.11.241` (observed; and #3261's current revision uses the same value).
6. **`KeyNotificationType.SExpired` value.** `hexpired` sits in the command block (`HExpired = 6`),
   but #3261 takes 66/67 (`Bless`/`Unbless`) and `sexpired` is a side-effect event like `expired`,
   so default: `SExpired = 1006` in the side-effect block, collision-free whichever PR lands
   first. The enum is append-only; the value is forever either way.

## 10. Out of scope

- Any v4 `RespContext` surface (the demos repo's `setexpiry` mini-library shows that shape; this
  plan is the classic `IDatabase` surface only).
- `STTL`/`SEXPIRETIME` seconds-precision bindings (R.10).
- `BLESS` (#3261) and `BITRANGEINTARRAY` (#3315), beyond sharing the SER011/v8_12_0 plumbing.
- A `TransactionAnalyzer` row (step 5: nothing to map).
- `PublicAPI.Shipped.txt` promotion, release files, `.github/`.
