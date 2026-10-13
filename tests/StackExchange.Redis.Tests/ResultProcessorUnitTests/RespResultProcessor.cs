using System.Text;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for the <see cref="StackExchange.Redis.RespResult"/> result processor - the raw, low-allocation
/// counterpart to <see cref="RedisResult"/> used by <c>ExecLease</c>/<c>ScriptEvalLease</c> and friends.
/// </summary>
public class RespResultProcessor(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("$5\r\nhello\r\n", RespPrefix.BulkString, "hello")]
    [InlineData("+world\r\n", RespPrefix.SimpleString, "world")]
    [InlineData(":42\r\n", RespPrefix.Integer, "42")]
    public void ScalarReply_CapturesRawFrameAndDecodes(string resp, RespPrefix expectedPrefix, string expectedText)
    {
        using var result = ParseResult(resp);

        Assert.NotNull(result);
        Assert.Equal(expectedPrefix, result.Prefix);
        Assert.False(result.IsNull);

        // decode via ReadRedisValue()
        Assert.Equal(expectedText, (string?)result.ReadScalar().ReadRedisValue());

        // decode via ReadLease()
        using var lease = result.ReadScalar().ReadLease();
        Assert.Equal(expectedText, Encoding.UTF8.GetString(lease!.Span));

        // decode via a caller-supplied buffer (RespReader.CopyTo)
        var reader = result.ReadScalar();
        byte[] buffer = new byte[reader.ScalarLength()];
        var copied = reader.CopyTo(buffer);
        Assert.Equal(expectedText, Encoding.UTF8.GetString(buffer, 0, copied));
    }

    [Theory]
    [InlineData("$-1\r\n", RespPrefix.BulkString)] // RESP2 null bulk string
    [InlineData("*-1\r\n", RespPrefix.Array)] // RESP2 null array
    [InlineData("_\r\n", RespPrefix.Null)] // RESP3 unified null
    public void NullReply_UsesSharedSingleton(string resp, RespPrefix expectedPrefix)
    {
        var first = ParseResult(resp);
        var second = ParseResult(resp);

        Assert.NotNull(first);
        Assert.True(first!.IsNull);
        Assert.Equal(expectedPrefix, first.Prefix);

        // the three null shapes are shared, never-disposed singletons - never a fresh allocation
        Assert.Same(first, second);

        // disposing a singleton must be a complete no-op, even repeatedly
        first.Dispose();
        first.Dispose();
        Assert.False(first.IsNull is false); // still usable afterwards
        Assert.Equal(expectedPrefix, first.Prefix);
    }

    [Fact]
    public void AggregateReply_SupportsTreeAccessButRejectsScalarAccessors()
    {
        using var result = ParseResult("*4\r\n:1\r\n:2\r\n$5\r\nthree\r\n*2\r\n:4\r\n:5\r\n");

        Assert.NotNull(result);
        Assert.Equal(RespPrefix.Array, result.Prefix);
        Assert.False(result.IsNull);

        var reader = result.Read();
        Assert.True(reader.IsAggregate);
        Assert.Equal(4, reader.AggregateLength());

        // scalar-only accessors must all fail the same, consistent way against a tree
        Assert.Throws<System.InvalidOperationException>(() => result.ReadScalar());
    }

    [Fact]
    public void AggregateReply_ReadRedisResultMaterializesFullTree()
    {
        using var result = ParseResult("*3\r\n:1\r\n:2\r\n$5\r\nthree\r\n");

        Assert.NotNull(result);
        var reader = result.Read();
        var redisResult = reader.ReadRedisResult();
        var values = (RedisValue[]?)redisResult;

        Assert.NotNull(values);
        Assert.Equal(3, values!.Length);
        Assert.Equal(1, (long)values[0]);
        Assert.Equal(2, (long)values[1]);
        Assert.Equal("three", (string?)values[2]);
    }

    [Fact]
    public void AggregateReply_AggregateChildren_WalksEachElement()
    {
        using var result = ParseResult("*3\r\n:1\r\n:2\r\n$5\r\nthree\r\n");

        var children = result!.Read().AggregateChildren();
        Assert.True(children.MoveNext());
        Assert.Equal(1, (long)children.Value.ReadRedisValue());
        Assert.True(children.MoveNext());
        Assert.Equal(2, (long)children.Value.ReadRedisValue());
        Assert.True(children.MoveNext());
        Assert.Equal("three", (string?)children.Value.ReadRedisValue());
        Assert.False(children.MoveNext());
    }

    [Fact]
    public void AggregateReply_AggregateChildren_DescendsIntoNestedSubArray()
    {
        using var result = ParseResult("*4\r\n:1\r\n:2\r\n$5\r\nthree\r\n*2\r\n:4\r\n:5\r\n");

        var children = result!.Read().AggregateChildren();
        Assert.True(children.MoveNext());
        Assert.Equal(1, (long)children.Value.ReadRedisValue());
        Assert.True(children.MoveNext());
        Assert.Equal(2, (long)children.Value.ReadRedisValue());
        Assert.True(children.MoveNext());
        Assert.Equal("three", (string?)children.Value.ReadRedisValue());

        Assert.True(children.MoveNext());
        Assert.True(children.Value.IsAggregate);
        var nested = children.Value.AggregateChildren();
        Assert.True(nested.MoveNext());
        Assert.Equal(4, (long)nested.Value.ReadRedisValue());
        Assert.True(nested.MoveNext());
        Assert.Equal(5, (long)nested.Value.ReadRedisValue());
        Assert.False(nested.MoveNext());

        Assert.False(children.MoveNext());
    }

    [Fact]
    public void AggregateReply_ReadPastArray_ProjectsTypedArray()
    {
        using var result = ParseResult("*3\r\n:1\r\n:2\r\n$5\r\nthree\r\n");

        var reader = result!.Read();
        RedisValue[]? values = reader.ReadPastArray(static (ref r) => r.ReadRedisValue(), scalar: true);

        Assert.NotNull(values);
        Assert.Equal(3, values!.Length);
        Assert.Equal(1, (long)values[0]);
        Assert.Equal(2, (long)values[1]);
        Assert.Equal("three", (string?)values[2]);
    }

    [Fact]
    public void NullArrayReply_ReadPastArray_ReturnsNull()
    {
        using var result = ParseResult("*-1\r\n");

        var reader = result!.Read();
        RedisValue[]? values = reader.ReadPastArray(static (ref r) => r.ReadRedisValue(), scalar: true);

        Assert.Null(values);
    }

    // ErrorReply_PropagatesAsRedisServerException is gone: an error reply never reaches this handler. The new
    // core converts it to RedisServerException when the frame arrives (RespPayloadOperation.ParseFrame),
    // before any handler is chosen, so there is no handler-level behaviour left to pin here.

    /// <summary>
    /// Parse exactly as the core does for this handler: it retains the frame, so it is parsed from the
    /// payload rather than from a positioned reader (which it refuses).
    /// </summary>
    private static RespResult ParseResult(string resp)
    {
        using var payload = RespPayload.Create(Encoding.UTF8.GetBytes(resp));
        return ((IRespPayloadHandler<RespResult>)RespHandlers.Result).Parse(payload);
    }
}
