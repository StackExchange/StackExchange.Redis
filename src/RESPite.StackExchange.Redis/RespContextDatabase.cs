using RESPite.Connections;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

/// <summary>
/// Implements IDatabase on top of a <see cref="IRespContextSource"/>, which provides access to a RESP context; this
/// could be direct to a known server or routed - the <see cref="IRespContextSource.Context"/> is responsible for
/// that determination.
/// </summary>
internal partial class RespContextDatabase : IDatabase
{
    private readonly IConnectionMultiplexer _muxer;
    private IRespContextSource _source;
    private readonly int _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="RespContextDatabase"/> class.
    /// Implements IDatabase on top of a <see cref="IRespContextSource"/>, which provides access to a RESP context; this
    /// could be direct to a known server or routed - the <see cref="IRespContextSource.Context"/> is responsible for
    /// that determination.
    /// </summary>
    public RespContextDatabase(IConnectionMultiplexer muxer, IRespContextSource source, int db)
    {
        _muxer = muxer;
        _source = source;
        _db = db;
    }

    // change the proxy being used
    protected void SetSource(IRespContextSource source)
        => this._source = source;

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

        return _source.Context.With(_db, (RespContext.RespContextFlags)flags, flagMask);
    }

    private TimeSpan SyncTimeout => _source.Context.SyncTimeout;
    public int Database => _db;

    IConnectionMultiplexer IRedisAsync.Multiplexer => _muxer;

    public bool TryWait(Task task) => task.Wait(SyncTimeout);

    public void Wait(Task task) => _muxer.Wait(task);

    public T Wait<T>(Task<T> task) => _muxer.Wait(task);

    public void WaitAll(params Task[] tasks) => _muxer.WaitAll(tasks);
}
