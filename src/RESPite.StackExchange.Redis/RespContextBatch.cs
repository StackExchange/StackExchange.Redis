using RESPite.Connections;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed class RespContextBatch : RespContextDatabase, IBatch, IDisposable, IRespContextSource
{
    private readonly RespBatch _batch;

    public RespContextBatch(IRedisAsync parent, IRespContextSource source, int db) : base(parent, source, db)
    {
        _batch = source.Context.CreateBatch();
        SetSource(this);
    }

    void IBatch.Execute() => _batch.Flush();

    public void Dispose() => _batch.Dispose();

    public ref readonly RespContext Context => ref _batch.Context;
}
