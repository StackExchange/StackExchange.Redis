using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Subscription confirmations, as the shipped <c>TrackSubscriptionsProcessor</c> read them.
/// </summary>
/// <remarks>
/// The shipped processor stored the count on the <c>PhysicalConnection</c> and added/removed the endpoint
/// on the <c>Subscription</c>, answering only <see langword="true"/>. The new core's
/// <c>PubSub.SubscriptionConfirmationHandler</c> answers the count itself, and the bookkeeping is done by
/// whoever awaited it - so the count, which these tests could not previously see, is now asserted.
/// </remarks>
public class TrackSubscriptions(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static IRespHandler<long> Confirmation
        => PrivateHandlers.Get<long>(typeof(PubSub), "SubscriptionConfirmationHandler");

    [Theory]
    [InlineData("*3\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n:1\r\n", 1)] // SUBSCRIBE response with count 1
    [InlineData("*3\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n:5\r\n", 5)] // SUBSCRIBE response with count 5
    [InlineData("*3\r\n$11\r\nunsubscribe\r\n$7\r\nchannel\r\n:0\r\n", 0)] // UNSUBSCRIBE response with count 0
    [InlineData("*3\r\n$10\r\npsubscribe\r\n$8\r\npattern*\r\n:2\r\n", 2)] // PSUBSCRIBE response with count 2
    [InlineData(">3\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n:1\r\n", 1)] // RESP3: the confirmation is a push
    [InlineData("*3\r\n$11\r\nunsubscribe\r\n$-1\r\n:0\r\n", 0)] // empty UNSUBSCRIBE: nil channel
    public void TrackSubscriptions_Success(string resp, int expectedCount)
    {
        var result = Execute(resp, Confirmation);
        Assert.Equal(expectedCount, result);
    }

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("*2\r\n$9\r\nsubscribe\r\n$7\r\nchannel\r\n")]
    public void TrackSubscriptions_Unexpected(string resp)
        => ExecuteUnexpected(resp, Confirmation);
}
