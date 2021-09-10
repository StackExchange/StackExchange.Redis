using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class CommandRetryPolicyTests : TestBase
    {
        public CommandRetryPolicyTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(CommandFlags.AlwaysRetry, true)]
        [InlineData(CommandFlags.NoRetry, false)]
        [InlineData(CommandFlags.RetryIfNotSent, true)]
        public void ValidateOverrideFlag(CommandFlags flag, bool shouldRetry)
        {
            using var muxer = Create(retryPolicy: mux => new DefaultCommandRetryPolicy(mux, c => true));
            var message = Message.Create(0, flag, RedisCommand.GET);
            message.ResetStatusToWaitingToBeSent();
            var ex = new RedisConnectionException(ConnectionFailureType.SocketClosed, "test");
            Assert.Equal(shouldRetry, muxer.RetryQueueIfEligible(message, CommandFailureReason.WriteFailure, ex));
        }

        [Theory]
        [InlineData(CommandFlags.AlwaysRetry, false)]
        [InlineData(CommandFlags.NoRetry, false)]
        [InlineData(CommandFlags.RetryIfNotSent, false)]
        public void ValidateOverrideFlagWithIsAdmin(CommandFlags flag, bool shouldRetry)
        {
            using var muxer = Create(retryPolicy: mux => new DefaultCommandRetryPolicy(mux, c => true));
            var message = Message.Create(0, flag, RedisCommand.FLUSHDB);
            message.ResetStatusToWaitingToBeSent();
            var ex = new RedisConnectionException(ConnectionFailureType.SocketClosed, "test");
            Assert.Equal(shouldRetry, muxer.RetryQueueIfEligible(message, CommandFailureReason.WriteFailure, ex));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ValidateRetryIfNotSentOverrideFlag(bool alreadySent, bool shouldRetry)
        {
            using var muxer = Create(retryPolicy: mux => new DefaultCommandRetryPolicy(mux, c => true));
            var message = Message.Create(0, CommandFlags.RetryIfNotSent, RedisCommand.GET);
            if (alreadySent)
            {
                message.SetRequestSent();
            }
            else
            {
                message.ResetStatusToWaitingToBeSent();
            }
            var ex = new RedisConnectionException(ConnectionFailureType.SocketClosed, "test");
            Assert.Equal(shouldRetry, muxer.RetryQueueIfEligible(message, CommandFailureReason.WriteFailure, ex));
        }

        [Fact]
        public void DefaultPolicy()
        {
            Assert.Equal(CommandRetryPolicy.IfNotSent, CommandRetryPolicy.Default);
        }

        [Fact]
        public void NeverPolicy()
        {
            var policy = CommandRetryPolicy.Never(null);

            var message = new TestMessage(CommandFlags.None, RedisCommand.GET);
            var failedCommand = new FailedCommand(message, CommandFailureReason.WriteFailure, new RedisException("test"));

            Assert.False(policy.TryQueue(failedCommand));
            message.SetRequestSent();
            Assert.False(policy.TryQueue(failedCommand));
        }

        [Fact]
        public void IfNotSentPolicy()
        {
            var policy = CommandRetryPolicy.IfNotSent(null);

            var message = new TestMessage(CommandFlags.None, RedisCommand.GET);
            var failedCommand = new FailedCommand(message, CommandFailureReason.WriteFailure, new RedisException("test"));

            Assert.True(policy.TryQueue(failedCommand));
            message.SetRequestSent();
            Assert.False(policy.TryQueue(failedCommand));
            // Just for good measure...
            message.ResetStatusToWaitingToBeSent();
            Assert.True(policy.TryQueue(failedCommand));
        }

        [Fact]
        public void MessageExclusions()
        {
            // Base eligibility
            var message = new TestMessage(CommandFlags.None, RedisCommand.GET);
            Assert.True(CommandRetryPolicy.IsEligible(message));

            // NoRetry excludes the command
            message = new TestMessage(CommandFlags.NoRetry, RedisCommand.GET);
            Assert.False(CommandRetryPolicy.IsEligible(message));

            // RetryIfNotSent should work
            message = new TestMessage(CommandFlags.RetryIfNotSent, RedisCommand.GET);
            Assert.True(CommandRetryPolicy.IsEligible(message));
            //...unless it's sent
            message.SetRequestSent();
            Assert.False(CommandRetryPolicy.IsEligible(message));

            // Admin commands are ineligible
            message = new TestMessage(CommandFlags.None, RedisCommand.KEYS);
            Assert.True(message.IsAdmin);
            Assert.False(CommandRetryPolicy.IsEligible(message));

            // Internal is ineligible
            message = new TestMessage(CommandFlags.None, RedisCommand.GET);
            message.SetInternalCall();
            Assert.False(CommandRetryPolicy.IsEligible(message));
        }

        [Fact]
        public void ExceptionExclusions()
        {
            // Sanity checking RedisException - all we look for
            var ex = new RedisException("Boom");
            Assert.True(CommandRetryPolicy.IsEligible(ex));

            // Other exceptions don't qualify
            var oex = new Exception("test");
            Assert.False(CommandRetryPolicy.IsEligible(oex));
        }

        [Fact]
        public void EndToEndExclusions()
        {
            using var muxer = Create(retryPolicy: CommandRetryPolicy.Always);
            var policy = (muxer as ConnectionMultiplexer).CommandRetryPolicy;

            var ex = new RedisException("test");
            var message = new TestMessage(CommandFlags.None, RedisCommand.GET);
            Assert.True(muxer.RetryQueueIfEligible(message, CommandFailureReason.WriteFailure, ex));

            message = new TestMessage(CommandFlags.NoRetry, RedisCommand.GET);
            Assert.False(muxer.RetryQueueIfEligible(message, CommandFailureReason.WriteFailure, ex));
        }

        private class TestMessage : Message
        {
            public TestMessage(CommandFlags flags, RedisCommand command) : base(0, flags, command) { }

            public override int ArgCount => 0;
            protected override void WriteImpl(PhysicalConnection physical) { }
        }
    }
}
