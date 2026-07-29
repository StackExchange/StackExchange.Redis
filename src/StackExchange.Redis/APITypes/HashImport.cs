using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// A reusable, connection-local <em>field-set</em> for streaming hash imports via <see cref="IDatabase.HashImport"/>.
/// </summary>
/// <remarks>
/// <para>
/// The underlying <c>HIMPORT</c> family is <em>session-local</em>: a field-set is <c>PREPARE</c>d against a single
/// physical connection and then referenced by name from each <c>HIMPORT SET</c>. Because the multiplexer hides
/// connections (pooling, transparent reconnects, cluster routing, active/active), this type does <b>not</b> pin a
/// connection. Instead, the <c>PREPARE</c> is injected lazily and automatically on whichever connection a given
/// <see cref="IDatabase.HashImport"/> actually writes to (mirroring how <c>SELECT</c> is injected for database
/// selection); a reconnect or a fan-out to another cluster node simply re-prepares on demand. Each import is applied
/// on its own and may be pipelined freely with unrelated work, so imports are effectively unbounded.
/// </para>
/// <para>
/// Disposing sends a best-effort <c>HIMPORT DISCARD</c> to the connections the field-set was prepared on, releasing the
/// server-side state. Disposal is not required for correctness — the state also dies with the connection — but is good
/// hygiene for long-lived connections.
/// </para>
/// <para>A single <see cref="HashImport"/> is safe to use concurrently and against multiple databases/multiplexers.</para>
/// </remarks>
[Experimental(Experiments.Server_8_10, UrlFormat = Experiments.UrlFormat)]
public sealed class HashImport : IDisposable, IAsyncDisposable
{
    // process-wide monotonic id; deliberately not bound to any multiplexer so a single field-set can span
    // active/active deployments. The id doubles as the opaque, connection-local field-set name on the wire - its 8 raw
    // bytes are written verbatim (binary-safe) whenever a name is needed, so no separate byte[] is stored.
    private static long _counter;

    private readonly ReadOnlyMemory<RedisValue> _fields;
    private readonly long _id;

    private readonly object _sync = new();
    // servers this field-set has been prepared against, weakly held so an idle multiplexer can still be collected;
    // used only to target DISCARD on disposal. Guarded by _sync. (A named struct rather than a ValueTuple: the shipped
    // assembly must not reference System.ValueTuple - it breaks .NET Framework; see SanityChecks.ValueTupleNotReferenced.)
    private List<ServerRef>? _servers;
    private volatile bool _disposed;

    private readonly struct ServerRef(WeakReference<ServerEndPoint> server, int db)
    {
        public WeakReference<ServerEndPoint> Server { get; } = server;
        public int Db { get; } = db;
    }

    private HashImport(ReadOnlyMemory<RedisValue> fields)
    {
        if (fields.IsEmpty) throw new ArgumentException("At least one field name must be supplied.", nameof(fields));

        // Snapshot into storage we own. The field-set is long-lived and its wire encoding must stay stable for the
        // object's lifetime, so we must not alias caller memory that could be mutated after Create - which would also
        // silently bypass the validation below. A field-set is created once and reused for many rows, so this one
        // copy is amortized to nothing (and a handful of RedisValue at that).
        var snapshot = fields.ToArray();

        // The server rejects a PREPARE with duplicate field names, but because PREPARE is injected fire-and-forget that
        // would surface only indirectly as every SET failing with "no such fieldset". Reject it up front for a clear
        // error at the point of the mistake.
        var seen = new HashSet<RedisValue>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].IsNull) throw new ArgumentException("Field names must not be null.", nameof(fields));
            if (!seen.Add(snapshot[i])) throw new ArgumentException($"Duplicate field name: '{snapshot[i]}'.", nameof(fields));
        }

        _fields = snapshot;
        _id = Interlocked.Increment(ref _counter);
    }

    /// <summary>
    /// Creates a field-set describing the ordered field names shared by every hash imported through it.
    /// </summary>
    /// <param name="fields">The field names; import values are supplied positionally in this order.</param>
    public static HashImport Create(params RedisValue[] fields) => new(fields);

    /// <inheritdoc cref="Create(RedisValue[])"/>
    public static HashImport Create(ReadOnlyMemory<RedisValue> fields) => new(fields);

    internal long Id => _id;
    internal ReadOnlyMemory<RedisValue> Fields => _fields;
    internal int FieldCount => _fields.Length;

    // writes the opaque field-set name: the id's 8 raw bytes as a bulk string. Endianness is irrelevant (the server
    // treats the name as an arbitrary byte string, and a token never leaves the process), so an unaligned blit of the
    // id is enough - and identical for this token's every PREPARE/SET/DISCARD, which is all that matters.
    internal void WriteName(in MessageWriter writer)
    {
        Span<byte> name = stackalloc byte[8];
        Unsafe.WriteUnaligned(ref name[0], _id);
        writer.WriteBulkString(name);
    }

    // rejects use of a disposed field-set before anything is sent; a disposed field-set may already have been DISCARDed
    // on the server, so a SET against it would silently mis-behave (and would never be cleaned up).
    internal void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HashImport));
    }

    // creates the HIMPORT PREPARE injected (fire-and-forget) ahead of a SET on a connection that has not yet
    // prepared this field-set; its result is never surfaced - a genuinely broken PREPARE re-appears as the SET
    // failing with a "no such field-set" server error.
    internal Message CreatePrepareMessage(int db) => new HashImportPrepareMessage(db, CommandFlags.FireAndForget, this);

    // Records (once per server) that this field-set is now prepared somewhere on the given server, so disposal can
    // target a DISCARD there.
    //
    // Note the deliberate split of responsibilities, in case a future reader worries that ServerEndPoint (which
    // outlives any one connection and spans reconnects/nodes) is too coarse to track connection-local state:
    //   * Correctness - the "must I inject a PREPARE?" decision - is keyed on the actual PhysicalConnection
    //     (PhysicalConnection._preparedFieldSets). That is the real thing tied to the real session; a reconnect gives a
    //     fresh, empty set and re-prepares automatically. This list is NEVER consulted for that.
    //   * This list is used ONLY for best-effort cleanup on Dispose, at node granularity. DISCARD is idempotent and
    //     carries this field-set's globally-unique id, so it can only ever drop its own state or no-op ("no such
    //     fieldset") - it can never disturb another field-set regardless of which connection it lands on. If the
    //     connection rotated since PREPARE, the old session state is already gone and the DISCARD is a harmless no-op
    //     on the new connection. Node granularity is also exactly right for cluster, where one field-set is prepared
    //     independently on several nodes; deduping by server keeps this list bounded to the nodes touched (rather than
    //     growing per reconnect) and naturally follows each node's current connection.
    //
    // Bookkeeping only - it is called from inside the bridge write lock (during PREPARE injection), so it must never
    // itself issue I/O. If the token is already being disposed we simply skip: a field-set prepared by a SET racing
    // against Dispose may be stranded, but it dies with the connection anyway (best-effort), and using a token
    // concurrently with disposing it is a caller error.
    internal void RegisterServer(ServerEndPoint server, int db)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _servers ??= new();
            for (int i = 0; i < _servers.Count; i++)
            {
                if (_servers[i].Server.TryGetTarget(out var existing) && ReferenceEquals(existing, server)) return;
            }
            _servers.Add(new ServerRef(new WeakReference<ServerEndPoint>(server), db));
        }
    }

    private List<ServerRef>? TakeServers()
    {
        lock (_sync)
        {
            if (_disposed) return null;
            _disposed = true;
            var servers = _servers;
            _servers = null;
            return servers;
        }
    }

    /// <summary>
    /// Releases the connection-local server state for this field-set (best-effort <c>HIMPORT DISCARD</c>).
    /// </summary>
    public void Dispose()
    {
        var servers = TakeServers();
        if (servers is null) return;
        foreach (var entry in servers)
        {
            if (entry.Server.TryGetTarget(out var server)) _ = SafeDiscardAsync(server, entry.Db);
        }
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        var servers = TakeServers();
        if (servers is null) return;
        foreach (var entry in servers)
        {
            if (entry.Server.TryGetTarget(out var server)) await SafeDiscardAsync(server, entry.Db).ForAwait();
        }
    }

    private async Task SafeDiscardAsync(ServerEndPoint server, int db)
    {
        try
        {
            await server.WriteDirectAsync(new HashImportDiscardMessage(db, CommandFlags.FireAndForget, this), ResultProcessor.DemandOK).ForAwait();
        }
        catch
        {
            // best-effort: the field-set dies with the connection regardless, so cleanup failures are benign
        }
    }
}

// HIMPORT SET <key> <field-set> <value...>: the user-facing per-row import. Carries a reference to its field-set so
// the write path can inject a PREPARE the first time this field-set is seen on a connection (see PhysicalBridge).
internal sealed class HashImportSetMessage : Message.CommandKeyBase
{
    private readonly HashImport _fieldSet;
    private readonly ReadOnlyMemory<RedisValue> _values;

    public HashImportSetMessage(int db, CommandFlags flags, HashImport fieldSet, in RedisKey key, ReadOnlyMemory<RedisValue> values)
        : base(db, flags, RedisCommand.HIMPORT, key)
    {
        _fieldSet = fieldSet;
        _values = values;
    }

    internal HashImport FieldSet => _fieldSet;

    protected override void WriteImpl(in MessageWriter writer)
    {
        var values = _values.Span;
        writer.WriteHeader(RedisCommand.HIMPORT, 3 + values.Length);
        writer.WriteBulkString(RedisLiterals.SET);
        writer.Write(Key);
        _fieldSet.WriteName(writer);
        for (int i = 0; i < values.Length; i++) writer.WriteBulkString(values[i]);
    }

    public override int ArgCount => 3 + _values.Length;
}

// HIMPORT PREPARE <field-set> <field...>: injected fire-and-forget ahead of the first SET for a field-set on a
// connection; defines the connection-local name->fields mapping the SET references.
internal sealed class HashImportPrepareMessage : Message
{
    private readonly HashImport _fieldSet;

    public HashImportPrepareMessage(int db, CommandFlags flags, HashImport fieldSet)
        : base(db, flags, RedisCommand.HIMPORT) => _fieldSet = fieldSet;

    protected override void WriteImpl(in MessageWriter writer)
    {
        var fields = _fieldSet.Fields.Span;
        writer.WriteHeader(RedisCommand.HIMPORT, 2 + fields.Length);
        writer.WriteBulkString(RedisLiterals.PREPARE);
        _fieldSet.WriteName(writer);
        for (int i = 0; i < fields.Length; i++) writer.WriteBulkString(fields[i]);
    }

    public override int ArgCount => 2 + _fieldSet.FieldCount;
}

// HIMPORT DISCARD <field-set>: targeted cleanup of a single field-set, issued on disposal. Deliberately not
// DISCARDALL, which would drop sibling field-sets sharing the connection.
internal sealed class HashImportDiscardMessage : Message
{
    private readonly HashImport _fieldSet;

    public HashImportDiscardMessage(int db, CommandFlags flags, HashImport fieldSet)
        : base(db, flags, RedisCommand.HIMPORT) => _fieldSet = fieldSet;

    internal long FieldSetId => _fieldSet.Id;

    protected override void WriteImpl(in MessageWriter writer)
    {
        writer.WriteHeader(RedisCommand.HIMPORT, 2);
        writer.WriteBulkString(RedisLiterals.DISCARD);
        _fieldSet.WriteName(writer);
    }

    public override int ArgCount => 2;
}
