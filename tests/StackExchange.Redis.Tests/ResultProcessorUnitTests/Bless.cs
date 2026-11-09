using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// <c>BLESS GET</c> replies with an array of active flag names; an unblessed key is an empty array, not nil.
/// </summary>
public class Bless(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("*0\r\n", BlessFlags.None)]
    [InlineData("*1\r\n$8\r\nNO-EVICT\r\n", BlessFlags.NoEvict)]
    [InlineData("~1\r\n$8\r\nNO-EVICT\r\n", BlessFlags.NoEvict)] // tolerate a RESP3 set
    [InlineData("*1\r\n+NO-EVICT\r\n", BlessFlags.NoEvict)]
    [InlineData("*1\r\n$6\r\nIN-RAM\r\n", BlessFlags.None)] // a flag from a newer server is ignored
    [InlineData("*2\r\n$6\r\nIN-RAM\r\n$8\r\nNO-EVICT\r\n", BlessFlags.NoEvict)]
    [InlineData("*1\r\n$8\r\nno-evict\r\n", BlessFlags.None)] // the server's token is upper-case; don't guess
    public void BlessFlags_Valid(string resp, BlessFlags expected)
        => Assert.Equal(expected, Execute(resp, ResultProcessor.BlessFlags));

    [Theory]
    [InlineData("*-1\r\n")]
    [InlineData("$-1\r\n")]
    [InlineData("$8\r\nNO-EVICT\r\n")]
    [InlineData(":1\r\n")]
    public void BlessFlags_Invalid(string resp) => ExecuteUnexpected(resp, ResultProcessor.BlessFlags);
}
