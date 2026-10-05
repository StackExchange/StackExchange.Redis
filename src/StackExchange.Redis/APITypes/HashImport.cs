using System;
using System.Buffers;
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
/// <para>
/// <b>Why this cannot currently move to the interpolated command surface.</b> Every other command moved so far
/// is a pure function from arguments to bytes; <c>HIMPORT SET</c> is not, because it references state that must
/// already exist on the socket it lands on. The nearest sibling is <c>EVALSHA</c>, which has the same shape -
/// an optimistic short reference to a named, cached payload, where the client's belief that the payload is
/// present can be wrong - and the comparison is instructive precisely because of where it breaks:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Scope.</b> The script cache is server-wide, so an <c>EVALSHA</c> recovery can be routed like any other
/// command. A field-set is connection-local, so a recovery must reach one specific socket.
/// </description></item>
/// <item><description>
/// <b>Fallback form.</b> <c>EVAL &lt;script&gt;</c> is a single self-contained command carrying everything the
/// hash referenced, so a <c>NOSCRIPT</c> is recovered by re-rendering one message (see
/// <c>ResultProcessor.RespResult</c>, which keeps the request buffer alive for exactly that). <c>HIMPORT SET</c>
/// has <b>no</b> such form. <c>HSET</c> is not it: this command <i>replaces</i> the hash at the key, where
/// <c>HSET</c> merges into it, so the inline expansion would be <c>DEL</c> plus <c>HSET</c> - two commands,
/// not atomic, and a different failure profile. Recovery is therefore inherently two ordered commands that
/// must share a connection.
/// </description></item>
/// </list>
/// <para>
/// Which is why the <c>PREPARE</c> is injected inside the bridge's write lock rather than anywhere earlier:
/// that is the only point at which the connection is known and nothing has been written yet, and it is exactly
/// the window a two-command, connection-local recovery needs. Acting there is what lets this type avoid pinning
/// a connection at all. A frame-based surface has no such point - it hands an opaque payload to an executor and
/// the connection is chosen afterwards - so expressing this outside the bridge would need <i>connection</i>
/// affinity across a retry, and <c>CommandServerSpecific</c> pins an endpoint, not a connection.
/// </para>
/// <para>
/// Note the comparison with <c>SELECT</c> injection is a red herring, tempting though the shared mechanism is:
/// a database index is a register the client mirrors and is the sole author of, so it can never miss. This and
/// <c>EVALSHA</c> are lookups by name that can.
/// </para>
/// </remarks>
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

    /// <summary>The same opaque name, written through the interpolated builder.</summary>
    /// <remarks>
    /// A second spelling rather than a shared one, as <c>ArrayGrepRequest</c>'s predicates are: the two
    /// writers have no common interface. The bytes are identical by construction - both blit the same
    /// <see cref="long"/> - which is the property that matters, since the name is what ties a
    /// <c>PREPARE</c>, its <c>SET</c>s and its <c>DISCARD</c> together.
    /// </remarks>
    internal void WriteName(scoped ref Protocol.RespRequestBuilder handler)
    {
        Span<byte> name = stackalloc byte[8];
        Unsafe.WriteUnaligned(ref name[0], _id);
        handler.AppendFormatted(name);
    }

    // rejects use of a disposed field-set before anything is sent; a disposed field-set may already have been DISCARDed
    // on the server, so a SET against it would silently mis-behave (and would never be cleaned up).
    internal void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HashImport));
    }

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

    /// <summary>Compose and send <c>HIMPORT DISCARD</c> for this field-set; synchronous up to the send, since the frame is a ref struct.</summary>
    private ValueTask<bool> SendDiscard(RespContext context)
    {
        var cmd = context.Compose(RedisCommand.HIMPORT, 2);
        Protocol.RespRequestFrame frame;
        try
        {
            cmd.AppendFormatted(RedisLiterals.DISCARD);
            WriteName(ref cmd);
            frame = cmd.Complete();
        }
        catch
        {
            cmd.Dispose();
            throw;
        }

        try
        {
            return context.SendAsync(ref frame, CommandFlags.FireAndForget, Protocol.RespHandlers.Success, default);
        }
        finally
        {
            frame.Dispose(); // a no-op once the send has detached it
        }
    }

    private async Task SafeDiscardAsync(ServerEndPoint server, int db)
    {
        // nothing was prepared on a core that was never created, so there is nothing to discard
        if (server.Multiplexer.NewCoreIfCreated is not { } core) return;

        // on the connection the PREPARE went out on, which is where the gate claimed it. The claim goes first, so
        // it never outlives the server's own copy; the DISCARD is fire-and-forget, as it always was.
        core.ReleaseClaim(server.EndPoint, Id);
        try
        {
            await SendDiscard(new RedisServer(server, null).Context.Raw.WithDatabase(db)).ConfigureAwait(false);
        }
        catch
        {
            // best-effort: a field-set the server still holds is reclaimed with its connection
        }
    }
}
