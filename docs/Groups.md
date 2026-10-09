Command groups
===

Every Redis command this client models lives in a **group**, named for the part of Redis it works on: `db.Strings`, `db.Hashes`, `db.Lists`, and so on. You find a command by going to its group, and you call it the same way everywhere:

```csharp
IDatabase db = conn.GetDatabase();

await db.Strings.SetAsync("greeting", "hello");
RedisValue value = await db.Strings.GetAsync("greeting");
long length = await db.Lists.RightPushAsync("queue", "job:1");
```

If you have used an earlier version of this library: these were `db.StringSet`, `db.StringGet` and `db.ListRightPush`, all on one flat interface. That spelling still works and is still supported - see [The original `IDatabase` API](LegacyApi) for the full mapping, and for the handful of commands that are not on the groups yet.

What a group is
---

A group is a small `readonly struct` that holds the **context** it came from - the connection, the database number, any key prefix - and nothing else. Getting one costs nothing: `db.Strings` allocates nothing and does no work, so there is no reason to store it; reach for it at the call site.

The commands themselves are extension methods on the group. That is what makes the surface open: a library can add a command to an existing group, or a group of its own, without waiting for this one (see [Extending the client](Extending)).

The groups
---

On a database (`IDatabase`, and anything else that carries a database - see below):

| group | works on | for example |
|---|---|---|
| `Strings` | [strings](https://redis.io/docs/latest/develop/data-types/strings/) | `GetAsync`, `SetAsync`, `IncrementAsync`, `GetLeaseAsync` |
| `Hashes` | [hashes](https://redis.io/docs/latest/develop/data-types/hashes/) | `GetAsync`, `SetAsync`, `GetAllAsync` |
| `Lists` | [lists](https://redis.io/docs/latest/develop/data-types/lists/) | `LeftPushAsync`, `RightPopAsync`, `RangeAsync` |
| `Sets` | [sets](https://redis.io/docs/latest/develop/data-types/sets/) | `AddAsync`, `ContainsAsync`, `MembersAsync` |
| `SortedSets` | [sorted sets](https://redis.io/docs/latest/develop/data-types/sorted-sets/) | `AddAsync`, `RangeByScoreAsync`, `PopAsync` |
| `Streams` | [streams](https://redis.io/docs/latest/develop/data-types/streams/) | `AddAsync`, `ReadAsync`, `ReadGroupAsync` |
| `Keys` | any key | `DeleteAsync`, `ExistsAsync`, `ExpireAsync` |
| `Geospatial` | [geospatial indexes](https://redis.io/docs/latest/develop/data-types/geospatial/) | `AddAsync`, `SearchAsync` |
| `HyperLogLog` | [HyperLogLog](https://redis.io/docs/latest/develop/data-types/probabilistic/hyperloglogs/) | `AddAsync`, `LengthAsync` |
| `Bitmaps` | [bitmaps](https://redis.io/docs/latest/develop/data-types/bitmaps/) | `SetAsync`, `GetAsync`, `CountAsync` |
| `VectorSets` | [vector sets](https://redis.io/docs/latest/develop/data-types/vector-sets/) | `AddAsync`, `SimilaritySearchAsync` |
| `Arrays` | [arrays](Arrays) | |
| `Scripts` | Lua scripts and functions | `EvaluateAsync` - see [Scripting](Scripting) |
| `PubSub` | publishing | `PublishAsync` - subscribing is `ISubscriber`'s; see [Pub/Sub](PubSubOrder) |

The method names are the ones IntelliSense shows you; the examples above are a guide to the shape, not an exhaustive list.

On a server (`IServer`), for commands that belong to one node rather than to a key:

| group | for example |
|---|---|
| `Keyspace` | the node's keys as a whole: `CountAsync`, `FlushAsync`, `FlushAllAsync`, `SwapAsync` |
| `Config` | `SetAsync`, `RewriteAsync`, `ResetStatisticsAsync` |
| `Diagnostics` | what the server says about itself: `InfoAsync`, `SlowLogAsync`, `LatencyLatestAsync`, `MemoryStatsAsync`, `TimeAsync` |
| `Scripts`, `PubSub` | the node-level forms: the script cache (`ExistsAsync`, `FlushAsync`), channel listings (`ChannelsAsync`) |

Where you can use them
---

A group comes off anything that has a context:

- **`IDatabase`, `IBatch` and `ITransaction`** - all three are keyspace targets, so `db.Strings`, `batch.Strings` and `transaction.Strings` all work.
- **A context**, including one you have adjusted. `db.Context` is the database's context, and `db.Context.AppendKeyPrefix("tenant:")` is a context too - its groups prefix every key they send:

  ```csharp
  var tenant = db.Context.AppendKeyPrefix("tenant:42:");
  await tenant.Strings.SetAsync("name", "Contoso");   // writes tenant:42:name
  ```

- **`IServer`** - a server target, for the server groups above.

Method shape
---

Every group method is asynchronous and follows one pattern:

```csharp
ValueTask<TResult> SomethingAsync(/* the command's arguments */,
    CommandFlags flags = CommandFlags.None,
    CancellationToken cancellationToken = default)
```

- **`ValueTask`, not `Task`.** It completes without allocating when the reply is already known (a client-side cache hit, say), and otherwise costs no more than a `Task` would. Await it once; don't store it to await twice. If you need a `Task` (to pass to `Task.WhenAll`, say), `.AsTask()` gives you one. A `ValueTask` has no `AsyncState`; if you rely on `Task.AsyncState`, use the original `IDatabase` methods.
- **`CommandFlags`** work as they always have: `FireAndForget`, replica preference, retry categories.
- **`CancellationToken`** cancels the *wait*: a command not yet written is never sent; one already written still runs on the server, and its reply is discarded.
- **Leases.** Where a reply is a large or binary payload, there is often a `...LeaseAsync` form (`Strings.GetLeaseAsync`, for example) that hands back a pooled buffer instead of a fresh array. Dispose what it returns. See [SER012](exp/SER012).

There is no synchronous *twin* of each method. If you need to block, either use the original `IDatabase` methods, or make the context blocking - every group method then sends and waits on the calling thread, and returns a `ValueTask` that has already completed:

```csharp
var blocking = db.Context.Blocking();   // once; it is a context like any other
RedisValue value = blocking.Strings.GetAsync("greeting").GetAwaiter().GetResult();
```

This is how the synchronous `IDatabase` methods are implemented, and it is the supported way for a library to offer synchronous forms of its own commands. It does not depend on the thread pool to wake the caller. A retrying context blocks too, retries included; a batch or transaction cannot (nothing is sent until it is executed), and sends asynchronously as usual - see [Sync over async](SyncOverAsync) for why you usually don't want to block at all.

Batches and transactions
---

Groups work the same way inside a batch or a transaction: compose the commands through the batch's groups, execute, then await the results:

```csharp
var batch = db.Context.BeginBatch();
var a = batch.Strings.GetAsync("a");
var b = batch.Strings.GetAsync("b");
await batch.ExecuteAsync();          // sent as one contiguous run
RedisValue first = await a, second = await b;
```

Nothing is sent until `ExecuteAsync`, so don't await a command's result before then - it has not been asked for yet. `BeginTransaction()` is the same, inside `MULTI`/`EXEC`. See [Transactions](Transactions) and [SER014](exp/SER014).

Commands that aren't in a group
---

For a command this client does not model - a module, a brand-new server feature - send it through the context directly: `db.Context.SendAsync<T>($"...")`, or build it in pieces with `Compose`. See [Ad-hoc commands](Execute). If you are shipping several such commands, give them a group of their own: [Extending the client](Extending) shows how, in exactly the shape the built-in groups use.
