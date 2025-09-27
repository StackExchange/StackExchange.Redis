namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    public static ref readonly RespContext Self(this in RespContext context)
        => ref context; // this just proves that the above are well-defined in terms of escape analysis
}
