using RESPite.Messages;

namespace RESPite.Resp.Readers;

/// <summary>
/// Reads RESP payloads.
/// </summary>
public interface IRespReader<TRequest, TResponse> : IReader<TRequest, TResponse>
{
    /// <summary>
    /// Read a given value.
    /// </summary>
    TResponse Read(in TRequest request, ref RespReader reader);
}
