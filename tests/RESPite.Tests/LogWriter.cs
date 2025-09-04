using System.IO;
using System.Text;
using Xunit;

namespace RESPite.Tests;

internal sealed class LogWriter(ITestOutputHelper? log) : TextWriter
{
    public override Encoding Encoding => Encoding.Unicode;
    public override void WriteLine(string? value) => log?.WriteLine(value ?? "");
}
