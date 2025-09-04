namespace RESPite.StackExchange.Redis;

internal sealed class RoutingRespConnection(RespMultiplexer multiplexer, in RespContext tail)
    : RespConnection(in tail, multiplexer.Configuration)
{
    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError;
    internal override int OutstandingOperations => 0;

    internal void OnConnectionError(RespConnectionErrorEventArgs e) => ConnectionError?.Invoke(this, e);

    public override void Write(in RespOperation message)
    {
        throw new NotImplementedException();
    }
}
