namespace RESPite.Connections.Internal;

internal sealed class NullConnection : RespConnection
{
    public static NullConnection WithConfiguration(RespConfiguration configuration)
        => ReferenceEquals(configuration, RespConfiguration.Default)
            ? Default
            : new(configuration);

    public static readonly NullConnection Default = new(RespConfiguration.Default);

    private NullConnection(RespConfiguration configuration) : base(configuration)
    {
    }

    private const string SendErrorMessage = "Null connections do not support sending messages.";
    public override void Send(in RespOperation message)
    {
        message.Message.TrySetException(message.Token, new NotSupportedException(SendErrorMessage));
    }

    public override Task SendAsync(in RespOperation message)
    {
        Send(message);
        return Task.CompletedTask;
    }
}
