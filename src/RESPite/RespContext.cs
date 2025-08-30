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
