using System.Runtime.CompilerServices;
using Resp;

namespace RESPite.Redis.Alt; // legacy fallback for down-level compilers

/// <summary>
/// For use with older compilers that don't support byref-return, extension-everything, etc.
/// </summary>
public static class DownlevelExtensions
{
    public static RedisStrings AsStrings(this in RespContext context)
        => Unsafe.As<RespContext, RedisStrings>(ref Unsafe.AsRef(in context));

    public static RedisKeys AsKeys(this in RespContext context)
        => Unsafe.As<RespContext, RedisKeys>(ref Unsafe.AsRef(in context));
}
