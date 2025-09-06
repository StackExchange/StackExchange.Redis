using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed class ProxiedBatch : ProxiedDatabase, IBatch, IRespContextProxy
{
    private readonly IRespContextProxy _originalProxy;
    private RespBatch _batch;

    public ProxiedBatch(IRespContextProxy proxy, int db) : base(proxy, db)
    {
        _originalProxy = proxy;
        _batch = proxy.Context.CreateBatch();
        SetProxy(this);
    }

    void IBatch.Execute() => _batch.Flush();

    public RespMultiplexer Multiplexer => _originalProxy.Multiplexer;
    public ref readonly RespContext Context => ref _batch.Context;
    RespContextProxyKind IRespContextProxy.RespContextProxyKind => RespContextProxyKind;
}
