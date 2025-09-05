using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

/// <summary>
/// Implements IDatabase on top of a <see cref="IRespContextProxy"/>, which provides access to a RESP context; this
/// could be direct to a known server or routed - the <see cref="IRespContextProxy.Context"/> is responsible for
/// that determination.
/// </summary>
internal sealed partial class ProxiedDatabase(IRespContextProxy proxy, int db) : IDatabase
{
    // Question: cache this, or rebuild each time? the latter handles shutdown better.
    // internal readonly RespContext Context = proxy.Context.WithDatabase(db);
    internal RespContext Context => proxy.Context.WithDatabase(db);

    public int Database => db;

    public IConnectionMultiplexer Multiplexer => proxy.Multiplexer;
    public RespContextProxyKind RespContextProxyKind => proxy.RespContextProxyKind;

    public bool TryWait(Task task) => proxy.Multiplexer.TryWait(task);

    public void Wait(Task task) => proxy.Multiplexer.Wait(task);

    public T Wait<T>(Task<T> task) => proxy.Multiplexer.Wait(task);

    public void WaitAll(params Task[] tasks) => proxy.Multiplexer.WaitAll(tasks);
}
