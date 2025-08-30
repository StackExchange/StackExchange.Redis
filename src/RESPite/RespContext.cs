using System.Runtime.CompilerServices;
using RESPite.Connections;
using RESPite.Connections.Internal;
using RESPite.Messages;

namespace RESPite;

/// <summary>
/// Transient state for a RESP operation.
/// </summary>
public readonly struct RespContext
{
    public static ref readonly RespContext Null => ref NullConnection.Instance.Context;

    private readonly IRespConnection _connection;
    private readonly CancellationToken _cancellationToken;
    private readonly int _database;

    private readonly int _flags;
    private const int FlagsDisableCaptureContext = 1 << 0;

    private const string CtorUsageWarning = $"The context from {nameof(IRespConnection)}.{nameof(IRespConnection.Context)} should be preferred, using {nameof(WithCancellationToken)} etc as necessary.";

    /// <inheritdoc/>
    public override string ToString() => _connection?.ToString() ?? "(null)";

    [Obsolete(CtorUsageWarning)]
    public RespContext(IRespConnection connection) : this(connection, -1, CancellationToken.None)
    {
    }

    [Obsolete(CtorUsageWarning)]
    public RespContext(IRespConnection connection, CancellationToken cancellationToken)
        : this(connection, -1, cancellationToken)
    {
    }

    /// <summary>
    /// Transient state for a RESP operation.
    /// </summary>
    [Obsolete(CtorUsageWarning)]
    public RespContext(
        IRespConnection connection,
        int database = -1,
        CancellationToken cancellationToken = default)
    {
        _connection = connection;
        _database = database;
        _cancellationToken = cancellationToken;
    }

    public IRespConnection Connection => _connection;
    public int Database => _database;
    public CancellationToken CancellationToken => _cancellationToken;

    public RespCommandMap CommandMap => _connection.Configuration.CommandMap;

    public RespContext WithCancellationToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RespContext clone = this;
        Unsafe.AsRef(in clone._cancellationToken) = cancellationToken;
        return clone;
    }

    public RespContext WithDatabase(int database)
    {
        RespContext clone = this;
        Unsafe.AsRef(in clone._database) = database;
        return clone;
    }

    public RespContext WithConnection(IRespConnection connection)
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

    public IBatchConnection CreateBatch(int sizeHint = 0) => new BatchConnection(in this, sizeHint);

    internal static RespContext For(IRespConnection connection)
#pragma warning disable CS0618 // Type or member is obsolete
        => new(connection);
#pragma warning restore CS0618 // Type or member is obsolete
}
