using System.ComponentModel;
using System.Diagnostics;

namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
[Conditional("DEBUG"), ImmutableObject(true)]
public sealed class RespSuffixAttribute(string token) : Attribute
{
    public string Token => token;
}
