using System;
using System.IO;
using System.Text;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// A <see cref="TextWriter"/> that forwards whole lines to an <see cref="ITestOutputHelper"/>.
/// </summary>
/// <remarks>
/// Needed because <c>ConnectionMultiplexer.ConnectGroupAsync</c> logs to a <see cref="TextWriter"/>, and that
/// log is where a member that never connected actually says so - the alternative is inferring it from a
/// failed assertion four minutes later.
/// <para>
/// Writes after the test has ended are swallowed: the multiplexer logs from its own threads, so a connect
/// attempt can outlive the test that started it, and xUnit throws rather than ignoring it.
/// </para>
/// </remarks>
internal sealed class TestOutputWriter(ITestOutputHelper output) : TextWriter
{
    private readonly StringBuilder _buffer = new(2048);

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_buffer)
        {
            if (value is '\n' or '\r')
            {
                if (_buffer.Length > 0) FlushBuffer();
            }
            else
            {
                _buffer.Append(value);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        if (value is null) return;

        lock (_buffer) // keep a whole line together
        {
            base.WriteLine(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_buffer)
        {
            if (_buffer.Length > 0) FlushBuffer();
        }

        base.Dispose(disposing);
    }

    // called under the _buffer lock
    private void FlushBuffer()
    {
        var text = _buffer.ToString();
        _buffer.Clear();
        try
        {
            output.WriteLine(text);
        }
        catch (InvalidOperationException)
        {
            // written from a background thread after the test ended; nothing useful to do with it
        }
    }
}
