Where are `KEYS`, `SCAN`, `FLUSHDB` etc?
===

Some very common recurring questions are:

> There doesn't seem to be a `Keys(...)` or `Scan(...)` method? How can I query which keys exist in the database?

or

> There doesn't seem to be a `Flush(...)` method? How can I remove all the keys in the database?

The key word here, oddly enough, is the last one: database. Because StackExchange.Redis aims to target scenarios such as cluster, it is important to know which commands target the *database* (the logical database that could be distributed over multiple nodes), and which commands target the *server*. The following commands all target a single server:

- `KEYS` / `SCAN`
- `FLUSHDB` / `FLUSHALL`
- `RANDOMKEY`
- `CLIENT`
- `CLUSTER`
- `CONFIG` / `INFO` / `TIME`
- `SLAVEOF`
- `SAVE` / `BGSAVE` / `LASTSAVE`
- `SCRIPT` (not to be confused with `EVAL` / `EVALSHA`)
- `SHUTDOWN`
- `SLOWLOG`
- `PUBSUB` (not to be confused with `PUBLISH` / `SUBSCRIBE` / etc)
- some `DEBUG` operations

(I've probably missed at least one) Most of these will seem pretty obvious, but the first 3 rows are not so obvious:

- `KEYS` / `SCAN` only list keys that are on the current server; not the wider logical database
- `FLUSHDB` / `FLUSHALL` only remove keys that are on the current server; not the wider logical database
- `RANDOMKEY` only selects a key that is on the current server; not the wider logical database

Actually, StackExchange.Redis spoofs the `RANDOMKEY` one on the `IDatabase` API by simply selecting a target server at random, but this is not possible for the others.

So how do I use them?
---

Simple: start from a server, not a database.

```C#
// get the target server
var server = conn.GetServer(someServer);

// show all keys in database 0 that include "foo" in their name
foreach(var key in server.Keys(pattern: "*foo*")) {
    Console.WriteLine(key);
}

// completely wipe ALL keys from database 0
server.FlushDatabase();
```

Note that unlike the `IDatabase` API (where the target database has already been selected in the `GetDatabase()` call), these methods take an optional parameter for the database, or it defaults to `0`.

The `Keys(...)` method deserves special mention: it is unusual in that it does not have an `*Async` counterpart. The reason for this is that behind the scenes, the system will determine the most appropriate method to use (`KEYS` vs `SCAN`, based on the server version), and if possible will use the `SCAN` approach to hand you back an `IEnumerable<RedisKey>` that does all the paging internally - so you never need to see the implementation details of the cursor operations. If `SCAN` is not available, it will use `KEYS`, which can cause blockages at the server. Either way, both `SCAN` and `KEYS` will need to sweep the entire keyspace, so should be avoided on production servers - or at least, targeted at slaves.

So I need to remember which server I connected to? That sucks!
---

No, not quite. You can use `conn.GetEndPoints()` to list the endpoints (either all known, or the ones specified in the original configuration - these are not necessarily the same thing), and iterate with `GetServer()` to find the server you want (for example, selecting a slave).
