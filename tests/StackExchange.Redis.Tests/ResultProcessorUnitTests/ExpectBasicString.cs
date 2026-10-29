using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// The commands that answered <c>+OK</c> (<c>LSET</c>, <c>LTRIM</c>, <c>RESTORE</c>, <c>PFMERGE</c>, ...)
/// now read their reply with <see cref="RespHandlers.Success"/>, and <c>BGSAVE</c>/<c>BGREWRITEAOF</c>
/// with the diagnostics group's save-started handler.
/// </summary>
/// <remarks>
/// <c>DemandPONG</c> has no counterpart: the shipped core sent a <c>PING</c> for an empty transaction and
/// demanded <c>PONG</c> back, whereas the new transaction completes an empty body without sending anything.
/// Its tests were removed with it.
/// </remarks>
public class ExpectBasicString(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("+OK\r\n", true)]
    [InlineData("$2\r\nOK\r\n", true)]
    public void DemandOK_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, RespHandlers.Success));

    // DemandOK rejected any non-error reply other than OK; Success accepts every non-error reply, because a
    // failed command is reported by the server as an error frame (which still throws - see below), and
    // no command in this family answers anything but OK on success. So these are accepted now.
    [Theory]
    [InlineData("+FAIL\r\n")]
    [InlineData("$4\r\nFAIL\r\n")]
    [InlineData(":1\r\n")]
    [InlineData("+ok\r\n")] // lowercase
    [InlineData("+Ok\r\n")] // mixed case
    [InlineData("$2\r\nok\r\n")] // lowercase bulk string
    public void DemandOK_NonOkReply_IsAccepted(string resp) => Assert.True(Execute(resp, RespHandlers.Success));

    [Fact]
    public void DemandOK_ErrorReply_Fails() => ExecuteUnexpected("-ERR no such key\r\n", RespHandlers.Success);

    [Theory]
    [InlineData("+Background saving started\r\n", true)]
    [InlineData("$25\r\nBackground saving started\r\n", true)]
    [InlineData("+Background saving started by parent\r\n", true)]
    public void BackgroundSaveStarted_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, Diagnostics.SaveStartedHandler.Rdb));

    [Theory]
    [InlineData("+Background append only file rewriting started\r\n", true)]
    [InlineData("$45\r\nBackground append only file rewriting started\r\n", true)]
    public void BackgroundSaveAOFStarted_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, Diagnostics.SaveStartedHandler.Aof));

    // Case sensitivity tests - these demonstrate that the implementation is case-sensitive
    // The old CommandBytes implementation was case-insensitive (stored uppercase)
    [Theory]
    [InlineData("+background saving started\r\n")] // lowercase
    [InlineData("+BACKGROUND SAVING STARTED\r\n")] // uppercase
    public void BackgroundSaveStarted_CaseSensitive_Failure(string resp) => Assert.False(TryExecute(resp, Diagnostics.SaveStartedHandler.Rdb, out _, out _));
}
