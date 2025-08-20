using System;
using System.Threading;

namespace Resp.RedisCommands;

public static class RespConnectionExtensions
{
    public static RedisStrings Strings(this IRespConnection connection, TimeSpan timeout = default) => new(connection, timeout);
    public static RedisStrings Strings(this IRespConnection connection, CancellationToken cancellationToken) => new(connection, cancellationToken);
}
