using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Single-key <c>DEL</c>/<c>UNLINK</c>/<c>TOUCH</c>, which used <c>DemandZeroOrOne</c>; the new core reads
/// them with the general <see cref="RespHandlers.Boolean"/>.
/// </summary>
public class DemandZeroOrOne(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData(":0\r\n", false)]
    [InlineData(":1\r\n", true)]
    public void ValidZeroOrOne_Success(string resp, bool expected)
    {
        var result = Execute(resp, RespHandlers.Boolean);
        Assert.Equal(expected, result);
    }

    // NEW BEHAVIOUR: "0"/"1" spelt as a simple or bulk string used to read as a boolean; RespReader.ReadBoolean
    // takes integers (and #t/#f, and OK) only. These commands reply with an integer, so no real reply changes.
    [Theory]
    [InlineData("+0\r\n")]
    [InlineData("+1\r\n")]
    [InlineData("$1\r\n0\r\n")]
    [InlineData("$1\r\n1\r\n")]
    public void StringZeroOrOne_Failure(string resp) => ExecuteUnexpected(resp, RespHandlers.Boolean);

    [Theory]
    [InlineData(":2\r\n")]
    [InlineData("*1\r\n:1\r\n")]
    public void InvalidResponse_Failure(string resp)
    {
        ExecuteUnexpected(resp, RespHandlers.Boolean);
    }

    // NEW BEHAVIOUR: the shared boolean handler reads +OK as yes and nil as no (the SET-family meanings),
    // where DemandZeroOrOne rejected both. DEL/UNLINK/TOUCH never reply with either, so this is leniency only.
    [Theory]
    [InlineData("+OK\r\n", true)]
    [InlineData("$-1\r\n", false)]
    public void OkOrNil_IsAccepted(string resp, bool expected) => Assert.Equal(expected, Execute(resp, RespHandlers.Boolean));
}
