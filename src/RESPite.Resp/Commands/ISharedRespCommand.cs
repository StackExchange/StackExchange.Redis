using RESPite.Messages;

namespace RESPite.Resp.Commands;

#if NET8_0_OR_GREATER
/// <summary>
/// Represents a RESP command that uses a single shared instance as the implementation.
/// </summary>
/// <typeparam name="TRequest">The type that represents this command.</typeparam>
/// <typeparam name="TResponse">The type of data returned from this command.</typeparam>
public interface ISharedRespCommand<TRequest, TResponse> : IRespCommand<TRequest, TResponse>
{
    /// <summary>
    /// The shared command instance for this operation.
    /// </summary>
    static abstract ref readonly TRequest Command { get; }
}
#endif
