using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class Misc(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData(":1\r\n", StreamTrimResult.Deleted)] // Integer 1
    [InlineData(":-1\r\n", StreamTrimResult.NotFound)] // Integer -1
    [InlineData(":2\r\n", StreamTrimResult.NotDeleted)] // Integer 2
    [InlineData("+1\r\n", StreamTrimResult.Deleted)] // Simple string "1"
    [InlineData("$1\r\n1\r\n", StreamTrimResult.Deleted)] // Bulk string "1"
    [InlineData("*1\r\n:1\r\n", StreamTrimResult.Deleted)] // Unit array with integer 1
    public void Int32EnumProcessor_StreamTrimResult(string resp, StreamTrimResult expected)
    {
        var processor = ResultProcessor.StreamTrimResult;
        var result = Execute(resp, processor);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_EmptyArray()
    {
        var resp = "*0\r\n";
        var processor = ResultProcessor.StreamTrimResultArray;
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_NullArray()
    {
        var resp = "*-1\r\n";
        var processor = ResultProcessor.StreamTrimResultArray;
        var result = Execute(resp, processor);
        Assert.Null(result);
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_MultipleValues()
    {
        // Array with 3 elements: [1, -1, 2]
        var resp = "*3\r\n:1\r\n:-1\r\n:2\r\n";
        var processor = ResultProcessor.StreamTrimResultArray;
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(StreamTrimResult.Deleted, result[0]);
        Assert.Equal(StreamTrimResult.NotFound, result[1]);
        Assert.Equal(StreamTrimResult.NotDeleted, result[2]);
    }

    [Fact]
    public void ConnectionIdentityProcessor_ReturnsEndPoint()
    {
        // ConnectionIdentityProcessor doesn't actually read from the RESP response,
        // it just returns the endpoint from the connection (or null if no bridge).
        var resp = "+OK\r\n";
        var processor = ResultProcessor.ConnectionIdentity;
        var result = Execute(resp, processor);

        // No bridge in test helper means result is null, but that's OK
        Assert.Null(result);
    }

    [Fact]
    public void DigestProcessor_ValidDigest()
    {
        // DigestProcessor reads a scalar string containing a hex digest
        // Example: XXh3 digest of "asdfasd" is "91d2544ff57ccca3"
        var resp = "$16\r\n91d2544ff57ccca3\r\n";
        var processor = ResultProcessor.Digest;
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.True(result.HasValue);

        // Parse the expected digest and verify equality
        var expected = ValueCondition.ParseDigest("91d2544ff57ccca3");
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void DigestProcessor_NullDigest()
    {
        // DigestProcessor should handle null responses
        var resp = "$-1\r\n";
        var processor = ResultProcessor.Digest;
        var result = Execute(resp, processor);
        Assert.Null(result);
    }
}
