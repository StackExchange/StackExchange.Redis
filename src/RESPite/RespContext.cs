#define MULTI_BATCH // use combining batches, rather than simple batches

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
    private readonly RespContextFlags _flags;

    public RespContextFlags Flags => _flags;

    [Flags]
    public enum RespContextFlags
    {
        /// <summary>
        /// No additional flags; this is the default. Operations will prefer primary nodes if available.
        /// </summary>
        None = 0,

        /// <summary>
        /// The equivalent of <see cref="ConfigureAwait(bool)"/> with `false`.
        /// </summary>
        DisableCaptureContext = 1,

        // IMPORTANT: the following align with CommandFlags, to avoid needing any additional mapping.

        /// <summary>
        /// The caller is not interested in the result; the caller will immediately receive a default-value
        /// of the expected return type (this value is not indicative of anything at the server).
        /// </summary>
        FireAndForget = 2,

        /// <summary>
        /// This operation should only be performed on the primary.
        /// </summary>
        DemandPrimary = 4,

        /// <summary>
        /// This operation should be performed on the replica if it is available, but will be performed on
        /// a primary if no replicas are available. Suitable for read operations only.
        /// </summary>
        PreferReplica = 8, // note: we're using a 2-bit set here, which [Flags] formatting hates

        /// <summary>
        /// This operation should only be performed on a replica. Suitable for read operations only.
        /// </summary>
        DemandReplica = 12, // note: we're using a 2-bit set here, which [Flags] formatting hates

        /// <summary>
        /// Indicates that this operation should not be forwarded to other servers as a result of an ASK or MOVED response.
        /// </summary>
        NoRedirect = 64,

        /// <summary>
        /// Indicates that script-related operations should use EVAL, not SCRIPT LOAD + EVALSHA.
        /// </summary>
        NoScriptCache = 512,
    }

    /// <inheritdoc/>
    public override string ToString() => _connection?.ToString() ?? "(null)";

    public RespConnection Connection => _connection;
    public int Database => _database;

    public RespCommandMap CommandMap => _connection.NonDefaultCommandMap ?? RespCommandMap.Default;
    public TimeSpan SyncTimeout => _connection.SyncTimeout;

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
    public Lifetime WithCombineCancellationToken(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled
            || cancellationToken == CancellationToken)
        {
            // would have no effect
            CancellationToken.ThrowIfCancellationRequested();
            return new(in this, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!CancellationToken.CanBeCanceled)
        {
            // we don't currently have cancellation; no need for a link
            return new(in this, null, cancellationToken);
        }

        CancellationToken.ThrowIfCancellationRequested();
        var src = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
        return new(in this, src, src.Token);
    }

    public Lifetime WithCombine(IDisposable lifetime)
        => new(in this, lifetime);

    public Lifetime WithCombineTimeout(TimeSpan timeout)
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

        return new Lifetime(in this, src, src.Token);
    }

    public readonly struct Lifetime : IDisposable
    {
        // Unusual public field; a ref-readonly would be preferable, but by-ref props have restrictions on structs.
        // We would rather avoid the copy semantics associated with a regular property getter.
        public readonly RespContext Context;

        private readonly IDisposable? _source;

        internal Lifetime(in RespContext context, IDisposable? source)
        {
            Context = context;
            _source = source;
        }

        internal Lifetime(in RespContext context, IDisposable? source, CancellationToken cancellationToken)
        {
            Context = context; // snapshot, we can now mutate this locally
            _source = source;
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
            ? _flags & ~RespContextFlags.DisableCaptureContext
            : _flags | RespContextFlags.DisableCaptureContext;
        return clone;
    }

    /// <summary>
    /// Replaces the <see cref="RespContextFlags"/> associated with this context.
    /// </summary>
    public RespContext WithFlags(RespContextFlags flags)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._flags) = flags;
        return clone;
    }

    /// <summary>
    /// Replaces the <see cref="RespContextFlags"/> and <see cref="Database"/> associated with this context.
    /// </summary>
    public RespContext With(int database, RespContextFlags flags)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._database) = database;
        Unsafe.AsRef(in clone._flags) = flags;
        return clone;
    }

    /// <summary>
    /// Replaces the <see cref="RespContextFlags"/> and <see cref="Database"/> associated with this context,
    /// using a mask to determine which flags to replace. Passing <see cref="RespContextFlags.None"/>
    /// for <paramref name="mask"/> will replace no flags.
    /// </summary>
    public RespContext With(int database, RespContextFlags flags, RespContextFlags mask)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._database) = database;
        Unsafe.AsRef(in clone._flags) = (flags & ~mask) | (_flags & mask);
        return clone;
    }

    public RespBatch CreateBatch(int sizeHint = 0)
#if MULTI_BATCH
        => new MergingBatchConnection(in this, sizeHint);
#else
        => new BasicBatchConnection(in this, sizeHint);
#endif
}
