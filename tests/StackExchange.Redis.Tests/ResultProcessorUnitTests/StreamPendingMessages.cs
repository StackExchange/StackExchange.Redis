using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class StreamPendingMessages(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void SingleMessage_Success()
    {
        // XPENDING mystream group55 - + 10
        // 1) 1) 1526984818136-0
        //    2) "consumer-123"
        //    3) (integer) 196415
        //    4) (integer) 1
        var resp = "*1\r\n" + // Array of 1 message
                   "*4\r\n" + // Each message is an array of 4 elements
                   "$15\r\n1526984818136-0\r\n" + // Message ID
                   "$12\r\nconsumer-123\r\n" + // Consumer name
                   ":196415\r\n" + // Idle time in ms
                   ":1\r\n"; // Delivery count

        var result = Execute(resp, ResultProcessor.StreamPendingMessages);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("1526984818136-0", result[0].MessageId);
        Assert.Equal("consumer-123", result[0].ConsumerName);
        Assert.Equal(196415, result[0].IdleTimeInMilliseconds);
        Assert.Equal(1, result[0].DeliveryCount);
    }

    [Fact]
    public void MultipleMessages_Success()
    {
        // XPENDING mystream group55 - + 10
        // 1) 1) 1526984818136-0
        //    2) "consumer-123"
        //    3) (integer) 196415
        //    4) (integer) 1
        // 2) 1) 1526984818137-0
        //    2) "consumer-456"
        //    3) (integer) 5000
        //    4) (integer) 3
        var resp = "*2\r\n" + // Array of 2 messages
                   "*4\r\n" + // First message
                   "$15\r\n1526984818136-0\r\n" +
                   "$12\r\nconsumer-123\r\n" +
                   ":196415\r\n" +
                   ":1\r\n" +
                   "*4\r\n" + // Second message
                   "$15\r\n1526984818137-0\r\n" +
                   "$12\r\nconsumer-456\r\n" +
                   ":5000\r\n" +
                   ":3\r\n";

        var result = Execute(resp, ResultProcessor.StreamPendingMessages);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        Assert.Equal("1526984818136-0", result[0].MessageId);
        Assert.Equal("consumer-123", result[0].ConsumerName);
        Assert.Equal(196415, result[0].IdleTimeInMilliseconds);
        Assert.Equal(1, result[0].DeliveryCount);

        Assert.Equal("1526984818137-0", result[1].MessageId);
        Assert.Equal("consumer-456", result[1].ConsumerName);
        Assert.Equal(5000, result[1].IdleTimeInMilliseconds);
        Assert.Equal(3, result[1].DeliveryCount);
    }

    [Fact]
    public void EmptyArray_Success()
    {
        // No pending messages
        var resp = "*0\r\n";

        var result = Execute(resp, ResultProcessor.StreamPendingMessages);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamPendingMessages);
    }

    [Fact]
    public void Null_Failure()
    {
        var resp = "$-1\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamPendingMessages);
    }
}
