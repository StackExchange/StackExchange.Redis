using RESPite.Messages;
using Xunit;

namespace RESPite.Tests;

public class RespScannerTests
{
    [Fact]
    public void ScanNull()
    {
        RespScanState scanner = default;
        Assert.True(scanner.TryRead("_\r\n"u8, out var consumed));

        Assert.Equal(3, consumed);
        Assert.Equal(3, scanner.TotalBytes);
        Assert.Equal(RespPrefix.Null, scanner.Prefix);
    }
}
