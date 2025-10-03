using System.Runtime.CompilerServices;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly StringCommands Strings(this in RespContext context)
        => ref Unsafe.As<RespContext, StringCommands>(ref Unsafe.AsRef(in context));
}

internal readonly struct StringCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class StringCommandsExtensions
{
    [RespCommand("get")]
    public static partial RespOperation<RedisValue> Get(this in StringCommands context, RedisKey key);
}
