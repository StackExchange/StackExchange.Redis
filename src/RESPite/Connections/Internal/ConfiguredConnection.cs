namespace RESPite.Connections.Internal;

internal sealed class ConfiguredConnection : IRespConnection
{
    private readonly IRespConnection _tail;
    private readonly RespConfiguration _configuration;
    private readonly RespContext _context;

    public ref readonly RespContext Context => ref _context;

    public ConfiguredConnection(in RespContext tail, RespConfiguration configuration)
    {
        _tail = tail.Connection;
        _configuration = configuration;
        _context = tail.WithConnection(this);
    }

    public void Dispose() => _tail.Dispose();

    public ValueTask DisposeAsync() => _tail.DisposeAsync();

    public RespConfiguration Configuration => _configuration;

    public bool CanWrite => _tail.CanWrite;

    public int Outstanding => _tail.Outstanding;

    public void Send(in RespOperation message) => _tail.Send(message);
    public void Send(ReadOnlySpan<RespOperation> messages) => _tail.Send(messages);

    public Task SendAsync(in RespOperation message) =>
        _tail.SendAsync(message);

    public Task SendAsync(ReadOnlyMemory<RespOperation> messages) => _tail.SendAsync(messages);
}
