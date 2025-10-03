using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal static class RespContextExtensions
{
    // Question: cache this, or rebuild each time? the latter handles shutdown better.
    // internal readonly RespContext Context = proxy.Context.WithDatabase(db);
    internal static RespContext With(this in RespContext context, int db, CommandFlags flags)
    {
        // the flags intentionally align between CommandFlags and RespContextFlags
        const RespContext.RespContextFlags FlagMask = RespContext.RespContextFlags.DemandPrimary
                                                      | RespContext.RespContextFlags.DemandReplica
                                                      | RespContext.RespContextFlags.PreferReplica
                                                      | RespContext.RespContextFlags.NoRedirect
                                                      | RespContext.RespContextFlags.FireAndForget
                                                      | RespContext.RespContextFlags.NoScriptCache;
        return context.With(db, (RespContext.RespContextFlags)flags, FlagMask);
    }
}
