using System.ComponentModel;
using System.Diagnostics;

namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter)]
[Conditional("DEBUG"), ImmutableObject(true)]
public sealed class RespIgnoreAttribute : Attribute
{
    private readonly object _value;
    public object Value => _value;
    public RespIgnoreAttribute(object value) => _value = value;
}
