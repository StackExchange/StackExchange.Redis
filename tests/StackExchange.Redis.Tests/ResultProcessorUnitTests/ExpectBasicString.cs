using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class ExpectBasicString(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("+OK\r\n", true)]
    [InlineData("$2\r\nOK\r\n", true)]
    public void DemandOK_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.DemandOK));

    [Theory]
    [InlineData("+PONG\r\n", true)]
    [InlineData("$4\r\nPONG\r\n", true)]
    public void DemandPONG_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.DemandPONG));

    [Theory]
    [InlineData("+FAIL\r\n")]
    [InlineData("$4\r\nFAIL\r\n")]
    [InlineData(":1\r\n")]
    public void DemandOK_Failure(string resp) => Assert.False(TryExecute(resp, ResultProcessor.DemandOK, out _, out _));

    [Theory]
    [InlineData("+FAIL\r\n")]
    [InlineData("$4\r\nFAIL\r\n")]
    [InlineData(":1\r\n")]
    public void DemandPONG_Failure(string resp) => Assert.False(TryExecute(resp, ResultProcessor.DemandPONG, out _, out _));

    [Theory]
    [InlineData("+Background saving started\r\n", true)]
    [InlineData("$25\r\nBackground saving started\r\n", true)]
    [InlineData("+Background saving started by parent\r\n", true)]
    public void BackgroundSaveStarted_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.BackgroundSaveStarted));

    [Theory]
    [InlineData("+Background append only file rewriting started\r\n", true)]
    [InlineData("$45\r\nBackground append only file rewriting started\r\n", true)]
    public void BackgroundSaveAOFStarted_Success(string resp, bool expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.BackgroundSaveAOFStarted));

    // Case sensitivity tests - these demonstrate that the new implementation is case-sensitive
    // The old CommandBytes implementation was case-insensitive (stored uppercase)
    [Theory]
    [InlineData("+ok\r\n")] // lowercase
    [InlineData("+Ok\r\n")] // mixed case
    [InlineData("$2\r\nok\r\n")] // lowercase bulk string
    public void DemandOK_CaseSensitive_Failure(string resp) => Assert.False(TryExecute(resp, ResultProcessor.DemandOK, out _, out _));

    [Theory]
    [InlineData("+pong\r\n")] // lowercase
    [InlineData("+Pong\r\n")] // mixed case
    [InlineData("$4\r\npong\r\n")] // lowercase bulk string
    public void DemandPONG_CaseSensitive_Failure(string resp) => Assert.False(TryExecute(resp, ResultProcessor.DemandPONG, out _, out _));

    [Theory]
    [InlineData("+background saving started\r\n")] // lowercase
    [InlineData("+BACKGROUND SAVING STARTED\r\n")] // uppercase
    public void BackgroundSaveStarted_CaseSensitive_Failure(string resp) => Assert.False(TryExecute(resp, ResultProcessor.BackgroundSaveStarted, out _, out _));
}
