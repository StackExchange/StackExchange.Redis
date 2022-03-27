namespace StackExchange.Redis;

internal enum WriteResult
{
    Success,
    NoConnectionAvailable,
    TimeoutBeforeWrite,
    WriteFailure,
}
