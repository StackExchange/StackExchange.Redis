using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RountTripUnitTests;

public class EchoRoundTrip
{
    [Theory(Timeout = 1000)]
    [InlineData("hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n", "+hello\r\n")]
    [InlineData("hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n", "$5\r\nhello\r\n")]
    public async Task EchoRoundTripTest(string payload, string requestResp, string responseResp)
    {
        var msg = Message.Create(-1, CommandFlags.None, RedisCommand.ECHO, (RedisValue)payload);
        var result = await TestConnection.Test(msg, ResultProcessor.String, requestResp, responseResp);
        Assert.Equal(payload, result);
    }
}
