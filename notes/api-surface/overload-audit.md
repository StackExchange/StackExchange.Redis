# v4 group-surface overload audit

An audit of the new (context/group) API surface for the overload-minimalism goal: one method with
richer optional operands (`Expiration`, `ValueCondition`) rather than a stack of overloads
accumulated per nuance, as the legacy surface has. The legacy `IDatabase` surface is out of scope:
its overloads mirror v3 deliberately, for migration.

Method: every `public static` extension in `Groups/*.Methods.cs` (247 methods, 17 groups),
clustered by name within a group, each cluster classified.

## The surface is already mostly minimal

Nearly every overload pair is one of three deliberate shapes, none of which is the cumulative
pattern:

- **Single vs many** (`AddAsync(key, value)` / `AddAsync(key, span)`): different reply types
  (`bool` vs `long`, value vs lease), so they cannot be one method. ~30 clusters, all fine.
- **`long` vs `double`** (`IncrementAsync`): typed returns; folding would box or lose inference.
- **Different command, different reply** (`DeleteAsync` → `XDEL` vs `XDELEX`;
  `IncrementAsync` → `INCRBY` vs `INCREX`, whose reply carries the applied delta): the reply shape
  forces the split, and each is documented at the method.

`Strings.SetAsync(key, value, Expiration = default, ValueCondition = default)` is the exemplar
already in place; `VectorSets` already uses request objects (`VectorSetAddRequest`) where the
clause space is large. `Streams` expresses its large option spaces as optional parameters and
`StreamAddOptions`, not overloads.

## Findings

### A. SortedSets: the range family is still the v3 per-command split (the one real cluster)

Eleven methods say "a range of a sorted set", split by axis exactly as the pre-6.2 commands were:

| axis | read | read+scores | remove | length |
|---|---|---|---|---|
| rank | `RangeByRankAsync` | `RangeByRankWithScoresAsync` | `RemoveRangeByRankAsync` | - |
| score | `RangeByScoreAsync` | `RangeByScoreWithScoresAsync` | `RemoveRangeByScoreAsync` | `LengthAsync(min,max,exclude)` |
| value | `RangeByValueAsync` | - | `RemoveRangeByValueAsync` | `LengthByValueAsync` |

plus `RangeAndStoreAsync(... SortedSetOrder sortedSetOrder ...)`, which already smuggles the axis
in as an enum. The implementations still compose `ZRANGEBYSCORE`/`ZREVRANGEBYLEX`/etc. per cell.

**Proposal**: a `SortedSetRange` operand type in the `Expiration`/`ValueCondition` mould - a
readonly struct with factories `SortedSetRange.ByRank(start, stop)`,
`.ByScore(min, max, Exclude = None)`, `.ByValue(min, max, Exclude = None)` - and five methods:

```
RangeAsync(key, SortedSetRange range, Order order = Ascending, long skip = 0, long take = -1)
RangeWithScoresAsync(key, SortedSetRange range, Order order = Ascending, long skip = 0, long take = -1)
RangeAndStoreAsync(source, destination, SortedSetRange range, Order order = ..., long skip = 0, long? take = null)
RemoveRangeAsync(key, SortedSetRange range)
RangeLengthAsync(key, SortedSetRange range)
```

Order/skip/take stay on the methods, not the operand, because the remove/length commands have no
spelling for them (and a by-rank length is `(stop-start+1)` clamped, which the operand can refuse
or the method can compute). The operand picks the wire command (modern `ZRANGE`/`ZRANGESTORE`
where the axis needs it, the per-axis command where it doesn't or the server is old).

Eleven methods become five, and the axis enum on `RangeAndStoreAsync` disappears. All alpha, so
the old names can just go. This is the one change in the audit I'd call clearly worth making.

**Correction found while implementing**: the by-value `WithScores` cell is not "arbitrarily
lacking" - the server has no `WITHSCORES` for `ZRANGEBYLEX` (every member of a lex-ordered set
shares one score by definition), so `RangeWithScoresAsync` *rejects* a by-value range rather than
filling the cell. Likewise there is no server command counting a rank range, so
`RangeLengthAsync` rejects by-rank (the answer is arithmetic over `LengthAsync`). The by-value
*store* cell is real, though, and the operand surface did gain it - including open (`-`/`+`)
lexical bounds and descending bound-swapping, which the `SortedSetOrder`-shaped legacy method
left to the caller to spell.

### B. Keys.RestoreAsync takes `TimeSpan?` where `Expiration` fits better

`RestoreAsync(key, value, TimeSpan? expiry = null)` - but `RESTORE` has `ABSTTL`, so an
`Expiration` (which already spells relative, absolute, and none, and `KEEPTTL` is simply invalid
here) is the natural operand: `RestoreAsync(key, value, Expiration expiry = default)`. Small,
mechanical, and exactly the pattern the rest of the surface uses. The `TimeSpan.MaxValue`
means-none special case goes away too.

### C. Geospatial: four methods carrying the same four-knob tail

`SearchAsync` ×2 and `SearchAndStoreAsync` ×2 differ only in how the centre is spelled (member vs
longitude/latitude), and each repeats `int count = -1, bool demandClosest = true, Order? order =
null, ...`. Two options, independent of each other:

1. A `GeoCenter` struct with implicit conversions from `RedisValue` and `(double, double)` folds
   each pair into one method (4 → 2).
2. A small `GeoSearchOptions` record for the shared tail (count/demandClosest/order + the
   radius-options/store-distances leg) if the knob count grows again.

(1) is cheap and real; (2) is only worth it if the server grows more clauses - four optional
parameters is still readable. Suggest doing (1), skipping (2) for now.

## Outcome

All three findings implemented (branch `marc/v4-overload-minimalism`):

- **A**: `SortedSetRange` (`ByRank`/`ByScore`/`ByValue` factories; `default` = whole set by rank)
  with `RangeAsync`, `RangeWithScoresAsync`, `RangeAndStoreAsync`, `RemoveRangeAsync`,
  `RangeLengthAsync`; `LengthAsync` slimmed to bare `ZCARD` (`ZCOUNT`/`ZLEXCOUNT` live under
  `RangeLengthAsync`). The per-axis methods were demoted to internal, still carrying the wire
  spelling and the legacy `IDatabase` adapter. A by-rank exclusivity is now unrepresentable
  rather than rejected at runtime; by-rank skip/take and the no-server-command cells throw.
- **B**: `RestoreAsync(key, value, Expiration expiry = default)` - relative stays positional
  milliseconds, absolute adds `ABSTTL`, and `KEEPTTL`/`PERSIST`/ENX are rejected; the
  `TimeSpan.MaxValue`-means-none wart survives only in the internal legacy sibling.
- **C(1)**: `GeoCenter` (implicit from `RedisValue`/`string`/`GeoPosition`; *not* from a tuple -
  `SanityChecks.ValueTupleNotReferenced` pins that this library never references
  `System.ValueTuple`, which net461 only gets from an extra package) folds `SearchAsync` and
  `SearchAndStoreAsync` 4 → 2; a `default` center is rejected. C(2) (`GeoSearchOptions`)
  deliberately skipped, as proposed.

Incidental fix en route: `RangeByScoreCore` and `CombineCore` accepted a `CancellationToken` from
their public callers and silently dropped it before `SendAsync`; both now forward it. The same
drop exists in geo's `SearchCore` (`default` passed where the token should go) - left alone here,
worth a sweep of its own.

## Considered and deliberately left alone

- **`Hashes.SetAsync` vs `SetWithExpiryAsync`** (HSET vs HSETEX): folding to one method with
  `Expiration expiry = default` would make the expiry argument silently switch the wire command -
  different server gate (8.0), different condition semantics (HSETEX's FNX/FXX are per-call over
  all fields, HSET's `When` is not), different reply. The name is the honest signal; keep.
  Same reasoning for `GetSetExpiryAsync` (HGETEX) beside `GetAsync`.
- **`...LeaseAsync` variants** (`GetLeaseAsync`, `GetLeaseDeleteAsync`, ...): a return-form axis,
  documented in Groups.md; not an overload stack.
- **Pop families ×3** (`LeftPopAsync` etc.): single → value, count → lease, multi-key → `LMPOP`
  result type. Three shapes, three replies.
- **`Lists.MoveAsync` ×2**: `LMOVE` (one value) vs the counted form (lease); reply split again.
- **`Strings.IncrementAsync` ×4**: `{long,double}` × `{INCRBY,INCREX}`; both axes forced.
- **Hash field-TTL family is span-only** (`ExpireAsync(key, fields, ...)`, no single-field
  convenience): the minimal choice already; don't add singles.
- **Context withers**: public surface is `WithDatabase`/`WithCachePolicy`/`WithServices` +
  `AppendKeyPrefix` - already policy-shaped (the cache withers were folded into `CachePolicy` in
  #3350/#3354).

## Not examined

`IServer`'s membership (large, node-scoped, mostly single methods per command - no clusters
surfaced in the scan) and the legacy `IDatabase`/`IDatabaseAsync` mirror (out of scope by design).
