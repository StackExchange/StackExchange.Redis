using System;
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
            using (var muxer = Create(allowAdmin:true,retryPolicy: RetryPolicy.Handle<RedisException>().AlwaysRetry()))
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
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync:false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            mockfailedCommand.Setup(failedCommand => failedCommand.IsEndpointAvailable()).Returns(true);

            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);

            Assert.True(commandRetryQueueManager.RetryQueueLength == 0);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageWaitOnEndPointUnAvailable()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Returns(false);


            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);

            Assert.True(commandRetryQueueManager.RetryQueueLength == 1);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
        }

        [Fact]
        public void TryHandleFailedMessageTimedoutEndPointAvailable()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            var timeout = new RedisTimeoutException("timedout", CommandStatus.Unknown);
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Returns(true);
            mockfailedCommand.Setup(failedCommand => failedCommand.HasTimedOut()).Returns(true);
            mockfailedCommand.Setup(failedCommand => failedCommand.GetTimeoutException()).Returns(timeout);


            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);

            Assert.True(commandRetryQueueManager.RetryQueueLength == 0);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
            mockfailedCommand.Verify(failedCommand => failedCommand.SetExceptionAndComplete(timeout), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageGetEndpointThrows()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            var ex = new Exception("failedendpoint");
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Throws(ex);

            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);

            Assert.True(commandRetryQueueManager.RetryQueueLength == 0);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
            mockfailedCommand.Verify(failedCommand => failedCommand.SetExceptionAndComplete(ex), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageDrainsQueue()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Returns(false);

            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);
            commandRetryQueueManager.Dispose();

            Assert.True(commandRetryQueueManager.RetryQueueLength == 0);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
            mockfailedCommand.Verify(failedCommand => failedCommand.SetExceptionAndComplete(It.IsAny<Exception>()), Times.Once);
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 1)]
        public void CheckRetryForTimeoutTimesout(bool hasTimedout, int queueLength)
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Returns(false);
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.HasTimedOut()).Returns(hasTimedout);

            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);
            commandRetryQueueManager.CheckRetryQueueForTimeouts();

            Assert.Equal(queueLength, commandRetryQueueManager.RetryQueueLength);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
            mockfailedCommand.Verify(failedCommand => failedCommand.SetExceptionAndComplete(It.IsAny<Exception>()), Times.Exactly(hasTimedout ? 1 : 0));
        }

        [Fact]
        public void TryHandleFailedMessageTimeoutThrow()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(runRetryLoopAsync: false);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockfailedCommand = new Mock<IInternalFailedCommand>();
            var ex = new Exception();
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable()).Returns(true);
            mockfailedCommand.Setup(mockfailedCommand => mockfailedCommand.HasTimedOut()).Throws(ex);

            commandRetryQueueManager.TryHandleFailedCommand(mockfailedCommand.Object);

            Assert.True(commandRetryQueueManager.RetryQueueLength == 0);
            mockfailedCommand.Verify(failedCommand => failedCommand.TryResendAsync(), Times.Never);
            mockfailedCommand.Verify(failedCommand => failedCommand.SetExceptionAndComplete(It.IsAny<Exception>()), Times.Once);
        }
    }
}
