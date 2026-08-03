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
        buffer.AppendLine();
        _lineHasContent = false;
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

    private bool _lineHasContent;

    private void IndentIfNeeded()
    {
        if (!_lineHasContent)
        {
            buffer.Append(' ', _indent * 4);
            _lineHasContent = true;
        }
    }

    public CodeWriter Append(string? value)
    {
        IndentIfNeeded();
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(char value)
    {
        IndentIfNeeded();
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(int value)
    {
        IndentIfNeeded();
        buffer.Append(value);
        return this;
    }

    public CodeWriter Append(long value)
    {
        IndentIfNeeded();
        buffer.Append(value);
        return this;
    }
}
