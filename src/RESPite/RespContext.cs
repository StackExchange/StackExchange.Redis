using System.Runtime.CompilerServices;
using RESPite.Connections.Internal;

namespace RESPite;

/// <summary>
/// Transient state for a RESP operation.
/// </summary>
public readonly struct RespContext
{
    public static ref readonly RespContext Null => ref NullConnection.Default.Context;

    private readonly RespConnection _connection;
    private readonly CancellationToken _cancellationToken;
    private readonly int _database;

    private readonly int _flags;
    private const int FlagsDisableCaptureContext = 1 << 0;

    /// <inheritdoc/>
    public override string ToString() => _connection?.ToString() ?? "(null)";

    public RespConnection Connection => _connection;
    public int Database => _database;
    public CancellationToken CancellationToken => _cancellationToken;

    public RespCommandMap CommandMap => _connection.Configuration.CommandMap;

    /// <summary>
    /// REPLACES the <see cref="CancellationToken"/> associated with this context.
    /// </summary>
    public RespContext WithCancellationToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RespContext clone = this;
        Unsafe.AsRef(in clone._cancellationToken) = cancellationToken;
        return clone;
    }

    /// <summary>
    /// COMBINES the <see cref="CancellationToken"/> associated with this context
    /// with an additional cancellation. The returned <see cref="Lifetime"/>
    /// represents the lifetime of the combined operation, and should be
    /// disposed when complete.
    /// </summary>
    public Lifetime WithLinkedCancellationToken(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled
            || cancellationToken == _cancellationToken)
        {
            // would have no effect
            return new(null, in this, _cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_cancellationToken.CanBeCanceled)
        {
            // we don't currently have cancellation; no need for a link
            return new(null, in this, cancellationToken);
        }

        var src = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationToken);
        return new(src, in this, src.Token);
    }

    public readonly struct Lifetime : IDisposable
    {
        // Unusual public field; a ref-readonly would be preferable, but by-ref props have restrictions on structs.
        // We would rather avoid the copy semantics associated with a regular property getter.
        public readonly RespContext Context;

        private readonly CancellationTokenSource? _source;

        internal Lifetime(CancellationTokenSource? source, in RespContext context, CancellationToken cancellationToken)
        {
            _source = source;
            Context = context; // snapshot, we can now mutate this locally
            Unsafe.AsRef(in Context._cancellationToken) = cancellationToken;
        }

        public void Dispose()
        {
            var src = _source;
            // best effort cleanup, noting that copies may exist
            // (which is also why we can't risk TryReset+pool)
            Unsafe.AsRef(in _source) = null;
            Unsafe.AsRef(in Context._cancellationToken) = AlreadyCanceled;
            src?.Dispose(); // don't cancel on EOL; want consistent behaviour with/without link
        }

        private static readonly CancellationToken AlreadyCanceled = CreateCancelledToken();

        private static CancellationToken CreateCancelledToken()
        {
            CancellationTokenSource cts = new();
            cts.Cancel();
            return cts.Token;
        }
    }

    public RespContext WithDatabase(int database)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._database) = database;
        return clone;
    }

    public RespContext WithConnection(RespConnection connection)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._connection) = connection;
        return clone;
    }

    public RespContext ConfigureAwait(bool continueOnCapturedContext)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._flags) = continueOnCapturedContext
            ? _flags & ~FlagsDisableCaptureContext
            : _flags | FlagsDisableCaptureContext;
        return clone;
    }

    public RespBatch CreateBatch(int sizeHint = 0) => new BasicBatchConnection(in this, sizeHint);
}
