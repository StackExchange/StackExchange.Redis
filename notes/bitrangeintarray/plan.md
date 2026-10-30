---
feature: bitrangeintarray
client: se-redis
hld: {path: none, sha: none}
tracks: [redis/redis#15986, redis/redis#15331]
target_version: "8.12"
decision_class: C
conventions:
  - .claude/skills/implement-resp-command/SKILL.md#Steps
  - .claude/skills/implement-resp-command/SKILL.md#Tests - the two layers that matter
  - AGENTS.md#Public API tracking (important — easy to trip over)
estimated_size: small
integration_targets: []
unit_targets: [RoundTripUnitTests.BitPositionsRoundTrip, ResultProcessorUnitTests.BasicAggregates]
open_questions: 6
---

# BITRANGEINTARRAY: StackExchange.Redis implementation plan

No shared client HLD exists for this command. The server facts below come from the description of
redis/redis#15986 (merged 2026-10-28) and from `src/commands/bitrangeintarray.json` at
redis/redis@6a5fee67 (`since` 8.12.0, group `bitmap`, arity -4, flags `READONLY`, ACL category
`BITMAP`, one key at position 1, `reply_schema` an array of non-negative integers). Requirement
ids `R.x` are assigned here from the PR's "Command behavior" bullets so they can be tracked.

## 1. Summary

Callers get `IDatabase.StringBitPositions` / `IDatabaseAsync.StringBitPositionsAsync`: the
offsets of the set bits (or, with `bit: false`, the clear bits) inside a byte or bit range of a
string or native bitmap, ascending, optionally capped with `limit`, returned as `long[]`. It is
the enumerating sibling of `StringBitPosition` (`BITPOS`, first match only) and `StringBitCount`
(`BITCOUNT`, count only), and takes the same `key, start, end, indexType` shape as both.

Decision class **C** (new core command), but the smallest possible instance of it: the reply is a
flat integer array, so `ResultProcessor.Int64Array` is reused unchanged, and the argument shape
fits `Message.Create` (key plus at most six values), so no `Message` subclass is needed.

Analogue traced end to end: `StringBitPosition` / `RedisCommand.BITPOS` - the interface pair
(`IDatabase.StringBitPosition`, `IDatabaseAsync.StringBitPositionAsync`), the implementation and
its message builder (`RedisDatabase.StringBitPosition`, `RedisDatabase.GetStringBitPositionMessage`),
the `IsPrimaryOnly` classification, the keyspace-isolation forwards
(`KeyPrefixedDatabase.StringBitPosition`, `KeyPrefixed.StringBitPositionAsync`), the exact-bytes
test (`RoundTripUnitTests/BitPositionRoundTrip.cs`) and the live tests (`BitTests.cs`). The
version gate and experimental gating follow `RedisFeatures.HashImport` and the `SER011` /
`Experiments.Server_8_12` gate introduced by #3261 (BLESS, also 8.12).

`estimated_size: small`: two interface members, one implementation pair, one internal message
builder, two isolation forwards, one enum member plus its `IsPrimaryOnly` case, one literal, a
version constant and property, public-API lines, one round-trip test class, live tests, and one
ledger row deleted. No new types, no new result processor.

## 2. Requirement coverage

| Id | Server fact (from redis/redis#15986) | Where (file, symbol) | Proving test | Note |
|---|---|---|---|---|
| R.1 | Syntax `BITRANGEINTARRAY key start end [BYTE\|BIT] [LIMIT count] [ZEROS]` | `RedisDatabase.GetStringBitPositionsMessage` | `BitPositionsRoundTrip.*` (exact bytes) | Options always emitted in this canonical order |
| R.2 | Read-only; works on string values and native bitmaps | `RedisCommand.IsPrimaryOnly` (replica-eligible branch, next to `BITCOUNT`/`BITPOS`) | `BitTests.StringBitPositions_*` (live, written not run) | `READONLY` flag in the spec |
| R.3 | `start`/`end` required, `BITCOUNT` range semantics: inclusive, `BYTE` default or `BIT`, negative from the end, clamped, reversed range empty | `IDatabase.StringBitPositions` parameters `start = 0, end = -1, indexType = StringIndexType.Byte` | `BitPositionsRoundTrip.Defaults_SendStartAndEnd`, `BitIndexType_SendsToken`; live `StringBitPositions_Ranges` | Client always sends both bounds; no `StringIndex.Unbounded` handling (unlike `BITPOS`, the server has no open-ended form here) |
| R.4 | `ZEROS` returns clear bits, never past the end of the value | `bit: false` -> `RedisLiterals.ZEROS` appended | `BitPositionsRoundTrip.ClearBits_SendsZeros`; live `StringBitPositions_ClearBits` | Documented in the XML remarks, contrasting `StringBitPosition(bit: false)` |
| R.5 | `LIMIT count`: lowest `count` offsets; `LIMIT 0` -> empty array; negative -> server error | `limit` parameter (`long? limit = null`), argument check in `GetStringBitPositionsMessage` | `BitPositionsRoundTrip.Limit_SendsToken`, `Limit_Negative_Throws`; live `StringBitPositions_Limit` | Negative rejected client-side with `ArgumentOutOfRangeException` rather than a round-trip |
| R.6 | Paging: `BIT` -> next `start` = last offset + 1; `BYTE` -> reply completes the byte, next `start` = last offset / 8 + 1 | XML `<remarks>` on `IDatabase.StringBitPositions` | live `StringBitPositions_PagesWithoutGaps` (BYTE and BIT) | No paging helper in this plan; see open question 4 |
| R.7 | Options accepted in any order; repeated option, last wins | n/a | n/a | Server-side leniency; the client emits one canonical order and never repeats |
| R.8 | Missing key or empty value -> empty array; other types -> `WRONGTYPE` | `ResultProcessor.Int64Array` (empty aggregate -> empty array); error propagates as `RedisServerException` | `ResultProcessorUnitTests.BasicAggregates.Int64Array` (`*0\r\n`); live `StringBitPositions_MissingKey` | Existing processor and tests already cover the empty shape |
| R.9 | Reply: ascending array of non-negative integers (`reply_schema`) | `ResultProcessor.Int64Array` | `BasicAggregates.Int64Array` (RESP2 `*`, streaming `*?`, RESP3 attribute prefix) | RESP3 has no distinct shape for an integer array |
| NF.1 | No fixed result cap; only client output-buffer limits bound the reply | `long[]` return; remarks advise `limit` for large bitmaps | n/a | See open question 3 (lease-based variant) |

## 3. Public API to add

Stable or experimental: **experimental**, `[Experimental(Experiments.Server_8_12, UrlFormat = Experiments.UrlFormat)]`,
because 8.12 is unreleased and #3261 (BLESS, same server version) establishes `SER011` as the
8.12 gate. See open question 1 for the ordering with #3261.

### `IDatabase` (sync)

```csharp
/// <summary>
/// Returns the positions of the bits set to 1 (or, when <paramref name="bit"/> is <c>false</c>, set to 0) in a range of a string or bitmap, in ascending order.
/// Positions are counted as in <see cref="StringBitPosition(RedisKey, bool, long, long, StringIndexType, CommandFlags)"/>: the string is an array of bits from left to right, where the most significant bit of the first byte is position 0.
/// </summary>
/// <param name="key">The key of the string or bitmap.</param>
/// <param name="bit">True to return the positions of set bits, false to return the positions of clear bits.</param>
/// <param name="start">The start of the range (defaults to 0).</param>
/// <param name="end">The end of the range, inclusive (defaults to -1, the last byte or bit). Negative values index from the end of the value.</param>
/// <param name="indexType">Whether <paramref name="start"/> and <paramref name="end"/> are byte or bit offsets (defaults to <see cref="StringIndexType.Byte"/>).</param>
/// <param name="limit">The maximum number of positions to return, lowest first; <c>null</c> returns every match in the range. See the remarks for paging.</param>
/// <param name="flags">The flags to use for this operation.</param>
/// <returns>The matching bit positions in ascending order; empty when the key does not exist, the range is empty, or nothing matches.</returns>
/// <remarks>
/// <para><seealso href="https://redis.io/commands/bitrangeintarray"/></para>
/// <para>
/// Unlike <see cref="StringBitPosition(RedisKey, bool, long, long, StringIndexType, CommandFlags)"/> with <paramref name="bit"/> <c>false</c>,
/// the range never extends past the end of the value, so no clear bits are reported beyond it.
/// </para>
/// <para>
/// To page through a large bitmap, pass <paramref name="limit"/> and continue from the last position returned:
/// with <see cref="StringIndexType.Bit"/>, the next <paramref name="start"/> is that position plus one;
/// with <see cref="StringIndexType.Byte"/>, the server completes the byte containing the last position (so a page can hold up to 7 more than <paramref name="limit"/>),
/// and the next <paramref name="start"/> is that position divided by 8, plus one. A page shorter than <paramref name="limit"/> ends the scan.
/// Pages are separate commands and do not share a snapshot.
/// </para>
/// <para>Available from Redis 8.12; older servers reject the command.</para>
/// </remarks>
[Experimental(Experiments.Server_8_12, UrlFormat = Experiments.UrlFormat)]
long[] StringBitPositions(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, long? limit = null, CommandFlags flags = CommandFlags.None);
```

### `IDatabaseAsync`

```csharp
/// <inheritdoc cref="IDatabase.StringBitPositions(RedisKey, bool, long, long, StringIndexType, long?, CommandFlags)"/>
[Experimental(Experiments.Server_8_12, UrlFormat = Experiments.UrlFormat)]
Task<long[]> StringBitPositionsAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, long? limit = null, CommandFlags flags = CommandFlags.None);
```

Grounding: parameter list and defaults copied from `IDatabase.StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)`
with `limit` inserted before `flags` (`flags` stays last, per every sibling). `bit` is required,
as on `StringBitPosition`. Return type `long[]` matches the other `ResultProcessor.Int64Array`
consumers. No new `APITypes/` type.

### `RedisFeatures`

```csharp
/// <summary>Is bit enumeration (<c>BITRANGEINTARRAY</c>) available?</summary>
public bool StringBitPositions => Version.IsAtLeast(v8_12_0);
```

plus the constant `v8_12_0` (see open question 2 for its value and for sharing it with #3261).

### Back-compat

Purely additive: two new interface members (default-free interfaces, so every implementor gains
them: `RedisDatabase`, `KeyPrefixedDatabase`, `KeyPrefixed`), one new `RedisFeatures` property,
one new `RedisCommand` member (internal). Nothing shipped changes. All public-API lines land in
`src/StackExchange.Redis/PublicAPI/PublicAPI.Unshipped.txt`, prefixed `[SER011]` as the BLESS
lines are; nothing in the `net6.0/` folder (no TFM-specific types involved).

## 4. Files to change

| Path | add/edit | What |
|---|---|---|
| `src/StackExchange.Redis/Enums/RedisCommand.cs` | edit | `BITRANGEINTARRAY` member in the `BIT*` group (after `BITPOS`); a `case RedisCommand.BITRANGEINTARRAY:` in the replica-eligible branch of `IsPrimaryOnly`, next to `BITCOUNT`/`BITPOS` |
| `src/StackExchange.Redis/RedisLiterals.cs` | edit | `ZEROS = RedisValue.FromRaw("ZEROS"u8)`; `BIT`, `BYTE`, `LIMIT` already exist |
| `src/StackExchange.Redis/Interfaces/IDatabase.cs` | edit | `StringBitPositions` with the XML doc above, placed after `StringBitPosition` |
| `src/StackExchange.Redis/Interfaces/IDatabaseAsync.cs` | edit | `StringBitPositionsAsync` with `<inheritdoc/>`, after `StringBitPositionAsync` |
| `src/StackExchange.Redis/RedisDatabase.cs` | edit | `StringBitPositions` / `StringBitPositionsAsync` next to `StringBitPosition`; `internal Message GetStringBitPositionsMessage(in RedisKey key, bool bit, long start, long end, StringIndexType indexType, long? limit, CommandFlags flags)` next to `GetStringBitPositionMessage` |
| `src/StackExchange.Redis/KeyspaceIsolation/KeyPrefixedDatabase.cs` | edit | sync forward `Inner.StringBitPositions(ToInner(key), bit, start, end, indexType, limit, flags)` |
| `src/StackExchange.Redis/KeyspaceIsolation/KeyPrefixed.cs` | edit | async forward with `ToInner(key)` |
| `src/StackExchange.Redis/RedisFeatures.cs` | edit | `v8_12_0` constant (if #3261 has not already added it) and `StringBitPositions` property |
| `src/RESPite/Shared/Experiments.cs` | edit (conditional) | `Server_8_12 = "SER011"` only if #3261 has not landed; otherwise nothing |
| `Directory.Build.props` | edit (conditional) | `SER011` in `<NoWarn>` only if #3261 has not landed |
| `docs/exp/SER011.md` | edit (conditional) | add `StringBitPositions` to the list of gated members if the page exists (it is added by #3261); otherwise create it as #3261 does |
| `src/StackExchange.Redis/PublicAPI/PublicAPI.Unshipped.txt` | edit | the two interface members and the `RedisFeatures` property; the build names the exact lines |
| `docs/CommandCoverage.md` | edit | delete the `BITRANGEINTARRAY` row from the todo table (the audit script reports it RESOLVED otherwise) |
| `tests/StackExchange.Redis.Tests/RoundTripUnitTests/BitPositionsRoundTrip.cs` | add | exact-bytes tests via `TestConnection.ExecuteAsync(msg, ResultProcessor.Int64Array, RequestResp, ResponseResp, log: log)` |
| `tests/StackExchange.Redis.Tests/BitTests.cs` | edit | live tests gated `Create(require: RedisFeatures.v8_12_0)` |

Nothing outside this table is touched. In particular no `Message` subclass, no `ResultProcessor`
change, no `APITypes/`, no analyzer, no `docs/index.md` (no new docs page; see section 7).

## 5. Ordered implementation steps

1. **Enum, routing, literal.** `RedisCommand.BITRANGEINTARRAY`; its `IsPrimaryOnly` case in the
   replica-eligible list (the spec flag is `READONLY`); `RedisLiterals.ZEROS`.
   Check: `dotnet build src/StackExchange.Redis/StackExchange.Redis.csproj -c Release -f net10.0`.
   Done when it builds (the exhaustive switch would otherwise throw at runtime, not compile time,
   so re-read the case list).
2. **Version gate and experiment.** `RedisFeatures.v8_12_0` and `RedisFeatures.StringBitPositions`.
   If #3261 is merged, reuse `Experiments.Server_8_12`, the `NoWarn` entry and `docs/exp/SER011.md`
   (add the new members to that page); if not, add them exactly as #3261 does so the later merge
   is textual. Check: build. Done when `Experiments.Server_8_12` resolves.
3. **Message builder.** `RedisDatabase.GetStringBitPositionsMessage`: validate `limit` (`< 0` ->
   `ArgumentOutOfRangeException(nameof(limit))`); then select the `Message.Create(Database, flags,
   RedisCommand.BITRANGEINTARRAY, key, ...)` overload by the number of trailing values:
   `start, end` always; `indexType.ToLiteral()` only when `indexType == StringIndexType.Bit`
   (mirrors `GetStringBitPositionMessage`, which omits the default `BYTE`); `RedisLiterals.LIMIT,
   limit.Value` when `limit` has a value; `RedisLiterals.ZEROS` when `bit` is `false`. Worst case
   is key plus six values, which `Message.Create` supports (`CommandKeyValueValueValueValueValueValueMessage`).
   Check: build. Done when all eight combinations compile without a `RedisValue[]` allocation.
4. **Interfaces and implementations.** `IDatabase.StringBitPositions`,
   `IDatabaseAsync.StringBitPositionsAsync`; `RedisDatabase` pair calling the builder and
   `ExecuteSync(msg, ResultProcessor.Int64Array)` / `ExecuteAsync(...)`; `KeyPrefixedDatabase` and
   `KeyPrefixed` forwards with `ToInner(key)`. Then the `PublicAPI.Unshipped.txt` lines the build
   demands (`[SER011]` prefix). Check: `dotnet build Build.csproj -c Release /p:CI=true`. Done when
   RS0016 is silent and every implementor compiles.
5. **Transaction analyzer (Step 9 of the convention skill): no.** `BITRANGEINTARRAY` is a
   read-only enumeration; it replaces loops of `GETBIT`/`BITPOS`, not a `MULTI`/`WATCH` shape, so
   no `TransactionAnalyzer` row. Nothing to do.
6. **Round-trip tests.** `RoundTripUnitTests/BitPositionsRoundTrip.cs` (section 6). Check:
   `dotnet test --project tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter-class "*BitPositionsRoundTrip*"`.
   Done when every byte string matches and the negative-limit test throws.
7. **Live tests, written not run.** Add to `BitTests.cs` under `Create(require: RedisFeatures.v8_12_0)`
   (section 6). Check: they compile and skip against the docker topology (`Skip` with the version
   message). Done when the class builds and the skip reason names the version.
8. **Ledger.** Delete the `BITRANGEINTARRAY` row from `docs/CommandCoverage.md`. Check: the
   `audit-command-coverage` script exits 0 against a fresh upstream checkout. Done when it does.

## 6. Test plan

### Result processor (`ResultProcessorUnitTests`)

`ResultProcessor.Int64Array` is reused and already covered by `BasicAggregates.Int64Array`:
RESP2 `*3\r\n:1\r\n:2\r\n:3\r\n`, streaming `*?...\r\n.\r\n`, RESP3 doubles, empty `*0\r\n`,
null `*-1\r\n` and `_\r\n`, and an attribute-prefixed array. No new processor tests. If a
reviewer wants the command named in a test, add one `InlineData` row to that theory rather than a
new class.

### Exact bytes (`RoundTripUnitTests/BitPositionsRoundTrip.cs`)

Each test builds the message via `db.GetStringBitPositionsMessage(...)` on
`new RedisDatabase(null!, 0, null)` and asserts the outbound RESP, as `BitPositionRoundTrip` does.
`$16\r\nBITRANGEINTARRAY` is the command bulk.

| Test | Call | Expected request |
|---|---|---|
| `Defaults_SendStartAndEnd` | `("k", true, 0, -1, Byte, null)` | `*4\r\n$16\r\nBITRANGEINTARRAY\r\n$1\r\nk\r\n$1\r\n0\r\n$2\r\n-1\r\n` |
| `BitIndexType_SendsToken` | `("k", true, 5, 10, Bit, null)` | `*5\r\n...$1\r\nk\r\n$1\r\n5\r\n$2\r\n10\r\n$3\r\nBIT\r\n` |
| `Limit_SendsToken` | `("k", true, 0, -1, Byte, 1)` | `*6\r\n...$1\r\n0\r\n$2\r\n-1\r\n$5\r\nLIMIT\r\n$1\r\n1\r\n` |
| `ClearBits_SendsZeros` | `("k", false, 24, 31, Bit, null)` | `*6\r\n...$2\r\n24\r\n$2\r\n31\r\n$3\r\nBIT\r\n$5\r\nZEROS\r\n` |
| `AllOptions_CanonicalOrder` | `("k", false, 2, -1, Bit, 2)` | `*8\r\n...$1\r\n2\r\n$2\r\n-1\r\n$3\r\nBIT\r\n$5\r\nLIMIT\r\n$1\r\n2\r\n$5\r\nZEROS\r\n` |
| `Limit_Zero_IsSent` | `("k", true, 0, -1, Byte, 0)` | `*6\r\n...$5\r\nLIMIT\r\n$1\r\n0\r\n` with reply `*0\r\n` -> empty array |
| `Limit_Negative_Throws` | `("k", true, 0, -1, Byte, -1)` | `Assert.Throws<ArgumentOutOfRangeException>`, `ParamName == "limit"` |

Replies for the positive cases: `*4\r\n:1\r\n:7\r\n:9\r\n:30\r\n` -> `[1, 7, 9, 30]`, and `*0\r\n`
-> empty.

### Live tests (`BitTests.cs`, written not run, `Create(require: RedisFeatures.v8_12_0)`)

Scenarios copied from the PR description and marked `expected`; the implementer observes them
when an 8.12 server is available. Setup for each: `SETBIT k 1 1`, `SETBIT k 7 1`, `SETBIT k 9 1`,
`SETBIT k 30 1`.

| Test | Call | expected |
|---|---|---|
| `StringBitPositions_Ranges` | `(k, true)` / `(k, true, 1, -1)` / `(k, true, 5, 10, Bit)` | `1 7 9 30` / `9 30` / `7 9` |
| `StringBitPositions_ClearBits` | `(k, false, 24, 31, Bit)` / `(k, false, 30, 100, Bit)` | `24 25 26 27 28 29 31` / `31` (clamped) |
| `StringBitPositions_Limit` | `(k, true, 0, -1, Bit, 1)` / `(k, true, 2, -1, Bit, 2)` / `(k, true, 0, -1, Byte, 1)` | `1` / `7 9` / `1 7` (BYTE completes byte 0) |
| `StringBitPositions_PagesWithoutGaps` | loop with `limit` in both units applying the R.6 rule over a 10,000-bit random bitmap | union of pages equals the unpaged result, no repeats |
| `StringBitPositions_MissingKey` | `(missing, true)` | empty |
| `StringBitPositions_WrongType` | on a list key | `RedisServerException` containing `WRONGTYPE` |
| `StringBitPositions_KeyPrefix` | via `db.WithKeyPrefix("p:")` | request shows `p:k` (unit-level: `KeyPrefixedTests` style) |

Commands:

```
dotnet test --project tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter-class "*BitPositionsRoundTrip*"
dotnet test --project tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter-class "StackExchange.Redis.Tests.BitTests"
```

No analyzer change, so `StackExchange.Redis.Build.Tests` is not run.

## 7. Docs, changelog, public-API files

- `docs/<Feature>.md`: **no**. There is no bitmap page today (`StringBitCount`/`BITCOUNT` appear
  only in `docs/ReleaseNotes.md`), and one method does not justify starting one; the XML remarks
  carry the paging rule. If a bitmap page is written later (native bitmaps, `RedisType.Bitmap`,
  this command), it goes in `docs/` with an `index.md` entry.
- `docs/exp/SER011.md`: **yes**, add the two members to the gated list (page created by #3261).
- `PublicAPI.Unshipped.txt`: the two interface members and the `RedisFeatures` property, `[SER011]`
  prefixed. `PublicAPI.Shipped.txt` untouched.
- `docs/CommandCoverage.md`: delete the row.
- `docs/ReleaseNotes.md` is frozen at 3.0; release notes live in GitHub Releases. Do not edit.

## 8. Behaviour against older servers

No client-side version check on the call path, matching every sibling. Below 8.12 the server
replies `ERR unknown command 'BITRANGEINTARRAY'`, which surfaces as `RedisServerException`.
`RedisFeatures.StringBitPositions` lets callers branch beforehand. Live tests skip through
`Create(require: RedisFeatures.v8_12_0)` with the standard version message.

## 9. Risks and open questions

1. **Ordering with #3261 (BLESS).** Both features are 8.12 and both want `SER011` /
   `Experiments.Server_8_12`, `RedisFeatures.v8_12_0`, the `NoWarn` entry and `docs/exp/SER011.md`.
   Default: land #3261 first and reuse; if this lands first, add them with identical text so the
   merge is clean. Never create a second id for the same server version.
2. **`v8_12_0` value.** #3261 uses a placeholder `new Version(8, 12, 0)` and notes the 8.12
   integration image reports `255.255.255`, so pre-release builds may report `8.11.x` and the live
   tests would skip on them. Default: keep the placeholder, same as #3261, and correct both when
   the RC numbering is known. Unverified: what `INFO server` reports on the local unstable build.
3. **Large replies.** `long[]` is simple and matches `Int64Array` consumers, but a dense multi-MB
   bitmap enumerated without `limit` allocates one `long` per set bit. A `Lease<long>` variant
   (the pattern `StringBitField(RedisKey, ReadOnlyMemory<BitFieldOperation>)` uses for
   `Lease<long?>`) would avoid the array but needs a new processor. Default: `long[]` only,
   remarks recommend `limit`; a lease overload is additive later.
4. **Paging helper.** An `IAsyncEnumerable<long>` that applies the R.6 rule would remove the
   byte-versus-bit arithmetic from callers, and the BYTE rule (completing the byte, next start is
   `last / 8 + 1`) is easy to get wrong. Default: not in this plan; document the rule in the remarks
   and add a `StringBitPositionsAsync` enumerable overload later if asked.
5. **Parameter shape.** `bool bit` plus `long? limit` mirrors `StringBitPosition` and keeps one
   overload. Alternative considered: a `[Flags]`/options type. Default: keep the sibling shape; a
   single boolean and one nullable integer do not justify a type.
6. **Replica routing.** Classified replica-eligible from the `READONLY` flag. A native bitmap
   written on the primary is replicated through `BITCONVERT`/`BITROAROP`, so a replica should hold
   the same representation; if an 8.12 replica ever lagged in representation the offsets would
   still be identical (the command is representation-agnostic). Default: replica-eligible.

## 10. Out of scope

- Any other client's API shape; the HLD for this command does not exist yet, and when it does the
  client-neutral proposal should be compared against section 3 here.
- `BITCONVERT`, `BITROAROP` (server-internal), `bitmap-default-roaring` configuration, and any
  typed exposure of `OBJECT ENCODING bitmap-roaring`.
- A `Lease<long>` or `IAsyncEnumerable<long>` overload (open questions 3 and 4).
- `PublicAPI.Shipped.txt` promotion, release files, `.github/`.
- Changes to `StringBitPosition`, `StringBitCount` or `StringIndexType`.
