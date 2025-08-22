using System;
using System.Diagnostics;

namespace Resp;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[Conditional("DEBUG")]
internal sealed class RespCommandAttribute(string? command = null) : Attribute
{
    public string? Command => command;
}
