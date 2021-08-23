using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class ConnectionMultiplexerRetryTests : TestBase
    {
        public ConnectionMultiplexerRetryTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(CommandFlags.AlwaysRetry, true)]
        [InlineData(CommandFlags.NoRetry, false)]
        [InlineData(CommandFlags.RetryIfNotSent, true)]
        public void ValidateOverrideFlag(CommandFlags flag, bool shouldRetry)
        {
            using var muxer = Create();
            var message = Message.Create(0, flag, RedisCommand.GET);
            message.ResetStatusToWaitingToBeSent();
            DefaultCommandRetryPolicy retryOnReconnect = new DefaultCommandRetryPolicy(muxer as ConnectionMultiplexer, c => true);
            Assert.Equal(shouldRetry, retryOnReconnect.CanRetry(message, new RedisConnectionException(ConnectionFailureType.SocketClosed, "test")));
        }

        [Theory]
        [InlineData(CommandFlags.AlwaysRetry, false)]
        [InlineData(CommandFlags.NoRetry, false)]
        [InlineData(CommandFlags.RetryIfNotSent, false)]
        public void ValidateOverrideFlagWithIsAdmin(CommandFlags flag, bool shouldRetry)
        {
            using var muxer = Create();
            var message = Message.Create(0, flag, RedisCommand.FLUSHDB);
            message.ResetStatusToWaitingToBeSent();
            DefaultCommandRetryPolicy retryOnReconnect = new DefaultCommandRetryPolicy(muxer as ConnectionMultiplexer, c => true);
            Assert.Equal(shouldRetry, retryOnReconnect.CanRetry(message, new RedisConnectionException(ConnectionFailureType.SocketClosed, "test")));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ValidateRetryIfNotSentOverrideFlag(bool alreadySent, bool shouldRetry)
        {
            using var muxer = Create();
            var message = Message.Create(0, CommandFlags.RetryIfNotSent, RedisCommand.GET);
            if (alreadySent)
            {
                message.SetRequestSent();
            }
            else
            {
                message.ResetStatusToWaitingToBeSent();
            }
            DefaultCommandRetryPolicy retryOnReconnect = new DefaultCommandRetryPolicy(muxer as ConnectionMultiplexer, c => true);
            Assert.Equal(shouldRetry, retryOnReconnect.CanRetry(message, new RedisConnectionException(ConnectionFailureType.SocketClosed, "test")));
        }

    }
}
