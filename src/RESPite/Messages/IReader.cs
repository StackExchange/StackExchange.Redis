using System.Buffers;

namespace RESPite.Messages;

/// <summary>
/// Reads payloads.
/// </summary>
public interface IReader<TRequest, TResponse>
{
    /// <summary>
    /// Read a given value.
    /// </summary>
    TResponse Read(in TRequest request, in ReadOnlySequence<byte> content);
}
