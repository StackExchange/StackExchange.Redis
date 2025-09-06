using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed class RespContextBatch : RespContextDatabase, IBatch, IRespContextSource, IDisposable
{
    private readonly IRespContextSource _originalSource;
    private readonly RespBatch _batch;

    public RespContextBatch(IRespContextSource source, int db) : base(source, db)
    {
        _originalSource = source;
        _batch = source.Context.CreateBatch();
        SetSource(this);
    }

    void IBatch.Execute() => _batch.Flush();

    public void Dispose() => _batch.Dispose();

    public RespMultiplexer Multiplexer => _originalSource.Multiplexer;

    public ref readonly RespContext Context => ref _batch.Context;

    RespContextProxyKind IRespContextSource.RespContextProxyKind => RespContextProxyKind;
}
