using System.Runtime.CompilerServices;

namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly ServerCommands Servers(this in RespContext context)
        => ref Unsafe.As<RespContext, ServerCommands>(ref Unsafe.AsRef(in context));
}
internal readonly struct ServerCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}
internal static partial class ServerCommandsExtensions
{
    [RespCommand]
    public static partial void Ping(this in ServerCommands context);
}
