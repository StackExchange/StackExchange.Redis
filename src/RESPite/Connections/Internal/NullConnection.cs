namespace RESPite.Connections.Internal;

internal sealed class NullConnection : RespConnection
{
    private enum FailureMode
    {
        Default,
        Disposed,
        NonRoutable,
    }

    private readonly FailureMode _failureMode;

    public static NullConnection WithConfiguration(RespConfiguration configuration)
        => ReferenceEquals(configuration, RespConfiguration.Default)
            ? Default
            : new(configuration, FailureMode.Default);

    // convenience singletons (all but Default are lazily created)
    public static readonly NullConnection Default = new(RespConfiguration.Default, FailureMode.Default);
    private static NullConnection? _disposed, _nonRoutable;
    public static NullConnection Disposed =>
        _disposed ??= new(RespConfiguration.Default, FailureMode.Disposed);
    public static NullConnection NonRoutable =>
        _nonRoutable ??= new(RespConfiguration.Default, FailureMode.NonRoutable);

    internal override int OutstandingOperations => 0;

    private NullConnection(RespConfiguration configuration, FailureMode failureMode) : base(configuration)
        => _failureMode = failureMode;

    private void SetError(in RespOperation message)
    {
        message.TrySetException(_failureMode switch
        {
            FailureMode.Disposed => new ObjectDisposedException(nameof(RespConnection)),
            FailureMode.NonRoutable => new InvalidOperationException("No connection is available for this operation."),
            _ => new NotSupportedException("Null connections do not support sending messages."),
        });
    }

    public override void Write(in RespOperation message) => SetError(in message);

    public override Task WriteAsync(in RespOperation message)
    {
        SetError(message);
        return Task.CompletedTask;
    }

    internal override void Write(ReadOnlySpan<RespOperation> messages)
    {
        foreach (var message in messages)
        {
            SetError(in message);
        }
    }

    internal override Task WriteAsync(ReadOnlyMemory<RespOperation> messages)
    {
        foreach (var message in messages.Span)
        {
            SetError(in message);
        }

        return Task.CompletedTask;
    }

    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError
    {
        add
        {
        }
        remove
        {
        }
    }

    internal override void ThrowIfUnhealthy() { }
}
