using System;

namespace Resp.RedisCommands;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class RespCommandAttribute(string? command = null) : Attribute
{
    public string? Command => command;
}
