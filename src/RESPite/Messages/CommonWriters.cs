using System.Buffers;

namespace RESPite.Messages;

/// <summary>
/// Utility writers for common scenarios.
/// </summary>
public static class CommonWriters
{
    private static readonly Core shared = new();

    /// <summary>
    /// Writes any payload as a an empty value.
    /// </summary>
    public static IWriter<Empty> Empty => shared;

    private sealed class Core : IWriter<Empty>
    {
        void IWriter<Empty>.Write(in Empty request, IBufferWriter<byte> target) { }
    }
}
