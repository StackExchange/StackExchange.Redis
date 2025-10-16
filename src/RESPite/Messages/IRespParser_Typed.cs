namespace RESPite.Messages;

/// <summary>
/// Parses a RESP response into a typed value of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of value being parsed.</typeparam>
public interface IRespParser<out TResponse>
{
    /// <summary>
    /// Parse <paramref name="reader"/> into a <typeparamref name="TResponse"/>.
    /// </summary>
    TResponse Parse(ref RespReader reader);
}
