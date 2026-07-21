Hash Import
===

The `HIMPORT` command (Redis 8.10 and later) is a fast, session-based way to create many hashes that share a common
set of field names — for example, importing a batch of records where every record has the same columns. Rather than
sending the field names again for every hash (as a series of `HSET` calls would), the field names are declared once per
connection and each hash then supplies only its values, positionally matched to those fields.

On the wire this is a *connection-sticky* container command with several sub-commands: `HIMPORT PREPARE` registers a
named *fieldset* (the ordered field names) on the current connection, `HIMPORT SET` creates one hash from a row of
values against that fieldset, and `HIMPORT DISCARD` releases the fieldset. The fieldset lives only on the connection
that prepared it and disappears when that connection is reset or closed.

Because StackExchange.Redis is a multiplexer that owns the connection lifetime on your behalf — you do not control
*which* physical connection a given command travels on — it does **not** expose the individual `HIMPORT` sub-commands.
Managing a `PREPARE`/`SET`/`DISCARD` sequence yourself would require pinning them all to one connection, which is
exactly the detail the multiplexer abstracts away. Instead, the library exposes a single bulk operation,
`IDatabase.HashImport` (and `HashImportAsync`), that performs the whole import for you: it generates a private fieldset,
issues the `PREPARE`, one `SET` per entry, and the terminating `DISCARD`, all guaranteed to land on a single connection.

Usage
---

You supply the shared field names once, and then one `HashImportEntry` per hash — each carrying the target key and that
hash's values, in the same order as the field names:

``` c#
IDatabase db = muxer.GetDatabase();

// the field names shared by every hash we are importing
ReadOnlyMemory<RedisValue> fields = new RedisValue[] { "name", "email", "age" };

// one entry per hash: the key, plus its values positionally matching the fields above
var entries = new HashImportEntry[]
{
    new("user:1", new RedisValue[] { "alice", "a@example.com", 30 }),
    new("user:2", new RedisValue[] { "bob",   "b@example.com", 25 }),
    new("user:3", new RedisValue[] { "carol", "c@example.com", 42 }),
};

await db.HashImportAsync(fields, entries);
```

After this completes, `user:1`, `user:2` and `user:3` each exist as a hash with the `name`, `email` and `age` fields
set to their respective values.

Notes
---

- `ReadOnlyMemory<T>` is used (rather than arrays) so that you can pass slices of larger buffers without copying — useful
  when importing in chunks from a pooled or reused backing array.
- Every entry must supply exactly as many values as there are field names; a mismatch throws before anything is sent.
- The import is **not atomic**. If a later entry fails on the server (for example, a value/field count mismatch that
  slipped past validation), earlier entries may already have been written. If you need all-or-nothing semantics, this is
  not the right tool.
- A zero-entry import is a no-op, and a single-entry import is issued as a plain `HSET` (for a single row, that is
  cheaper than the full `HIMPORT` handshake).
- Inside a `MULTI`/`EXEC` transaction (`CreateTransaction`), the import is unrolled into individual queued commands
  (`PREPARE`, one `SET` per entry, then `DISCARD`) so the whole import executes atomically as part of the transaction.
  Batches are *not* supported.
- Being connection-sticky, it is not cluster-aware; in a cluster, all supplied keys must map to a single node.
