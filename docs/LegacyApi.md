The original `IDatabase` API
===

Every command on `IDatabase` used to be spelled `db.<Type><Verb>` - `StringGet`, `HashSetAsync`, `SortedSetRangeByScore` - in one flat interface of over 600 members. From 4.0, commands are grouped by the data type they belong to, and the group is where you find them:

```csharp
// before                                   // from 4.0
db.StringSet("mykey", "abc");               await db.Strings.SetAsync("mykey", "abc");
string s = db.StringGet("mykey");           RedisValue s = await db.Strings.GetAsync("mykey");
db.HashSet(key, "field", "value");          await db.Hashes.SetAsync(key, "field", "value");
db.KeyDelete(key);                          await db.Keys.DeleteAsync(key);
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
| `db.KeyDelete(key)` | `await db.Keys.DeleteAsync(key)` |
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

What is not on the new surface yet
---

4.0 does not move everything, and where a group is missing a command the old API is the answer, not a workaround. As things stand:

- **Streams**: the reads and the consumer-group commands - `StreamAdd`, `StreamRead`, `StreamReadGroup`, `StreamPending`, `StreamClaim`, `StreamAutoClaim`, `StreamInfo` and friends. `db.Streams` has the scalar half: length, range, delete, trim, acknowledge, and the group-management commands.
- **`ScriptEvaluate`**: `db.Scripts.EvaluateAsync` exists and returns a `RespResult`; the `RedisResult`-returning `IDatabase` overloads do not have a group equivalent. See [Scripting](Scripting).
- **Locks**: `LockTake`/`LockRelease`/`LockExtend`/`LockQuery`, which wait on transactions.
- **Odds and ends**: `Publish`, `Ping`, `Execute`, `KeyMigrate`, `KeyRestore`, `DebugObject`, `HashImport`, `StringGetWithExpiry`, `ArrayGrep`.

Mixing is fine and costs nothing: `db.Strings.GetAsync(key)` and `db.StreamAdd(key, ...)` are the same connection, the same pipeline, the same `IDatabase`.

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
