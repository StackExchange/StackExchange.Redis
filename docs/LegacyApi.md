The original `IDatabase` API
===

Every command on `IDatabase` used to be spelled `db.<Type><Verb>` - `StringGet`, `HashSetAsync`, `SortedSetRangeByScore` - in one flat interface of over 600 members. From 4.0, commands are grouped by the data type they belong to, and the group is where you find them:

```csharp
// before                                   // from 4.0
db.StringSet("mykey", "abc");               await db.Strings.SetAsync("mykey", "abc");
string s = db.StringGet("mykey");           RedisValue s = await db.Strings.GetAsync("mykey");
db.HashSet(key, "field", "value");          await db.Hashes.SetAsync(key, "field", "value");
db.KeyDelete(key);                          await db.Keys.UnlinkAsync(key);
```

**The old API is not going away.** It is not obsolete, it is not deprecated, and nothing in 4.0 stops working. If you have a large codebase against `IDatabase`, you do not need to do anything. This page is for deciding what to write *next*.

What actually changed
---

**The prefix became a group.** `db.StringGet` is `db.Strings.GetAsync`; `db.SortedSetRangeByScore` is `db.SortedSets.RangeByScoreAsync`. The prefix was always naming a data type - it just had nowhere to live, because one interface had to hold every command. Now the receiver says which family you are in, so IntelliSense lists twenty members instead of four hundred, and the name stops repeating what the receiver already said.

**It is async-only.** There is no `db.Strings.Get`. The synchronous half of the old API exists because it always has, and it stays for the code that uses it, but blocking on a multiplexed connection is the single most common cause of the timeouts this library gets reported - see [Sync over async](SyncOverAsync). A new surface that offered a sync twin would be inviting the problem back in.

**Replies can be borrowed instead of allocated.** Where the old API returns `RedisValue[]` or `HashEntry[]` - a fresh array per call, plus one per element for nested shapes - the new one can return a `ReadOnlyLease<T>`, which is a window over the buffer the reply arrived in. You dispose it and the buffer goes back to the pool. Measured on a thousand-entry `XRANGE`: about **2,100x less memory** for roughly 5% more time. Where you want the array, the array is still there.

**Cancellation reaches the call.** Every method takes a `CancellationToken`.

**Your own commands are extension methods.** The reason for all of the above: adding a command no longer means adding a member to `IDatabase`, which is a binary break for anyone implementing it. A group is a receiver, and anything - including your library - can extend it. See [Extending the client](Extending).

The mapping
---

Mechanical, with three rules: drop the type prefix, use it as the group, add `Async`.

| old | new |
|---|---|
| `db.StringGet(key)` | `await db.Strings.GetAsync(key)` |
| `db.StringSet(key, value)` | `await db.Strings.SetAsync(key, value)` |
| `db.StringIncrement(key)` | `await db.Strings.IncrementAsync(key)` |
| `db.StringAppend(key, value)` | `await db.Strings.AppendAsync(key, value)` |
| `db.KeyDelete(key)` | `await db.Keys.UnlinkAsync(key)` - see below |
| `db.KeyExists(key)` | `await db.Keys.ExistsAsync(key)` |
| `db.KeyExpire(key, ttl)` | `await db.Keys.ExpireAsync(key, ttl)` |
| `db.HashGet(key, field)` | `await db.Hashes.GetAsync(key, field)` |
| `db.HashGetAll(key)` | `await db.Hashes.GetAllAsync(key)` |
| `db.ListLeftPush(key, value)` | `await db.Lists.LeftPushAsync(key, value)` |
| `db.ListRange(key)` | `await db.Lists.RangeAsync(key)` |
| `db.SetAdd(key, value)` | `await db.Sets.AddAsync(key, value)` |
| `db.SetMembers(key)` | `await db.Sets.MembersAsync(key)` |
| `db.SortedSetAdd(key, member, score)` | `await db.SortedSets.AddAsync(key, member, score)` |
| `db.SortedSetRangeByScore(key)` | `await db.SortedSets.RangeByScoreAsync(key)` |
| `db.GeoAdd(key, lon, lat, member)` | `await db.Geospatial.AddAsync(key, lon, lat, member)` |
| `db.HyperLogLogAdd(key, value)` | `await db.HyperLogLog.AddAsync(key, value)` |
| `db.StringBitCount(key)` | `await db.Bitmaps.CountAsync(key)` |
| `db.Sort(key)` | `await db.Keys.SortAsync(key)` |

A few names were taken as an opportunity rather than transcribed, because the old one described the implementation rather than the command:

| old | new | why |
|---|---|---|
| `db.StringGetLease` | `db.Strings.GetLeaseAsync` | same idea, but the lease is now read-only and shares the reply buffer rather than copying it |
| `db.SetCombine`, `db.SetCombineAndStore` | `db.Sets.CombineAsync`, `db.Sets.CombineAndStoreAsync` | unchanged, listed because the group makes them findable |
| `db.StringSetAndGet` | `db.Strings.SetAndGetAsync` | |
| four `HashFieldExpire` overloads | one `db.Hashes.ExpireAsync(key, fields, expiry)` | relative-vs-absolute and seconds-vs-milliseconds are decided by `Expiration`, not by picking a method |
| `db.KeyExpire(key, TimeSpan?)` + `db.KeyExpire(key, DateTime?)` | `db.Keys.ExpireAsync(key, Expiration)` | as above |
| `db.KeyDelete` | `db.Keys.UnlinkAsync` or `db.Keys.DeleteAsync` | `KeyDelete` quietly sent `UNLINK` where the server had it; the group makes you choose, and `DeleteAsync` is a blocking `DEL` |

What is not on the new surface
---

Almost everything has a group home now; the [full table](#every-member) below is generated from the code, so it is the list to trust. What remains on `IDatabase` alone is the short list that is not one command:

- **Locks**: `LockTake`/`LockRelease`/`LockExtend`/`LockQuery` are helpers built from several commands, not commands themselves, and stay on `IDatabase`.
- **`StringGetWithExpiry`**: two commands under one name. Send `db.Strings.GetAsync` and `db.Keys.TimeToLiveAsync` in a batch if you want the pair.
- **`Execute`/`ExecuteResp`**: the replacement is not a group but the interpolated `db.Context.SendAsync<T>($"...")`, which is cheaper and type-safe about keys. See [Extending the client](Extending).

A few groups return a different shape from the member they replace, so check the result type when you move a call: `db.Scripts.EvaluateAsync` returns a `RespResult` rather than a `RedisResult` (see [Scripting](Scripting)), and collection results often have a `ReadOnlyLease<T>` overload as well as the array one.

Mixing is fine and costs nothing: `db.Strings.GetAsync(key)` and `db.LockTake(key, ...)` are the same connection, the same pipeline, the same `IDatabase`.

Transactions and batches
---

`CreateTransaction()` and `CreateBatch()` are unchanged, and both carry the groups:

```csharp
var tran = db.CreateTransaction();
tran.AddCondition(Condition.StringEqual(key, "old"));
var set = tran.Strings.SetAsync(key, "new");   // queued, as before
if (await tran.ExecuteAsync()) { /* ... */ }
```

See [Transactions](Transactions).

Should you migrate?
---

For existing code: only if you are touching it anyway. The old API is supported, it is not slower for what it does, and a mechanical rename across a large codebase buys you very little on its own.

For new code: yes, and for two reasons that are not style. The allocation profile is better on anything that returns a collection, and the surface is extensible - so the day you need a command this client does not have, [you can add it](Extending) instead of waiting for us.

Every member
---

<!-- api-map:begin -->

Generated from the v4 `IDatabase` implementation by `eng/docs/api-map.py`; each old name covers its `Async` twin and every overload.

**Strings**

| `IDatabase` | grouped |
|---|---|
| `StringAppend` | `db.Strings.AppendAsync` |
| `StringDelete` | `db.Strings.DeleteAsync` |
| `StringDigest` | `db.Strings.DigestAsync` |
| `StringGet` | `db.Strings.GetAsync` |
| `StringGetDelete` | `db.Strings.GetDeleteAsync` |
| `StringGetLease` | `db.Strings.GetLeaseAsync` |
| `StringGetRange` | `db.Strings.GetRangeAsync` |
| `StringGetSetExpiry` | `db.Strings.GetSetExpiryAsync` |
| `StringIncrement` | `db.Strings.IncrementAsync` |
| `StringLength` | `db.Strings.LengthAsync` |
| `StringLongestCommonSubsequence` | `db.Strings.LongestCommonSubsequenceAsync` |
| `StringLongestCommonSubsequenceLength` | `db.Strings.LongestCommonSubsequenceLengthAsync` |
| `StringLongestCommonSubsequenceWithMatches` | `db.Strings.LongestCommonSubsequenceWithMatchesAsync` |
| `StringSet` | `db.Strings.SetAsync` |
| `StringSetAndGet` | `db.Strings.SetAndGetAsync` |
| `StringSetRange` | `db.Strings.SetRangeAsync` |

**Hashes**

| `IDatabase` | grouped |
|---|---|
| `HashDelete` | `db.Hashes.DeleteAsync` |
| `HashExists` | `db.Hashes.ExistsAsync` |
| `HashFieldExpire` | `db.Hashes.ExpireAsync` |
| `HashFieldGetAndDelete` | `db.Hashes.GetDeleteAsync` |
| `HashFieldGetAndSetExpiry` | `db.Hashes.GetSetExpiryAsync` |
| `HashFieldGetExpireDateTime` | `db.Hashes.GetExpireDateTimeAsync` |
| `HashFieldGetLeaseAndDelete` | `db.Hashes.GetLeaseDeleteAsync` |
| `HashFieldGetLeaseAndSetExpiry` | `db.Hashes.GetLeaseSetExpiryAsync` |
| `HashFieldGetTimeToLive` | `db.Hashes.GetTimeToLiveAsync` |
| `HashFieldPersist` | `db.Hashes.PersistAsync` |
| `HashFieldSetAndSetExpiry` | `db.Hashes.SetWithExpiryAsync` |
| `HashGet` | `db.Hashes.GetAsync` |
| `HashGetAll` | `db.Hashes.GetAllAsync` |
| `HashGetLease` | `db.Hashes.GetLeaseAsync` |
| `HashImport` | `db.Hashes.ImportAsync` |
| `HashIncrement` | `db.Hashes.IncrementAsync` |
| `HashKeys` | `db.Hashes.KeysAsync` |
| `HashLength` | `db.Hashes.LengthAsync` |
| `HashRandomField` | `db.Hashes.RandomFieldAsync` |
| `HashRandomFields` | `db.Hashes.RandomFieldsAsync` |
| `HashRandomFieldsWithValues` | `db.Hashes.RandomFieldsWithValuesAsync` |
| `HashScan` | `db.Hashes.ScanAsync` |
| `HashScanNoValues` | `db.Hashes.ScanNoValuesAsync` |
| `HashSet` | `db.Hashes.SetAsync` |
| `HashStringLength` | `db.Hashes.StringLengthAsync` |
| `HashValues` | `db.Hashes.ValuesAsync` |

**Lists**

| `IDatabase` | grouped |
|---|---|
| `ListGetByIndex` | `db.Lists.GetByIndexAsync` |
| `ListInsertAfter` | `db.Lists.InsertAfterAsync` |
| `ListInsertBefore` | `db.Lists.InsertBeforeAsync` |
| `ListLeftPop` | `db.Lists.LeftPopAsync` |
| `ListLeftPush` | `db.Lists.LeftPushAsync` |
| `ListLength` | `db.Lists.LengthAsync` |
| `ListMove` | `db.Lists.MoveAsync` |
| `ListPosition` | `db.Lists.PositionAsync` |
| `ListPositions` | `db.Lists.PositionsAsync` |
| `ListRange` | `db.Lists.RangeAsync` |
| `ListRemove` | `db.Lists.RemoveAsync` |
| `ListRightPop` | `db.Lists.RightPopAsync` |
| `ListRightPush` | `db.Lists.RightPushAsync` |
| `ListSetByIndex` | `db.Lists.SetByIndexAsync` |
| `ListTrim` | `db.Lists.TrimAsync` |

**Sets**

| `IDatabase` | grouped |
|---|---|
| `SetAdd` | `db.Sets.AddAsync` |
| `SetCombine` | `db.Sets.CombineAsync` |
| `SetCombineAndStore` | `db.Sets.CombineAndStoreAsync` |
| `SetCombineLength` | `db.Sets.CombineLengthAsync` |
| `SetContains` | `db.Sets.ContainsAsync` |
| `SetIntersectionLength` | `db.Sets.CombineLengthAsync` |
| `SetLength` | `db.Sets.LengthAsync` |
| `SetMembers` | `db.Sets.MembersAsync` |
| `SetMove` | `db.Sets.MoveAsync` |
| `SetPop` | `db.Sets.PopAsync` |
| `SetRandomMember` | `db.Sets.RandomMemberAsync` |
| `SetRandomMembers` | `db.Sets.RandomMembersAsync` |
| `SetRemove` | `db.Sets.RemoveAsync` |
| `SetScan` | `db.Sets.ScanAsync` |

**SortedSets**

| `IDatabase` | grouped |
|---|---|
| `SortedSetAdd` | `db.SortedSets.AddAsync` |
| `SortedSetCombine` | `db.SortedSets.CombineAsync` |
| `SortedSetCombineAndStore` | `db.SortedSets.CombineAndStoreAsync` |
| `SortedSetCombineWithScores` | `db.SortedSets.CombineWithScoresAsync` |
| `SortedSetIncrement` | `db.SortedSets.IncrementAsync` |
| `SortedSetIntersectionLength` | `db.SortedSets.CombineLengthAsync` |
| `SortedSetLength` | `db.SortedSets.LengthAsync` |
| `SortedSetLengthByValue` | `db.SortedSets.LengthByValueAsync` |
| `SortedSetPop` | `db.SortedSets.PopAsync` |
| `SortedSetRandomMember` | `db.SortedSets.RandomMemberAsync` |
| `SortedSetRandomMembers` | `db.SortedSets.RandomMembersAsync` |
| `SortedSetRandomMembersWithScores` | `db.SortedSets.RandomMembersWithScoresAsync` |
| `SortedSetRangeAndStore` | `db.SortedSets.RangeAndStoreAsync` |
| `SortedSetRangeByRank` | `db.SortedSets.RangeByRankAsync` |
| `SortedSetRangeByRankWithScores` | `db.SortedSets.RangeByRankWithScoresAsync` |
| `SortedSetRangeByScore` | `db.SortedSets.RangeByScoreAsync` |
| `SortedSetRangeByScoreWithScores` | `db.SortedSets.RangeByScoreWithScoresAsync` |
| `SortedSetRangeByValue` | `db.SortedSets.RangeByValueAsync` |
| `SortedSetRank` | `db.SortedSets.RankAsync` |
| `SortedSetRemove` | `db.SortedSets.RemoveAsync` |
| `SortedSetRemoveRangeByRank` | `db.SortedSets.RemoveRangeByRankAsync` |
| `SortedSetRemoveRangeByScore` | `db.SortedSets.RemoveRangeByScoreAsync` |
| `SortedSetRemoveRangeByValue` | `db.SortedSets.RemoveRangeByValueAsync` |
| `SortedSetScan` | `db.SortedSets.ScanAsync` |
| `SortedSetScore` | `db.SortedSets.ScoreAsync` |
| `SortedSetScores` | `db.SortedSets.ScoresAsync` |

**Keys**

| `IDatabase` | grouped |
|---|---|
| `DebugObject` | `db.Keys.DebugObjectAsync` |
| `KeyCopy` | `db.Keys.CopyAsync` |
| `KeyDump` | `db.Keys.DumpAsync` |
| `KeyEncoding` | `db.Keys.EncodingAsync` |
| `KeyExists` | `db.Keys.ExistsAsync` |
| `KeyExpireTime` | `db.Keys.ExpireTimeAsync` |
| `KeyFrequency` | `db.Keys.FrequencyAsync` |
| `KeyIdleTime` | `db.Keys.IdleTimeAsync` |
| `KeyMigrate` | `db.Keys.MigrateAsync` |
| `KeyMove` | `db.Keys.MoveAsync` |
| `KeyPersist` | `db.Keys.PersistAsync` |
| `KeyRandom` | `db.Keys.RandomAsync` |
| `KeyRefCount` | `db.Keys.RefCountAsync` |
| `KeyRename` | `db.Keys.RenameAsync` |
| `KeyRestore` | `db.Keys.RestoreAsync` |
| `KeyTimeToLive` | `db.Keys.TimeToLiveAsync` |
| `KeyTouch` | `db.Keys.TouchAsync` |
| `KeyType` | `db.Keys.TypeAsync` |
| `Sort` | `db.Keys.SortAsync` |
| `SortAndStore` | `db.Keys.SortAndStoreAsync` |

**Streams**

| `IDatabase` | grouped |
|---|---|
| `StreamAcknowledge` | `db.Streams.AcknowledgeAsync` |
| `StreamAcknowledgeAndDelete` | `db.Streams.AcknowledgeAndDeleteAsync` |
| `StreamAdd` | `db.Streams.AddAsync` |
| `StreamAutoClaim` | `db.Streams.AutoClaimAsync` |
| `StreamAutoClaimIdsOnly` | `db.Streams.AutoClaimIdsOnlyAsync` |
| `StreamClaim` | `db.Streams.ClaimAsync` |
| `StreamClaimIdsOnly` | `db.Streams.ClaimIdsOnlyAsync` |
| `StreamConfigure` | `db.Streams.ConfigureAsync` |
| `StreamConsumerGroupSetPosition` | `db.Streams.SetConsumerGroupPositionAsync` |
| `StreamConsumerInfo` | `db.Streams.ConsumerInfoAsync` |
| `StreamCreateConsumerGroup` | `db.Streams.CreateConsumerGroupAsync` |
| `StreamDelete` | `db.Streams.DeleteAsync` |
| `StreamDeleteConsumer` | `db.Streams.DeleteConsumerAsync` |
| `StreamDeleteConsumerGroup` | `db.Streams.DeleteConsumerGroupAsync` |
| `StreamGroupInfo` | `db.Streams.GroupInfoAsync` |
| `StreamInfo` | `db.Streams.InfoAsync` |
| `StreamLength` | `db.Streams.LengthAsync` |
| `StreamNegativeAcknowledge` | `db.Streams.NegativeAcknowledgeAsync` |
| `StreamPending` | `db.Streams.PendingAsync` |
| `StreamPendingMessages` | `db.Streams.PendingMessagesAsync` |
| `StreamRange` | `db.Streams.RangeAsync` |
| `StreamRead` | `db.Streams.ReadAsync` |
| `StreamReadGroup` | `db.Streams.ReadGroupAsync` |
| `StreamTrim` | `db.Streams.TrimAsync` |
| `StreamTrimByMinId` | `db.Streams.TrimByMinIdAsync` |

**Geospatial**

| `IDatabase` | grouped |
|---|---|
| `GeoAdd` | `db.Geospatial.AddAsync` |
| `GeoDistance` | `db.Geospatial.DistanceAsync` |
| `GeoHash` | `db.Geospatial.HashAsync` |
| `GeoPosition` | `db.Geospatial.PositionAsync` |
| `GeoRemove` | `db.Geospatial.RemoveAsync` |
| `GeoSearch` | `db.Geospatial.SearchAsync` |
| `GeoSearchAndStore` | `db.Geospatial.SearchAndStoreAsync` |

**HyperLogLog**

| `IDatabase` | grouped |
|---|---|
| `HyperLogLogAdd` | `db.HyperLogLog.AddAsync` |
| `HyperLogLogLength` | `db.HyperLogLog.LengthAsync` |
| `HyperLogLogMerge` | `db.HyperLogLog.MergeAsync` |

**Bitmaps**

| `IDatabase` | grouped |
|---|---|
| `StringBitCount` | `db.Bitmaps.CountAsync` |
| `StringBitField` | `db.Bitmaps.FieldAsync` |
| `StringBitOperation` | `db.Bitmaps.OperationAsync` |
| `StringBitPosition` | `db.Bitmaps.PositionAsync` |
| `StringGetBit` | `db.Bitmaps.GetAsync` |
| `StringSetBit` | `db.Bitmaps.SetAsync` |

**Arrays**

| `IDatabase` | grouped |
|---|---|
| `ArrayCount` | `db.Arrays.CountAsync` |
| `ArrayDelete` | `db.Arrays.DeleteAsync` |
| `ArrayDeleteRange` | `db.Arrays.DeleteRangeAsync` |
| `ArrayGet` | `db.Arrays.GetAsync` |
| `ArrayGetRange` | `db.Arrays.GetRangeAsync` |
| `ArrayGrep` | `db.Arrays.GrepAsync` |
| `ArrayInfo` | `db.Arrays.InfoAsync` |
| `ArrayInsert` | `db.Arrays.InsertAsync` |
| `ArrayLastItems` | `db.Arrays.LastItemsAsync` |
| `ArrayLength` | `db.Arrays.LengthAsync` |
| `ArrayNext` | `db.Arrays.NextAsync` |
| `ArrayOperation` | `db.Arrays.OperationAsync` |
| `ArrayRing` | `db.Arrays.RingAsync` |
| `ArrayScan` | `db.Arrays.ScanAsync` |
| `ArraySeek` | `db.Arrays.SeekAsync` |
| `ArraySet` | `db.Arrays.SetAsync` |

**VectorSets**

| `IDatabase` | grouped |
|---|---|
| `VectorSetAdd` | `db.VectorSets.AddAsync` |
| `VectorSetContains` | `db.VectorSets.ContainsAsync` |
| `VectorSetDimension` | `db.VectorSets.DimensionAsync` |
| `VectorSetGetApproximateVector` | `db.VectorSets.GetApproximateVectorAsync` |
| `VectorSetGetAttributesJson` | `db.VectorSets.GetAttributesJsonAsync` |
| `VectorSetGetLinks` | `db.VectorSets.GetLinksAsync` |
| `VectorSetGetLinksWithScores` | `db.VectorSets.GetLinksWithScoresAsync` |
| `VectorSetInfo` | `db.VectorSets.InfoAsync` |
| `VectorSetLength` | `db.VectorSets.LengthAsync` |
| `VectorSetRandomMember` | `db.VectorSets.RandomMemberAsync` |
| `VectorSetRandomMembers` | `db.VectorSets.RandomMembersAsync` |
| `VectorSetRange` | `db.VectorSets.RangeAsync` |
| `VectorSetRangeEnumerate` | `db.VectorSets.RangeEnumerateAsync` |
| `VectorSetRemove` | `db.VectorSets.RemoveAsync` |
| `VectorSetSetAttributesJson` | `db.VectorSets.SetAttributesJsonAsync` |
| `VectorSetSimilaritySearch` | `db.VectorSets.SimilaritySearchAsync` |

**PubSub**

| `IDatabase` | grouped |
|---|---|
| `Publish` | `db.PubSub.PublishAsync` |

**Not a one-to-one rename**

| `IDatabase` | grouped |
|---|---|
| `CreateBatch` | unchanged; or `db.BeginBatch()` ([SER014](exp/SER014), experimental) |
| `CreateTransaction` | unchanged; or `db.BeginTransaction()` ([SER014](exp/SER014), experimental) |
| `Execute` | `db.Context.SendAsync<T>($"...")` - see [Extending the client](Extending) |
| `ExecuteResp` | `db.Context.SendAsync<T>($"...")` - see [Extending the client](Extending) |
| `GeoRadius` | `db.Geospatial.SearchAsync` (`GEORADIUS` is deprecated by the server in favour of `GEOSEARCH`) |
| `HashDecrement` | `db.Hashes.IncrementAsync`, with the value negated |
| `KeyDelete` | `db.Keys.UnlinkAsync` to keep its behaviour (`KeyDelete` sends `UNLINK` where the server has it); `db.Keys.DeleteAsync` is a plain, blocking `DEL` |
| `KeyExpire` | `db.Keys.ExpireAsync(key, Expiration)`; a null expiry is `db.Keys.PersistAsync` |
| `ListRightPopLeftPush` | `db.Lists.MoveAsync(source, destination, ListSide.Right, ListSide.Left)` |
| `LockExtend` | none: a composite helper, stays on `IDatabase` |
| `LockQuery` | none: a composite helper, stays on `IDatabase` |
| `LockRelease` | none: a composite helper, stays on `IDatabase` |
| `LockTake` | none: a composite helper, stays on `IDatabase` |
| `Ping` | unchanged; or `db.PingMeasureAsync()`, which takes a `CancellationToken` |
| `ScriptEvaluate` | `db.Scripts.EvaluateAsync`, returning a `RespResult` rather than a `RedisResult` |
| `ScriptEvaluateReadOnly` | `db.Scripts.EvaluateReadOnlyAsync`, returning a `RespResult` rather than a `RedisResult` |
| `ScriptEvaluateReadOnlyResp` | `db.Scripts.EvaluateReadOnlyAsync` |
| `ScriptEvaluateResp` | `db.Scripts.EvaluateAsync` |
| `SortedSetDecrement` | `db.SortedSets.IncrementAsync`, with the value negated |
| `SortedSetUpdate` | `db.SortedSets.AddAsync(..., when, change: true)` (`ZADD ... CH`) |
| `StringDecrement` | `db.Strings.IncrementAsync`, with the value negated |
| `StringGetSet` | `db.Strings.SetAndGetAsync` (`GETSET` is deprecated by the server; `SET ... GET` needs 6.2) |
| `StringGetWithExpiry` | none: two commands; `db.Strings.GetAsync` and `db.Keys.TimeToLiveAsync` in a batch |

<!-- api-map:end -->
