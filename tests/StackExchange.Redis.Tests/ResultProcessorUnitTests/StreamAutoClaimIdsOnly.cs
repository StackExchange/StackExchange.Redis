using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class StreamAutoClaimIdsOnly(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // XAUTOCLAIM ... JUSTID, as the command sends it
    private static readonly IRespHandler<StreamAutoClaimIdsOnlyResult> Handler
        = GroupHandlers.Get<StreamAutoClaimIdsOnlyResult>(typeof(global::StackExchange.Redis.Streams), "StreamTypesHandler", "AutoClaimIdsOnly");

    [Fact]
    public void WithIds_ThreeElements_Success()
    {
        // XAUTOCLAIM mystream mygroup Alice 3600000 0-0 COUNT 25 JUSTID
        // 1) "0-0"
        // 2) 1) "1609338752495-0"
        //    2) "1609338752496-0"
        // 3) (empty array)
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*2\r\n" + // Array of 2 claimed IDs
                   "$15\r\n1609338752495-0\r\n" +
                   "$15\r\n1609338752496-0\r\n" +
                   "*0\r\n";  // Empty deleted IDs array

        var result = Execute(resp, Handler);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Equal(2, result.ClaimedIds.Length);
        Assert.Equal("1609338752495-0", result.ClaimedIds[0].ToString());
        Assert.Equal("1609338752496-0", result.ClaimedIds[1].ToString());
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void WithIds_TwoElements_OlderServer_Success()
    {
        // Older Redis 6.2 - only returns 2 elements (no deleted IDs)
        var resp = "*2\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*2\r\n" +
                   "$15\r\n1609338752495-0\r\n" +
                   "$15\r\n1609338752496-0\r\n";

        var result = Execute(resp, Handler);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Equal(2, result.ClaimedIds.Length);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void EmptyIds_Success()
    {
        // No IDs claimed
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*0\r\n" + // Empty claimed IDs array
                   "*0\r\n";  // Empty deleted IDs array

        var result = Execute(resp, Handler);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Empty(result.ClaimedIds);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void NullIds_Success()
    {
        // Null IDs array (alternative representation)
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "$-1\r\n" + // Null claimed IDs
                   "*0\r\n";

        var result = Execute(resp, Handler);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Empty(result.ClaimedIds);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void WithDeletedIds_Success()
    {
        // Some entries were deleted
        var resp = "*3\r\n" +
                   "$3\r\n0-0\r\n" +
                   "*1\r\n" + // 1 claimed ID
                   "$15\r\n1609338752495-0\r\n" +
                   "*2\r\n" + // 2 deleted IDs
                   "$15\r\n1609338752496-0\r\n" +
                   "$15\r\n1609338752497-0\r\n";

        var result = Execute(resp, Handler);

        Assert.Equal("0-0", result.NextStartId.ToString());
        Assert.Single(result.ClaimedIds);
        Assert.Equal("1609338752495-0", result.ClaimedIds[0].ToString());
        Assert.Equal(2, result.DeletedIds.Length);
        Assert.Equal("1609338752496-0", result.DeletedIds[0].ToString());
        Assert.Equal("1609338752497-0", result.DeletedIds[1].ToString());
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        // CHANGED: the old processor rejected this shape ("Unexpected response"); the new handler maps a reply
        // that is not the XAUTOCLAIM JUSTID shape to StreamAutoClaimIdsOnlyResult.Null. Pinned as found, and reported: it turns a protocol violation
        // into a plausible-looking result instead of a fault.
        var result = Execute(resp, Handler);

        Assert.True(result.IsNull);
    }

    [Fact]
    public void Null_Failure()
    {
        var resp = "$-1\r\n";

        // CHANGED: the old processor rejected this shape ("Unexpected response"); the new handler maps a reply
        // that is not the XAUTOCLAIM JUSTID shape to StreamAutoClaimIdsOnlyResult.Null. Pinned as found, and reported: it turns a protocol violation
        // into a plausible-looking result instead of a fault.
        var result = Execute(resp, Handler);

        Assert.True(result.IsNull);
    }
}
