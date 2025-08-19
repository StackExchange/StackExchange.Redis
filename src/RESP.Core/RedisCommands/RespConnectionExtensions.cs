using System;

namespace Resp.RedisCommands;

public static class RespConnectionExtensions
{
    public static RedisString String(this IRespConnection connection, string key, TimeSpan timeout = default) => new(connection, key, timeout);
}
