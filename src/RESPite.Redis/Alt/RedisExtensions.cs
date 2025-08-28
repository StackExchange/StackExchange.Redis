using System.Runtime.CompilerServices;
using Resp;

namespace RESPite.Redis.Alt; // legacy fallback for down-level compilers

public static class RedisExtensions
{
    public static ref readonly RedisStrings AsStrings(this in RespContext context)
        => ref Unsafe.As<RespContext, RedisStrings>(ref Unsafe.AsRef(in context));

    public static ref readonly RedisKeys AsKeys(this in RespContext context)
        => ref Unsafe.As<RespContext, RedisKeys>(ref Unsafe.AsRef(in context));
}
