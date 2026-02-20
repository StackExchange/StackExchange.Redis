using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class StreamAutoClaim(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void WithEntries_ThreeElements_Success()
    {
        // XAUTOCLAIM mystream mygroup Alice 3600000 0-0 COUNT 25
        // 1) "0-0"
        // 2) 1) 1) "1609338752495-0"
        //       2) 1) "field"
        //          2) "value"
        // 3) (empty array)
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*1\r\n" + // Array of 1 entry
                   "*2\r\n" + // Entry: [id, fields]
                   "$15\r\n1609338752495-0\r\n" +
                   "*2\r\n" + // Fields array
                   "$5\r\nfield\r\n" +
                   "$5\r\nvalue\r\n" +
                   "*0\r\n";  // Empty deleted IDs array

        var result = Execute(resp, ResultProcessor.StreamAutoClaim);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Single(result.ClaimedEntries);
        Assert.Equal("1609338752495-0", result.ClaimedEntries[0].Id.ToString());
        Assert.Equal("value", result.ClaimedEntries[0]["field"]);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void WithEntries_TwoElements_OlderServer_Success()
    {
        // Older Redis 6.2 - only returns 2 elements (no deleted IDs)
        var resp = "*2\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*1\r\n" +
                   "*2\r\n" +
                   "$15\r\n1609338752495-0\r\n" +
                   "*2\r\n" +
                   "$5\r\nfield\r\n" +
                   "$5\r\nvalue\r\n";

        var result = Execute(resp, ResultProcessor.StreamAutoClaim);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Single(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void EmptyEntries_Success()
    {
        // No entries claimed
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*0\r\n" + // Empty entries array
                   "*0\r\n";  // Empty deleted IDs array

        var result = Execute(resp, ResultProcessor.StreamAutoClaim);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void NullEntries_Success()
    {
        // Null entries array (alternative representation)
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "$-1\r\n" + // Null entries
                   "*0\r\n";

        var result = Execute(resp, ResultProcessor.StreamAutoClaim);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void WithDeletedIds_Success()
    {
        // Some entries were deleted
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*0\r\n" + // No claimed entries
                   "*2\r\n" + // 2 deleted IDs
                   "$15\r\n1609338752495-0\r\n" +
                   "$15\r\n1609338752496-0\r\n";

        var result = Execute(resp, ResultProcessor.StreamAutoClaim);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Empty(result.ClaimedEntries);
        Assert.Equal(2, result.DeletedIds.Length);
        Assert.Equal("1609338752495-0", result.DeletedIds[0].ToString());
        Assert.Equal("1609338752496-0", result.DeletedIds[1].ToString());
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamAutoClaim);
    }

    [Fact]
    public void Null_Failure()
    {
        var resp = "$-1\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamAutoClaim);
    }
}
