using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class EchoRoundTrip(ITestOutputHelper log)
{
    [Theory(Timeout = 5000)]
    [InlineData("hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n", "+hello\r\n")]
    [InlineData("hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n", "$5\r\nhello\r\n")]
    public async Task EchoRoundTripTest(string payload, string requestResp, string responseResp)
    {
        // ECHO is a server command on the new surface, so it goes through the server context's diagnostics
        var executor = new RoundTripExecutor(responseResp);
        var server = new RespServerContext(new RespContext().WithExecutor(executor));

        var result = await server.Diagnostics.EchoAsync(payload);

        RoundTrip.AssertSent(executor, log, requestResp);
        Assert.Equal(payload, (string?)result);
    }
}
