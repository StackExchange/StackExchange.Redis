using System.ComponentModel;
using System.Diagnostics;

namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter)]
[Conditional("DEBUG"), ImmutableObject(true)]
public sealed class RespKeyAttribute() : Attribute
{
}
