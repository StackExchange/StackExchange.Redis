using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

/// <summary>
/// Implements IDatabase on top of a <see cref="IRespContextSource"/>, which provides access to a RESP context; this
/// could be direct to a known server or routed - the <see cref="IRespContextSource.Context"/> is responsible for
/// that determination.
/// </summary>
internal partial class RespContextDatabase : IDatabase
{
    private IRespContextSource source;
    private readonly int _db;

    /// <summary>
    /// Implements IDatabase on top of a <see cref="IRespContextSource"/>, which provides access to a RESP context; this
    /// could be direct to a known server or routed - the <see cref="IRespContextSource.Context"/> is responsible for
    /// that determination.
    /// </summary>
    public RespContextDatabase(IRespContextSource source, int db)
    {
        this.source = source;
        _db = db;
    }

    // change the proxy being used
    protected void SetSource(IRespContextSource source)
        => this.source = source;

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

        return source.Context.With(_db, (RespContext.RespContextFlags)flags, flagMask);
    }

    private TimeSpan SyncTimeout => source.Context.SyncTimeout;
    public int Database => _db;

    IConnectionMultiplexer IRedisAsync.Multiplexer => source.Multiplexer;
    public RespContextProxyKind RespContextProxyKind => source.RespContextProxyKind;

    public bool TryWait(Task task) => source.Multiplexer.TryWait(task);

    public void Wait(Task task) => source.Multiplexer.Wait(task);

    public T Wait<T>(Task<T> task) => source.Multiplexer.Wait(task);

    public void WaitAll(params Task[] tasks) => source.Multiplexer.WaitAll(tasks);
}
