namespace RESPite;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class RespPrefixAttribute(string token) : Attribute
{
    public string Token => token;
}
