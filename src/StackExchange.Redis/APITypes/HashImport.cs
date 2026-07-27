using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    // active/active deployments. The id doubles as the opaque, connection-local field-set name on the wire.
    private static long _counter;

    private readonly ReadOnlyMemory<RedisValue> _fields;
    private readonly byte[] _name; // 8 raw bytes of _id, written verbatim as the field-set name (binary-safe)
    private readonly long _id;

    private readonly object _sync = new();
    // servers this field-set has been prepared against, weakly held so an idle multiplexer can still be collected;
    // used only to target DISCARD on disposal. Guarded by _sync.
    private List<(WeakReference<ServerEndPoint> Server, int Db)>? _servers;
    private volatile bool _disposed;

    private HashImport(ReadOnlyMemory<RedisValue> fields)
    {
        if (fields.IsEmpty) throw new ArgumentException("At least one field name must be supplied.", nameof(fields));

        // The server rejects a PREPARE with duplicate field names, but because PREPARE is injected fire-and-forget that
        // would surface only indirectly as every SET failing with "no such fieldset". Reject it up front for a clear
        // error at the point of the mistake. (Field names are validated against the field list, not the array's
        // identity, so a subsequently-mutated caller array is out of scope - same caveat as any ReadOnlyMemory input.)
        var span = fields.Span;
        var seen = new HashSet<RedisValue>();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].IsNull) throw new ArgumentException("Field names must not be null.", nameof(fields));
            if (!seen.Add(span[i])) throw new ArgumentException($"Duplicate field name: '{span[i]}'.", nameof(fields));
        }

        _fields = fields;
        _id = Interlocked.Increment(ref _counter);
        _name = new byte[8];
        long id = _id;
        for (int i = 0; i < 8; i++) _name[i] = (byte)(id >> (i * 8));
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
    internal ReadOnlySpan<byte> Name => _name;

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

    // records (once per server) that this field-set is now prepared somewhere on the given server, so disposal can
    // target a DISCARD there. Bookkeeping only - it is called from inside the bridge write lock (during PREPARE
    // injection), so it must never itself issue I/O. If the token is already being disposed we simply skip: a field-set
    // prepared by a SET racing against Dispose may be stranded, but it dies with the connection anyway (best-effort),
    // and using a token concurrently with disposing it is a caller error.
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
            _servers.Add((new WeakReference<ServerEndPoint>(server), db));
        }
    }

    private List<(WeakReference<ServerEndPoint> Server, int Db)>? TakeServers()
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
        foreach (var (weak, db) in servers)
        {
            if (weak.TryGetTarget(out var server)) _ = SafeDiscardAsync(server, db);
        }
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        var servers = TakeServers();
        if (servers is null) return;
        foreach (var (weak, db) in servers)
        {
            if (weak.TryGetTarget(out var server)) await SafeDiscardAsync(server, db).ForAwait();
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
        writer.WriteBulkString(_fieldSet.Name);
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
        writer.WriteBulkString(_fieldSet.Name);
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
        writer.WriteBulkString(_fieldSet.Name);
    }

    public override int ArgCount => 2;
}
