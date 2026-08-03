using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class ListMoveMultiple(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void MovedElements_Success()
    {
        // LMOVEM src dst LEFT RIGHT COUNT 2 BULK => ["a", "b"]
        var resp = "*2\r\n$1\r\na\r\n$1\r\nb\r\n";

        var result = Execute(resp, ResultProcessor.NullableRedisValueArray);

        Assert.NotNull(result);
        Assert.Equal("a,b", Join(result));
    }

    [Fact]
    public void EmptyArray_IsEmptyNotNull()
    {
        // an empty array must stay an empty array, distinct from a null reply.
        var resp = "*0\r\n";

        var result = Execute(resp, ResultProcessor.NullableRedisValueArray);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NullArray_Resp2_IsNull()
    {
        // EXACTLY not satisfied: RESP2 null array.
        var resp = "*-1\r\n";

        var result = Execute(resp, ResultProcessor.NullableRedisValueArray);

        Assert.Null(result);
    }

    [Fact]
    public void Null_Resp3_IsNull()
    {
        // EXACTLY not satisfied: RESP3 null.
        var resp = "_\r\n";

        var result = Execute(resp, ResultProcessor.NullableRedisValueArray, protocol: RedisProtocol.Resp3);

        Assert.Null(result);
    }

    [Fact]
    public void Scalar_Failure()
    {
        // A bulk-string / scalar reply is not a valid LMOVEM response.
        var resp = "$5\r\nhello\r\n";

        ExecuteUnexpected(resp, ResultProcessor.NullableRedisValueArray);
    }

    [Fact]
    public void Integer_Failure()
    {
        var resp = ":5\r\n";

        ExecuteUnexpected(resp, ResultProcessor.NullableRedisValueArray);
    }
}
