using System.Buffers;
using System.Text;
using RESPite.Internal;

namespace RESPite.Messages;

/// <summary>
/// Utility readers for common scenarios.
/// </summary>
public static class CommonReaders
{
    private static readonly Core shared = new();

    /// <summary>
    /// Parses a UTF8 payload as a string.
    /// </summary>
    public static IReader<Empty, string> StringUtf8 => shared;

    /// <summary>
    /// Parses any payload as a an empty value.
    /// </summary>
    public static IReader<Empty, Empty> Empty => shared;

    private sealed class Core : IReader<Empty, string>, IReader<Empty, Empty>
    {
        string IReader<Empty, string>.Read(in Empty request, in ReadOnlySequence<byte> content)
            => Constants.UTF8.GetString(content);
        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
            => RESPite.Empty.Value;
    }
}
