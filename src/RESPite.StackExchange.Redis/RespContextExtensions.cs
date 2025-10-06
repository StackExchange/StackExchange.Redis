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

    internal static BoundedDouble Start(this Exclude exclude, double value)
        => new(value, (exclude & Exclude.Start) != 0);
    internal static BoundedDouble Stop(this Exclude exclude, double value)
        => new(value, (exclude & Exclude.Stop) != 0);

    internal static BoundedRedisValue StartLex(this Exclude exclude, RedisValue value)
        => new(value, (exclude & Exclude.Start) != 0);
    internal static BoundedRedisValue StopLex(this Exclude exclude, RedisValue value)
        => new(value, (exclude & Exclude.Stop) != 0);
}
