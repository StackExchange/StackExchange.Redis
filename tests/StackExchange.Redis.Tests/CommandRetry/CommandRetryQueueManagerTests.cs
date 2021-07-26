using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.CommandRetry
{
    [Collection(SharedConnectionFixture.Key)]
    public class CommandRetryQueueManagerTests : TestBase
    {
        public CommandRetryQueueManagerTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public void ValidateMaxQueueLengthFails()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(maxRetryQueueLength: 0);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var messageFailedException = new Exception();

            var isEnqueuedWithZeroMaxLength = commandRetryQueueManager.TryHandleFailedCommand(new FailedCommand(message, mux, messageFailedException));
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);
            Assert.False(isEnqueuedWithZeroMaxLength);
        }

        [Fact]
        public async void RetryMessageSucceeds()
        {
            using (var muxer = Create(allowAdmin:true,retryPolicy: Policy.Handle<RedisException>().AlwaysRetry()))
            {
                var conn = muxer.GetDatabase();
                var duration = await conn.PingAsync().ForAwait();
                Log("Ping took: " + duration);
                Assert.True(duration.TotalMilliseconds > 0);
            }
        }

        [Fact]
        public void TryHandleFailedMessageSucceedsOnEndPointAvailable()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(maxRetryQueueLength: 0);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();

            Assert.True(commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object));

        }
    }
}
