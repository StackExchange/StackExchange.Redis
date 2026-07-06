using System.Text;

namespace StackExchange.Redis.Build;

/// <summary>
/// Thin builder-style wrapper around <see cref="StringBuilder"/> that centralizes the indent
/// handling used when emitting generated C#; replaces the per-method "<c>int indent</c> plus
/// local <c>NewLine()</c>" pattern.
/// </summary>
internal sealed class CodeWriter(StringBuilder buffer)
{
    private int _indent;

    /// <summary>Starts a new line, applying the current indent.</summary>
    public CodeWriter NewLine()
    {
        buffer.AppendLine().Append(' ', _indent * 4);
        return this;
    }

    /// <summary>Increases the indent level.</summary>
    public CodeWriter Indent()
    {
        _indent++;
        return this;
    }

    /// <summary>Decreases the indent level.</summary>
    public CodeWriter Outdent()
    {
        _indent--;
        return this;
    }

    public CodeWriter Append(string? value)
    {
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(char value)
    {
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(int value)
    {
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(long value)
    {
        buffer.Append(value);
        return this;
    }
}
