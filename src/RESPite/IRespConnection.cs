namespace RESPite;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    RespConfiguration Configuration { get; }
    bool CanWrite { get; }
    int Outstanding { get; }

    /// <summary>
    /// Gets the default context associates with this connection.
    /// </summary>
    ref readonly RespContext Context { get; }

    void Send(in RespOperation message);
    void Send(ReadOnlySpan<RespOperation> message);

    Task SendAsync(in RespOperation message);
    Task SendAsync(ReadOnlyMemory<RespOperation> message);
}
