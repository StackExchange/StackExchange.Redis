Transactions in Redis
=====================

Transactions in Redis are not like transactions in, say a SQL database. The [full documentation is here](http://redis.io/topics/transactions),
but to paraphrase:

A transaction in redis consists of a block of commands placed between `MULTI` and `EXEC` (or `DISCARD` for rollback). Once a `MULTI`
has been encountered, the commands on that connection *are not executed* - they are queued (and the caller gets the reply `QUEUED`
to each). When an `EXEC` is encountered, they are
all applied in a single unit (i.e. without other connections getting time between operations). If a `DISCARD` is seen instead of 
a `EXEC`, everything is thrown away. Because the commands inside the transaction are queued, you can't make decisions *inside*
the transaction. For example, in a SQL database you might do the following (pseudo-code - illustrative only):

```C#
// assign a new unique id only if they don't already
// have one, in a transaction to ensure no thread-races
var newId = CreateNewUniqueID(); // optimistic
using(var tran = conn.BeginTran())
{
	var cust = GetCustomer(conn, custId, tran);
	var uniqueId = cust.UniqueID;
	if(uniqueId == null)
	{
		cust.UniqueId = newId;
		SaveCustomer(conn, cust, tran);
	}
	tran.Complete();
}
```

So how do you do it in Redis?
---

This simply isn't possible in redis transactions: once the transaction is open you *can't fetch data* - your
operations are queued. Fortunately, there are two other commands that help us: `WATCH` and `UNWATCH`.

`WATCH {key}` tells Redis that we are interested in the specified key for the purposes of the transaction.
Redis will automatically keep track of this key, and any changes will essentially doom our transaction to
rollback - `EXEC` does the same as `DISCARD` (the caller can detect this and retry from the start). So what
you *can* do is: `WATCH` a key, check data from that key in the normal way, then `MULTI`/`EXEC` your changes.
If, when you check the data, you discover that you don't actually need the transaction, you can use `UNWATCH` to
forget all the watched keys. Note that watched keys are also reset during `EXEC` and `DISCARD`. So *at the Redis layer*, this is conceptually:

```
WATCH {custKey}
HEXISTS {custKey} "UniqueId"
(check the reply, then either:)
MULTI
HSET {custKey} "UniqueId" {newId}
EXEC
(or, if we find there was already an unique-id:)
UNWATCH
```

This might look odd - having a `MULTI`/`EXEC` that only spans a single operation - but the important thing
is that we are now also tracking changes to `{custKey}` from all other connections - if anyone else
changes the key, the transaction will be aborted.

And in StackExchange.Redis?
---

This is *further* complicated by the fact that StackExchange.Redis uses a multiplexer approach. We can't simply
let concurrent callers issue `WATCH` / `UNWATCH` / `MULTI` / `EXEC` / `DISCARD`: it would all be jumbled together. So
an additional abstraction is provided - additionally making things simpler to get right: *constraints*. *Constraints* are
basically pre-canned tests involving `WATCH`, some kind of test, and a check on the result. If all the constraints
pass, the `MULTI`/`EXEC` is issued; otherwise `UNWATCH` is issued. This is all done in a way that prevents the commands being
mixed together with other callers. So our example becomes:

```C#
var newId = CreateNewId();
var tran = db.CreateTransaction();
tran.AddCondition(Condition.HashNotExists(custKey, "UniqueID"));
tran.HashSetAsync(custKey, "UniqueID", newId);
bool committed = tran.Execute();
// ^^^ if true: it was applied; if false: it was rolled back
```

Note that the object returned from `CreateTransaction` only has access to the *async* methods - because the result of
each operation will not be known until after `Execute` (or `ExecuteAsync`) has completed. If the operations are not applied, all the `Task`s
will be marked as cancelled - otherwise, *after* the command has executed you can fetch the results of each as normal.

The set of available *conditions* is not extensive, but covers the most common scenarios; please contact me (or better: submit a pull-request) if
there are additional conditions that you would like to see.

Inbuilt operations via `When`
---

It should also be noted that many common scenarios (in particular: key/hash existence, like in the above) have been anticipated by Redis, and single-operation
atomic commands exist. These are accessed via the `When` parameter - so our previous example can *also* be written as:

```C#
var newId = CreateNewId();
bool wasSet = db.HashSet(custKey, "UniqueID", newId, When.NotExists);
```

(here, the `When.NotExists` causes the `HSETNX` command to be used, rather than `HSET`)

Lua
---

You should also keep in mind that Redis 2.6 and above [support Lua scripting](http://redis.io/commands/EVAL), a versatile tool for performing multiple operations as a single atomic unit at the server.
Since no other connections are serviced during a Lua script it behaves much like a transaction, but without the complexity of `MULTI` / `EXEC` etc.  This also avoids issues such as bandwidth and latency
between the caller and the server, but the trade-off is that it monopolises the server for the duration of the script.

At the Redis layer (and assuming `HSETNX` did not exist) this could be implemented as:

```
EVAL "if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end" 1 {custKey} {newId}
```

This can be used in StackExchange.Redis via:

```C#
var wasSet = (bool) db.ScriptEvaluate(@"if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
        new RedisKey[] { custKey }, new RedisValue[] { newId });
```

(note that the response from `ScriptEvaluate` and `ScriptEvaluateAsync` is variable depending on your exact script; the response can be interpreted by casting - in this case as a `bool`)
