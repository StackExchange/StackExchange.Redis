using System;
using System.Threading.Tasks;
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
            mockmessageRetryHelper = new Mock<IMessageRetryHelper>();
            messageRetryQueue = new MessageRetryQueue(mockmessageRetryHelper.Object);
        }

        [Fact]
        public void ValidateMaxQueueLengthFails()
        {
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mockmessageRetryHelper = new Mock<IMessageRetryHelper>();
            var messageRetryQueue = new MessageRetryQueue(mockmessageRetryHelper.Object, maxRetryQueueLength: 0);

            var isEnqueuedWithZeroMaxLength = messageRetryQueue.TryHandleFailedCommand(message);
            messageRetryQueue.TryHandleFailedCommand(message);
            Assert.False(isEnqueuedWithZeroMaxLength);
        }

        [Fact]
        public async Task RetryMessageSucceeds()
        {
            using (var muxer = Create(allowAdmin: true, retryPolicy: CommandRetryPolicy.Always))
            {
                var conn = muxer.GetDatabase();
                var duration = await conn.PingAsync().ForAwait();
                Log("Ping took: " + duration);
                Assert.True(duration.TotalMilliseconds > 0);
            }
        }

        [Fact]
        public async Task TryHandleFailedMessageSucceedsOnEndPointAvailable()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(failedCommand => failedCommand.IsEndpointAvailable(message)).Returns(true);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.Equal(1, messageRetryQueue.CurrentRetryQueueLength);
            await messageRetryQueue.ProcessRetryQueueAsync();
            Assert.Equal(0, messageRetryQueue.CurrentRetryQueueLength);

            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageWaitOnEndPointUnAvailable()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(false);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.Equal(1, messageRetryQueue.CurrentRetryQueueLength);
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

            Assert.Equal(1, messageRetryQueue.CurrentRetryQueueLength);
            messageRetryQueue.CheckRetryQueueForTimeouts();
            Assert.Equal(0, messageRetryQueue.CurrentRetryQueueLength);

            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, timeout), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageGetEndpointThrows()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            var ex = new Exception("failedendpoint");
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Throws(ex);

            messageRetryQueue.TryHandleFailedCommand(message);
            Assert.Equal(1, messageRetryQueue.CurrentRetryQueueLength);

            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, ex), Times.Once);
        }

        [Fact]
        public void TryHandleFailedMessageDrainsQueue()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(false);

            messageRetryQueue.TryHandleFailedCommand(message);
            messageRetryQueue.Dispose();

            Assert.Equal(0, messageRetryQueue.CurrentRetryQueueLength);
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

            Assert.Equal(queueLength, messageRetryQueue.CurrentRetryQueueLength);
            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, It.IsAny<Exception>()), Times.Exactly(hasTimedout ? 1 : 0));
        }

        [Fact]
        public async void TryHandleFailedMessageTimeoutThrow()
        {
            GetMock(out var messageRetryHelper, out var messageRetryQueue, out var message);
            var ex = new Exception();
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.IsEndpointAvailable(message)).Returns(true);
            messageRetryHelper.Setup(mockfailedCommand => mockfailedCommand.HasTimedOut(message)).Throws(ex);

            messageRetryQueue.TryHandleFailedCommand(message);

            Assert.Equal(1, messageRetryQueue.CurrentRetryQueueLength);
            await messageRetryQueue.ProcessRetryQueueAsync();
            Assert.Equal(0, messageRetryQueue.CurrentRetryQueueLength);

            messageRetryHelper.Verify(failedCommand => failedCommand.TryResendAsync(message), Times.Never);
            messageRetryHelper.Verify(failedCommand => failedCommand.SetExceptionAndComplete(message, It.IsAny<Exception>()), Times.Once);
        }
    }
}
