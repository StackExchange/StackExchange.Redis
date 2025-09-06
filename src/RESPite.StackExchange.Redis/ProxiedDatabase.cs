using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

/// <summary>
/// Implements IDatabase on top of a <see cref="IRespContextProxy"/>, which provides access to a RESP context; this
/// could be direct to a known server or routed - the <see cref="IRespContextProxy.Context"/> is responsible for
/// that determination.
/// </summary>
internal partial class ProxiedDatabase : IDatabase
{
    private IRespContextProxy _proxy;
    private readonly int _db;

    /// <summary>
    /// Implements IDatabase on top of a <see cref="IRespContextProxy"/>, which provides access to a RESP context; this
    /// could be direct to a known server or routed - the <see cref="IRespContextProxy.Context"/> is responsible for
    /// that determination.
    /// </summary>
    public ProxiedDatabase(IRespContextProxy proxy, int db)
    {
        _proxy = proxy;
        _db = db;
    }

    // change the proxy being used
    protected void SetProxy(IRespContextProxy proxy)
        => _proxy = proxy;

    // Question: cache this, or rebuild each time? the latter handles shutdown better.
    // internal readonly RespContext Context = proxy.Context.WithDatabase(db);
    private RespContext Context(CommandFlags flags)
    {
        // the flags intentionally align between CommandFlags and RespContextFlags
        const RespContext.RespContextFlags flagMask = RespContext.RespContextFlags.DemandPrimary
                                                      | RespContext.RespContextFlags.DemandReplica
                                                      | RespContext.RespContextFlags.PreferReplica
                                                      | RespContext.RespContextFlags.NoRedirect
                                                      | RespContext.RespContextFlags.FireAndForget
                                                      | RespContext.RespContextFlags.NoScriptCache;

        return _proxy.Context.With(_db, (RespContext.RespContextFlags)flags, flagMask);
    }

    private TimeSpan SyncTimeout => _proxy.Context.SyncTimeout;
    public int Database => _db;

    IConnectionMultiplexer IRedisAsync.Multiplexer => _proxy.Multiplexer;
    public RespContextProxyKind RespContextProxyKind => _proxy.RespContextProxyKind;

    public bool TryWait(Task task) => _proxy.Multiplexer.TryWait(task);

    public void Wait(Task task) => _proxy.Multiplexer.Wait(task);

    public T Wait<T>(Task<T> task) => _proxy.Multiplexer.Wait(task);

    public void WaitAll(params Task[] tasks) => _proxy.Multiplexer.WaitAll(tasks);
}
