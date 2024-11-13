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

/// <summary>
/// Additional methods for <see cref="IReader{TRequest, TResponse}"/> instances.
/// </summary>
public static class ReaderExtensions
{
    /// <summary>
    /// Read a stateless value (readers using <see cref="Empty"/> as their request type).
    /// </summary>
    public static TResponse Read<TResponse>(this IReader<Empty, TResponse> reader, in ReadOnlySequence<byte> content)
        => reader.Read(in Empty.Value, in content);
}
