using System;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.CommandRetry
{
    [Collection(SharedConnectionFixture.Key)]
    public class MessageRetryQueueTests : TestBase
    {
        public MessageRetryQueueTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        private void GetMock(out Moq.Mock<IMessageRetryHelper> mockmessageRetryHelper, out MessageRetryQueue messageRetryQueue, out Message message)
        {
            message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            mockmessageRetryHelper = new Mock<IMessageRetryHelper>();
            messageRetryQueue = new MessageRetryQueue(mockmessageRetryHelper.Object, runRetryLoopAsync: false);
        }

        [Fact]
        public void ValidateMaxQueueLengthFails()
        {
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var mockmessageRetryHelper = new Mock<IMessageRetryHelper>();
            var messageRetryQueue = new MessageRetryQueue(mockmessageRetryHelper.Object, maxRetryQueueLength:1, runRetryLoopAsync: false);

            var isEnqueuedWithZeroMaxLength = messageRetryQueue.TryHandleFailedCommand(message);
            messageRetryQueue.TryHandleFailedCommand(message);
            Assert.False(isEnqueuedWithZeroMaxLength);
        }

        [Fact]
        public async void RetryMessageSucceeds()
        {
            using (var muxer = Create(allowAdmin:true,retryPolicy: new CommandRetryPolicy().AlwaysRetryOnConnectionException()))
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
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(failedCommand => failedCommand.IsEndpointAvailable(message)).Returns(true);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.True(messageRetryQueue.RetryQueueLength == 0);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageWaitOnEndPointUnAvailable()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(false);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.True(messageRetryQueue.RetryQueueLength == 1);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
        }

        [Fact]
        public void TryHandleFailedMessageTimedoutEndPointAvailable()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);

            var timeout = new RedisTimeoutException("timedout", CommandStatus.Unknown);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(true);
            messageRetryHelper.Setup(failedCommand => failedCommand.HasTimedOut(message)).Returns(true);
            messageRetryHelper.Setup(failedCommand => failedCommand.GetTimeoutException(message)).Returns(timeout);


            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.True(messageRetryQueue.RetryQueueLength == 0);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message,timeout), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageGetEndpointThrows()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            var ex = new Exception("failedendpoint");
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Throws(ex);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.True(messageRetryQueue.RetryQueueLength == 0);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message,ex), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageDrainsQueue()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(false);

            messageRetryQueue.TryHandleFailedCommand(message);
            messageRetryQueue.Dispose();

            Assert.True(messageRetryQueue.RetryQueueLength == 0);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, It.IsAny<Exception>()), Times.Once);
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 1)]
        public void CheckRetryForTimeoutTimesout(bool hasTimedout, int queueLength)
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(false);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.HasTimedOut(message)).Returns(hasTimedout);

            messageRetryQueue.TryHandleFailedCommand(message);
            messageRetryQueue.CheckRetryQueueForTimeouts();

            Assert.Equal(queueLength, messageRetryQueue.RetryQueueLength);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message,It.IsAny<Exception>()), Times.Exactly(hasTimedout ? 1 : 0));
        }

        [Fact]
        public void TryHandleFailedMessageTimeoutThrow()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            var ex = new Exception();
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(true);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.HasTimedOut(message)).Throws(ex);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.True(messageRetryQueue.RetryQueueLength == 0);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, It.IsAny<Exception>()), Times.Once);
        }
    }
}
