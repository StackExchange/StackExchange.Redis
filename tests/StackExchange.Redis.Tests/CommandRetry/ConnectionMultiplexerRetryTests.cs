using Xunit;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class ConnectionMultiplexerRetryTests
    {
        [Theory]
        [InlineData(CommandFlags.AlwaysRetry, true)]
        [InlineData(CommandFlags.NoRetry, false)]
        [InlineData(CommandFlags.RetryIfNotSent, true)]
        public void ValidateOverrideFlag(CommandFlags flag, bool shouldRetry)
        {
            var message = Message.Create(0, flag, RedisCommand.GET);
            message.ResetStatusToWaitingToBeSent();
            Assert.Equal(shouldRetry, message.ShouldRetry());
        }
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ValidateRetryIfNotSentOverrideFlag(bool alreadySent, bool shouldRetry)
        {
            var message = Message.Create(0, CommandFlags.RetryIfNotSent, RedisCommand.GET);
            if (alreadySent)
            {
                message.SetRequestSent();
            }
            else
            {
                message.ResetStatusToWaitingToBeSent();
            }
            Assert.Equal(shouldRetry, message.ShouldRetry());
        }

    }
}
