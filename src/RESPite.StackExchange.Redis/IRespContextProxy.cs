namespace RESPite.StackExchange.Redis;

/// <summary>
/// Provides access to a RESP context to use for operations; this context could be direct to a known server or routed.
/// </summary>
internal interface IRespContextProxy
{
    RespMultiplexer Multiplexer { get; }
    ref readonly RespContext Context { get; }
    RespContextProxyKind RespContextProxyKind { get; }
}

internal enum RespContextProxyKind
{
    Unknown,
    Multiplexer,
    ConnectionInteractive,
    ConnectionSubscription,
    Batch,
}
