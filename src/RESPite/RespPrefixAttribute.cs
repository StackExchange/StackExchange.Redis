using System.ComponentModel;
using System.Diagnostics;

namespace RESPite;

// note: omitting the token means that a collection-count prefix will be written
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
[Conditional("DEBUG"), ImmutableObject(true)]
public sealed class RespPrefixAttribute(string token = "") : Attribute
{
    public string Token => token;
}
