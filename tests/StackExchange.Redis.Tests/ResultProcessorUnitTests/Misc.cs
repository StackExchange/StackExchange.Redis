using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class Misc(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // single-id XACKDEL: the new core always reads the reply as an array (IDatabase takes element [0]),
    // so each per-id outcome is read by the element projection the array handler uses
    [Theory]
    [InlineData(":1\r\n", StreamTrimResult.Deleted)] // Integer 1
    [InlineData(":-1\r\n", StreamTrimResult.NotFound)] // Integer -1
    [InlineData(":2\r\n", StreamTrimResult.NotDeleted)] // Integer 2
    [InlineData("+1\r\n", StreamTrimResult.Deleted)] // Simple string "1"
    [InlineData("$1\r\n1\r\n", StreamTrimResult.Deleted)] // Bulk string "1"
    public void Int32EnumProcessor_StreamTrimResult(string resp, StreamTrimResult expected)
    {
        var result = Execute(resp, new ElementHandler<StreamTrimResult>(RespHandlers.Elements.TrimResult));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Int32EnumProcessor_StreamTrimResult_UnitArray()
    {
        // Unit array with integer 1 - the shape a single-id XACKDEL actually replies with
        var result = Execute("*1\r\n:1\r\n", RespHandlers.Inbuilt<StreamTrimResult[]>.Require());
        Assert.Equal(StreamTrimResult.Deleted, Assert.Single(result));
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_EmptyArray()
    {
        var resp = "*0\r\n";
        var processor = RespHandlers.Inbuilt<StreamTrimResult[]>.Require();
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_NullArray()
    {
        // a nil aggregate now reads as empty rather than null: every caller of an array reply wants to iterate it
        var resp = "*-1\r\n";
        var processor = RespHandlers.Inbuilt<StreamTrimResult[]>.Require();
        var result = Execute(resp, processor);
        Assert.Empty(result);
    }

    [Fact]
    public void Int32EnumArrayProcessor_StreamTrimResult_MultipleValues()
    {
        // Array with 3 elements: [1, -1, 2]
        var resp = "*3\r\n:1\r\n:-1\r\n:2\r\n";
        var processor = RespHandlers.Inbuilt<StreamTrimResult[]>.Require();
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(StreamTrimResult.Deleted, result[0]);
        Assert.Equal(StreamTrimResult.NotFound, result[1]);
        Assert.Equal(StreamTrimResult.NotDeleted, result[2]);
    }

    // ConnectionIdentityProcessor is gone with nothing to retarget at: IdentifyEndpoint is now answered by
    // routing alone, without sending a command, so there is no reply to parse.

    [Fact]
    public void DigestProcessor_ValidDigest()
    {
        // DigestProcessor reads a scalar string containing a hex digest
        // Example: XXh3 digest of "asdfasd" is "91d2544ff57ccca3"
        var resp = "$16\r\n91d2544ff57ccca3\r\n";
        var processor = RespHandlers.Inbuilt<ValueCondition?>.Require();
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
        var processor = RespHandlers.Inbuilt<ValueCondition?>.Require();
        var result = Execute(resp, processor);
        Assert.Null(result);
    }

    private sealed class ElementHandler<T>(RespReader.Projection<T> projection) : IRespHandler<T>
    {
        public T Parse(ref RespReader reader) => projection(ref reader);
    }
}
