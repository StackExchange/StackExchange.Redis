using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class CommandRetryQueueManagerTests
    {
        [Fact]
        public void ValidateMaxQueueLengthFails()
        {
            CommandRetryQueueManager commandRetryQueueManager = new CommandRetryQueueManager(maxRetryQueueLength: 0);
            var message = Message.Create(0, CommandFlags.None, RedisCommand.SET);
            var mux = new SharedConnectionFixture().Connection;
            var messageFailedException = new Exception();

            var isEnqueuedWithZeroMaxLength = commandRetryQueueManager.TryHandleFailedCommand(new FailedCommand(message, mux, messageFailedException));

            Assert.False(isEnqueuedWithZeroMaxLength);
        }
    }
}
