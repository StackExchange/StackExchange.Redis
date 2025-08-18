namespace Resp.RedisCommands;

public static class RespConnectionExtensions
{
    public static RedisString String(this IRespConnection connection, string key) => new(connection, key);
}

internal sealed class RedisCommand<TRequest, TResponse>(IRespFormatter<TRequest> formatter, IRespParser<TResponse> parser)
{
    public TResponse Send(IRespConnection connection, TRequest request) => connection.Send("get"u8, request, formatter, parser);
}
