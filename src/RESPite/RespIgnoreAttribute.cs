using System.ComponentModel;
using System.Diagnostics;

namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter)]
[Conditional("DEBUG"), ImmutableObject(true)]
public sealed class RespIgnoreAttribute(object? value = null) : Attribute
{
    // note; nulls are always ignored (taking NRTs into account); the purpose
    // of an explicit null is for RedisValue - this prompts HasValue checks (i.e. non-trivial value).
    public object? Value => value;
}
