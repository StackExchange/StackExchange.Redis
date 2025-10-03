namespace RESPite.Messages;

public interface IRespParser<TState, out TResponse>
{
    /// <summary>
    /// Parse <paramref name="reader"/> into a <typeparamref name="TResponse"/>,
    /// using the state from <paramref name="state"/>.
    /// </summary>
    /// <param name="state">The state to use when parsing.</param>
    /// <param name="reader">The reader to parse.</param>
    TResponse Parse(in TState state, ref RespReader reader);
}
