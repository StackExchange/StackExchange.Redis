using System.Text;
using RESPite.Messages;
using Xunit;

namespace RESPite.Tests;

/// <summary>
/// Length prefixes near int.MaxValue with no payload yet must report "need more data".
/// </summary>
public class RespReaderOverflowTests
{
    [Theory]
    // bulk string / bulk error / verbatim string length prefixes
    [InlineData("$2147483647\r\n")]          // int.MaxValue
    [InlineData("$2147483634\r\n")]          // just over the overflow boundary
    [InlineData("$2147483633\r\n")]          // just under the overflow boundary
    [InlineData("$1073741824\r\n")]          // 2^30
    [InlineData("!2147483647\r\n")]          // bulk error
    [InlineData("=2147483647\r\n")]          // verbatim string
    // streaming scalar continuation chunk length
    [InlineData("$?\r\n;2147483647\r\n")]
    public void HugeLengthPrefixNeedsMoreData(string header)
    {
        var bytes = Encoding.ASCII.GetBytes(header);

        RespScanState state = default;
        bool complete = state.TryRead(bytes, out _);

        Assert.False(complete, "a header without its declared payload must report need-more-data");
    }
}
