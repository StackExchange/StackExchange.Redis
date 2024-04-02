using System.Buffers;

namespace RESPite.Messages;

/// <summary>
/// Writes payloads
/// </summary>
public interface IWriter<TRequest>
{
    /// <summary>
    /// Write a given value
    /// </summary>
    void Write<TTarget>(in TRequest request, ref TTarget target) where TTarget : IBufferWriter<byte>;
}

