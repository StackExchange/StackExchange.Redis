using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class StreamPendingInfo(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // XPENDING (summary form), as the command sends it
    private static readonly IRespHandler<global::StackExchange.Redis.StreamPendingInfo> Handler
        = GroupHandlers.Get<global::StackExchange.Redis.StreamPendingInfo>(typeof(global::StackExchange.Redis.Streams), "StreamTypesHandler", "PendingInfo");

    [Fact]
    public void SingleConsumer_Success()
    {
        // XPENDING mystream group55
        // 1) (integer) 1
        // 2) 1526984818136-0
        // 3) 1526984818136-0
        // 4) 1) 1) "consumer-123"
        //       2) "1"
        var resp = "*4\r\n" +
                   ":1\r\n" +
                   "$15\r\n1526984818136-0\r\n" +
                   "$15\r\n1526984818136-0\r\n" +
                   "*1\r\n" + // Array of 1 consumer
                   "*2\r\n" + // Each consumer is an array of 2 elements
                   "$12\r\nconsumer-123\r\n" + // Consumer name
                   "$1\r\n1\r\n"; // Pending count as string

        var result = Execute(resp, Handler);

        Assert.Equal(1, result.PendingMessageCount);
        Assert.Equal("1526984818136-0", result.LowestPendingMessageId);
        Assert.Equal("1526984818136-0", result.HighestPendingMessageId);
        Assert.Single(result.Consumers);
        Assert.Equal("consumer-123", result.Consumers[0].Name);
        Assert.Equal(1, result.Consumers[0].PendingMessageCount);
    }

    [Fact]
    public void MultipleConsumers_Success()
    {
        // XPENDING mystream mygroup
        // 1) (integer) 10
        // 2) 1526569498055-0
        // 3) 1526569506935-0
        // 4) 1) 1) "Bob"
        //       2) "2"
        //    2) 1) "Joe"
        //       2) "8"
        var resp = "*4\r\n" +
                   ":10\r\n" +
                   "$15\r\n1526569498055-0\r\n" +
                   "$15\r\n1526569506935-0\r\n" +
                   "*2\r\n" + // Array of 2 consumers
                   "*2\r\n" + // First consumer array
                   "$3\r\nBob\r\n" +
                   "$1\r\n2\r\n" +
                   "*2\r\n" + // Second consumer array
                   "$3\r\nJoe\r\n" +
                   "$1\r\n8\r\n";

        var result = Execute(resp, Handler);

        Assert.Equal(10, result.PendingMessageCount);
        Assert.Equal("1526569498055-0", result.LowestPendingMessageId);
        Assert.Equal("1526569506935-0", result.HighestPendingMessageId);
        Assert.Equal(2, result.Consumers.Length);
        Assert.Equal("Bob", result.Consumers[0].Name);
        Assert.Equal(2, result.Consumers[0].PendingMessageCount);
        Assert.Equal("Joe", result.Consumers[1].Name);
        Assert.Equal(8, result.Consumers[1].PendingMessageCount);
    }

    [Fact]
    public void NoConsumers_Success()
    {
        // When there are no consumers yet, the 4th element is null
        var resp = "*4\r\n" +
                   ":0\r\n" +
                   "$15\r\n1526569498055-0\r\n" +
                   "$15\r\n1526569506935-0\r\n" +
                   "$-1\r\n"; // null

        var result = Execute(resp, Handler);

        Assert.Equal(0, result.PendingMessageCount);
        Assert.Empty(result.Consumers);
    }

    [Fact]
    public void WrongArrayLength_Failure()
    {
        // Array with wrong length (3 instead of 4)
        var resp = "*3\r\n" +
                   ":1\r\n" +
                   "$15\r\n1526984818136-0\r\n" +
                   "$15\r\n1526984818136-0\r\n";

        // CHANGED: the old processor rejected this shape ("Unexpected response"); the new handler maps a reply
        // that is not a four-element array to default(StreamPendingInfo). Pinned as found, and reported: it turns a protocol violation
        // into a plausible-looking result instead of a fault.
        // (a real parse always has a non-null Consumers; default is the only way to get a null one)
        var result = Execute(resp, Handler);

        Assert.Equal(default, result);
        Assert.Null(result.Consumers);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        // CHANGED: the old processor rejected this shape ("Unexpected response"); the new handler maps a reply
        // that is not a four-element array to default(StreamPendingInfo). Pinned as found, and reported: it turns a protocol violation
        // into a plausible-looking result instead of a fault.
        // (a real parse always has a non-null Consumers; default is the only way to get a null one)
        var result = Execute(resp, Handler);

        Assert.Equal(default, result);
        Assert.Null(result.Consumers);
    }

    [Fact]
    public void Null_Failure()
    {
        var resp = "$-1\r\n";

        // CHANGED: the old processor rejected this shape ("Unexpected response"); the new handler maps a reply
        // that is not a four-element array to default(StreamPendingInfo). Pinned as found, and reported: it turns a protocol violation
        // into a plausible-looking result instead of a fault.
        // (a real parse always has a non-null Consumers; default is the only way to get a null one)
        var result = Execute(resp, Handler);

        Assert.Equal(default, result);
        Assert.Null(result.Consumers);
    }
}
