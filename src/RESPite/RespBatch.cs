namespace RESPite;

public abstract class RespBatch : RespConnection
{
    // a batch doesn't act as a proxy to the tail, so we don't need to DecoratorConnection logic
    protected readonly RespConnection Tail;
    private protected RespBatch(in RespContext tail) : base(tail)
    {
        Tail = tail.Connection;
    }

    public abstract Task FlushAsync();
    public abstract void Flush();
}
