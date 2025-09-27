namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class RespSuffixAttribute(string token) : Attribute
{
    public string Token => token;
}
