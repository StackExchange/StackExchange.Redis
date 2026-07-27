Hash Import
===

The `HIMPORT` command (Redis 8.10 and later) is a fast, session-based way to create many hashes that share a common
set of field names — for example, importing a batch of records where every record has the same columns. Rather than
sending the field names again for every hash (as a series of `HSET` calls would), the field names are declared once per
connection and each hash then supplies only its values, positionally matched to those fields.

On the wire this is a *connection-local* container command: `HIMPORT PREPARE` registers a named *field-set* (the
ordered field names) on the current connection, `HIMPORT SET` creates one hash from a row of values against that
field-set, and `HIMPORT DISCARD` releases it. A field-set lives only on the connection that prepared it and disappears
when that connection is reset or closed.

StackExchange.Redis is a multiplexer: it owns the connection lifetime on your behalf, and you do not control *which*
physical connection a given command travels on<sup>&dagger;</sup>. Rather than hide this behind an all-in-one bulk call, the library
exposes something close to the raw shape — a reusable [`HashImport`](xref:StackExchange.Redis.HashImport) field-set plus
a per-row `IDatabase.HashImport` — and takes care of the one hard part for you: the `HIMPORT PREPARE` is **injected
automatically** on whichever connection a given import actually writes to (exactly the way a `SELECT` is injected for
database selection). A transparent reconnect, or a fan-out to another cluster node, simply re-prepares on demand. You
never manage `PREPARE`/`SET`/`DISCARD` ordering or connection pinning yourself.

<sup>&dagger;</sup>: *in theory* multiplexing means a single connection, but with automatic reconnects, cluster slot
migrations, active-active / retries, etc: *in reality* it is more complicated than that — which is exactly why the raw
connection-local commands are not safe to drive directly through a multiplexer.

Usage
---

Create a field-set once (declaring the shared field names, in order), then import each hash by supplying its key and
its values positionally against those fields:

``` c#
IDatabase db = muxer.GetDatabase();

// declare the field names shared by every hash we are importing; reusable and safe to share/keep
await using var fields = HashImport.Create("name", "email", "age");

// import as many hashes as you like - streamed, not materialized up front, so the total is unbounded
await db.HashImportAsync("user:1", fields, new RedisValue[] { "alice", "a@example.com", 30 });
await db.HashImportAsync("user:2", fields, new RedisValue[] { "bob",   "b@example.com", 25 });
await db.HashImportAsync("user:3", fields, new RedisValue[] { "carol", "c@example.com", 42 });
```

After these complete, `user:1`, `user:2` and `user:3` each exist as a hash with the `name`, `email` and `age` fields set
to their respective values. Disposing the field-set (`await using` / `Dispose`) sends a best-effort `HIMPORT DISCARD` to
release the server-side state; it is optional (the state also dies with the connection) but good hygiene for long-lived
connections.

### Reusing a values buffer

To avoid allocating a fresh array per row, you may reuse a single `RedisValue[]` (or a slice of a larger pooled buffer),
refilling it for each hash. If you do, you **must await each import before refilling the buffer for the next
row**<sup>&Dagger;</sup> — the library reads the values when the command is actually written to the socket, which (on the
async path) can be *after* `HashImportAsync` returns, so awaiting the returned task is what guarantees the library has
finished with your data and the buffer is safe to overwrite:

``` c#
await using var fields = HashImport.Create("name", "email", "age");
var row = new RedisValue[3]; // reused for every hash

foreach (var record in records)
{
    row[0] = record.Name;
    row[1] = record.Email;
    row[2] = record.Age;
    await db.HashImportAsync(record.Key, fields, row); // MUST await before the next refill
}
```

Do **not** fire off many `HashImportAsync` calls sharing one buffer without awaiting between them (nor pass the buffer
fire-and-forget) — overwriting it while a prior write is still pending corrupts the data on the wire.

<sup>&Dagger;</sup>: we hope to lift this restriction in a future release, so that a shared buffer can be refilled without
waiting for each round-trip.

Notes
---

- The field-set is created without touching the server; the `HIMPORT PREPARE` is injected lazily on first use per
  connection. A `HashImport` is immutable and may be used concurrently and against multiple databases/multiplexers
  (useful for active-active). The field-set name on the wire is an opaque, process-unique id, so it never collides with
  another field-set even on a shared connection.
- Field names are validated at `Create`: **duplicate** names are rejected (the server rejects them too, but only via the
  injected — fire-and-forget — `PREPARE`, so we fail fast instead), and **null** names are rejected. An **empty** field
  name is allowed (a hash may legitimately have an empty-string field).
- `ReadOnlyMemory<RedisValue>` is used for the values (rather than an array) so you can pass slices of a larger, pooled
  or reused buffer without copying — useful when importing in chunks.
- Each call must supply exactly as many values as the field-set has fields; a mismatch throws (`ArgumentException`)
  before anything is sent.
- Each import **replaces** any existing hash at its key (it does not merge into it) — this is import, not `HSET`.
- Each import is applied **on its own** and may be pipelined freely with unrelated work. A server error (for example a
  key already holding a non-hash value, giving `WRONGTYPE`) is thrown for that call as usual, and does not affect other
  imports; unless the call is fire-and-forget, in which case you have opted out of seeing the result.
- **Cluster-aware**: each key routes to its slot as normal, re-preparing the field-set per node as needed — you do *not*
  need hash tags to keep keys together.
- Supported inside a **batch** (an ordered pipeline). *Not* supported inside a `MULTI`/`EXEC` **transaction**: the
  connection-local `PREPARE` cannot be staged inside the transaction, so it throws `NotSupportedException`.
