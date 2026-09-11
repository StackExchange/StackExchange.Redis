using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The comma-and-range slot form the cluster notifications carry, e.g. <c>123,456,789-1000</c>. Split out
/// because it is the one part of stage 3 that a wire capture cannot invalidate - it is the same form
/// <c>CLUSTER NODES</c> has always used.
/// </summary>
public class ClusterSlotMigrationUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData("123", "123-123")]
    [InlineData("123,456", "123-123,456-456")]
    [InlineData("789-1000", "789-1000")]
    [InlineData("123,456,789-1000", "123-123,456-456,789-1000")]
    [InlineData("0-16383", "0-16383")]
    [InlineData("5-5", "5-5")]
    public void ParsesTheSlotForm(string input, string expected)
    {
        Assert.True(SlotRange.TryParseList(input, out var ranges));
        var actual = string.Join(",", ranges.Select(x => $"{x.From}-{x.To}"));
        log.WriteLine($"{input} -> {actual}");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("123,,456", "123-123,456-456")] // empty element, skipped
    [InlineData("123,", "123-123")] // trailing comma
    [InlineData(",123", "123-123")] // leading comma
    public void ToleratesEmptyElements(string input, string expected)
    {
        Assert.True(SlotRange.TryParseList(input, out var ranges));
        Assert.Equal(expected, string.Join(",", ranges.Select(x => $"{x.From}-{x.To}")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("-5")] // no lower bound
    [InlineData("5-")] // no upper bound
    [InlineData("1000-789")] // reversed: a server bug, not something to normalize silently
    [InlineData("1-2-3")]
    [InlineData("123,abc")] // one bad element fails the list: a partial slot set is worse than none
    public void RejectsMalformedInput(string? input)
    {
        Assert.False(SlotRange.TryParseList(input, out var ranges));
        log.WriteLine($"'{input}' rejected, {ranges.Count} range(s)");
    }

    [Fact]
    public void OutOfRangeSlotIsRejected()
    {
        // 16384 does not fit the short, and a checked cast would throw rather than wrap
        Assert.False(SlotRange.TryParseList("99999", out _));
    }
}
