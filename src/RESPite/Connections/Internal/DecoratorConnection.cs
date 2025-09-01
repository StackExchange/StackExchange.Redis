namespace RESPite.Connections.Internal;

internal abstract class DecoratorConnection : RespConnection
{
    protected readonly RespConnection Tail;

    public DecoratorConnection(in RespContext tail, RespConfiguration? configuration = null)
        : base(tail, configuration)
    {
        Tail = tail.Connection;
    }

    protected virtual bool OwnsConnection => true;

    internal override bool IsHealthy => base.IsHealthy & Tail.IsHealthy;
    internal override int OutstandingOperations => Tail.OutstandingOperations;

    protected override void OnDispose(bool disposing)
    {
        if (disposing & OwnsConnection) Tail.Dispose();
    }

    protected override ValueTask OnDisposeAsync() =>
        OwnsConnection ? Tail.DisposeAsync() : default;

    // Note that default behaviour *does not* add a dispose check, as it
    // assumes that the connection is "owned", and therefore the tail will throw.
    public override void Write(in RespOperation message) => Tail.Write(message);

    internal override void Write(ReadOnlySpan<RespOperation> messages) => Tail.Write(messages);

    public override Task WriteAsync(in RespOperation message) => Tail.WriteAsync(in message);

    internal override Task WriteAsync(ReadOnlyMemory<RespOperation> messages) => Tail.WriteAsync(messages);
}
