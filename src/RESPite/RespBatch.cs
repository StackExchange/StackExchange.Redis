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

    internal override void ThrowIfUnhealthy()
    {
        Tail.ThrowIfUnhealthy();
        base.ThrowIfUnhealthy();
    }

    internal override bool IsHealthy => base.IsHealthy & Tail.IsHealthy;

    /// <summary>
    /// Suggests that the batch should ensure it has enough capacity for the given number of additional operations.
    /// Note that this contrasts with <see cref="List{T}"/>, where the number provided
    /// is the total number of elements.
    /// </summary>
    public virtual void EnsureCapacity(int additionalCount) { }
}
