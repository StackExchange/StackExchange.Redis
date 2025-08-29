using System.Runtime.CompilerServices;
using RESPite.Connections;

namespace RESPite;

/// <summary>
/// Transient state for a RESP operation.
/// </summary>
public readonly struct RespContext
{
    private readonly IRespConnection _connection;
    private readonly int _database;
    private readonly CancellationToken _cancellationToken;

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
/*
    public RespMessageBuilder<T> Command<T>(ReadOnlySpan<byte> command, T value, IRespFormatter<T> formatter)
        => new(this, command, value, formatter);

    public RespMessageBuilder<Void> Command(ReadOnlySpan<byte> command)
        => new(this, command, Void.Instance, RespFormatters.Void);

    public RespMessageBuilder<string> Command(ReadOnlySpan<byte> command, string value, bool isKey)
        => new(this, command, value, RespFormatters.String(isKey));

    public RespMessageBuilder<byte[]> Command(ReadOnlySpan<byte> command, byte[] value, bool isKey)
        => new(this, command, value, RespFormatters.ByteArray(isKey));
        */

    public RespCommandMap RespCommandMap => _connection.Configuration.RespCommandMap;

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

    public IBatchConnection CreateBatch(int sizeHint = 0) => new BatchConnection(in this, sizeHint);

    internal static RespContext For(IRespConnection connection)
#pragma warning disable CS0618 // Type or member is obsolete
        => new(connection);
#pragma warning restore CS0618 // Type or member is obsolete
}
