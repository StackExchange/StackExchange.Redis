namespace RESPite.Connections.Internal;

internal sealed class NullConnection : RespConnection
{
    public static NullConnection WithConfiguration(RespConfiguration configuration)
        => ReferenceEquals(configuration, RespConfiguration.Default)
            ? Default
            : new(configuration);

    public static readonly NullConnection Default = new(RespConfiguration.Default);

    internal override int OutstandingOperations => 0;

    private NullConnection(RespConfiguration configuration) : base(configuration)
    {
    }

    private const string SendErrorMessage = "Null connections do not support sending messages.";
    public override void Write(in RespOperation message)
    {
        message.Message.TrySetException(message.Token, new NotSupportedException(SendErrorMessage));
    }

    public override Task WriteAsync(in RespOperation message)
    {
        Write(message);
        return Task.CompletedTask;
    }

    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError
    {
        add { }
        remove { }
    }

    internal override void ThrowIfUnhealthy() { }
}
