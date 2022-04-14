using System;

namespace StackExchange.Redis;

internal enum WriteResult
{
    Success,
    NoConnectionAvailable,
    [Obsolete("Probably doesn't apply any more?")]
    TimeoutBeforeWrite,
    WriteFailure,
}
