namespace Resp.RedisCommands;

public static class RespConnectionExtensions
{
    public static RedisStrings Strings(this in RespContext context) => new(context);
    public static RedisStrings Strings(this IRespConnection connection) => new RespContext(connection).Strings();
}
