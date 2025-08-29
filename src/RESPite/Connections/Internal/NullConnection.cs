namespace RESPite.Connections.Internal;

internal sealed class NullConnection : IRespConnection
{
    private readonly RespContext _context;

    public static NullConnection WithConfiguration(RespConfiguration configuration)
        => ReferenceEquals(configuration, RespConfiguration.Default)
            ? Instance
            : new(configuration);

    private NullConnection(RespConfiguration configuration)
    {
        _context = RespContext.For(this);
        Configuration = configuration;
    }

    public static readonly NullConnection Instance = new(RespConfiguration.Default);
    public void Dispose() { }

    public ValueTask DisposeAsync() => default;

    public RespConfiguration Configuration { get; }
    public bool CanWrite => false;
    public int Outstanding => 0;

    public ref readonly RespContext Context => ref _context;

    private const string SendErrorMessage = "Null connections do not support sending messages.";
    public void Send(in RespOperation message) => throw new NotSupportedException(SendErrorMessage);

    public void Send(ReadOnlySpan<RespOperation> message) => throw new NotSupportedException(SendErrorMessage);

    public Task SendAsync(in RespOperation message) => throw new NotSupportedException(SendErrorMessage);

    public Task SendAsync(ReadOnlyMemory<RespOperation> message) => throw new NotSupportedException(SendErrorMessage);
}
