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
    public readonly CancellationToken CancellationToken;
    private readonly int _database;

    private readonly int _flags;
    private const int FlagsDisableCaptureContext = 1 << 0;

    /// <inheritdoc/>
    public override string ToString() => _connection?.ToString() ?? "(null)";

    public RespConnection Connection => _connection;
    public int Database => _database;

    public RespCommandMap CommandMap => _connection.Configuration.CommandMap;

    /// <summary>
    /// REPLACES the <see cref="System.Threading.CancellationToken"/> associated with this context.
    /// </summary>
    public RespContext WithCancellationToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RespContext clone = this;
        Unsafe.AsRef(in clone.CancellationToken) = cancellationToken;
        return clone;
    }

    /// <summary>
    /// COMBINES the <see cref="System.Threading.CancellationToken"/> associated with this context
    /// with an additional cancellation. The returned <see cref="Lifetime"/>
    /// represents the lifetime of the combined operation, and should be
    /// disposed when complete.
    /// </summary>
    public Lifetime WithLinkedCancellationToken(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled
            || cancellationToken == CancellationToken)
        {
            // would have no effect
            CancellationToken.ThrowIfCancellationRequested();
            return new(null, in this, CancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!CancellationToken.CanBeCanceled)
        {
            // we don't currently have cancellation; no need for a link
            return new(null, in this, cancellationToken);
        }

        CancellationToken.ThrowIfCancellationRequested();
        var src = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
        return new(src, in this, src.Token);
    }

    public Lifetime WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) Throw();
        CancellationTokenSource src;
        if (CancellationToken.CanBeCanceled)
        {
            CancellationToken.ThrowIfCancellationRequested();
            src = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            src.CancelAfter(timeout);
        }
        else
        {
            src = new CancellationTokenSource(timeout);
        }
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(timeout));

        return new Lifetime(src, in this, src.Token);
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
            Unsafe.AsRef(in Context.CancellationToken) = cancellationToken;
        }

        public void Dispose()
        {
            var src = _source;
            // best effort cleanup, noting that copies may exist
            // (which is also why we can't risk TryReset+pool)
            Unsafe.AsRef(in _source) = null;
            Unsafe.AsRef(in Context.CancellationToken) = AlreadyCanceled;
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
