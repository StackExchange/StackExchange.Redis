using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class Issue2653
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abcdef", "abcdef")]
    [InlineData("abc.def", "abc.def")]
    [InlineData("abc d \t  ef", "abc-d-ef")]
    [InlineData("  abc\r\ndef\n", "abc-def")]
    public void CheckLibraySanitization(string input, string expected)
        => Assert.Equal(expected, ServerEndPoint.ClientInfoSanitize(input));
}
