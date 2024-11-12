using RESPite.Messages;

namespace RESPite.Resp.Writers;

/// <summary>
/// Writes RESP payloads.
/// </summary>
public interface IRespWriter<TRequest> : IWriter<TRequest>
{
    /// <summary>
    /// Write a given value.
    /// </summary>
    void Write(in TRequest request, ref RespWriter writer);
}
