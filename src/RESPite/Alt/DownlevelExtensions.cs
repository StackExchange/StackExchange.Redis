using Resp;

namespace RESPite.Alt;

/// <summary>
/// For use with older compilers that don't support byref-return, extension-everything, etc.
/// </summary>
public static class DownlevelExtensions
{
    public static RespContext GetContext(this IRespConnection connection)
        => connection.Context;
}
