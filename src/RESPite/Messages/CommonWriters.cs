using System.Buffers;
using System.Text;

namespace RESPite.Messages;

/// <summary>
/// Utility writers for common scenarios
/// </summary>
public static class CommonWriters
{
    private static readonly Core shared = new();

    /// <summary>
    /// Writes any payload as a an empty value
    /// </summary>
    public static IWriter<Empty> Empty => shared;
    private sealed class Core : IWriter<Empty>
    {
        void IWriter<Empty>.Write<TTarget>(in Empty request, ref TTarget target) { }
    }
}
