using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class ChannelMessageQueueTests
    {
        [Fact]
        public void ItemsYieldedToChannelCanBeRead()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            Assert.True(channel.Writer.TryWrite(new ChannelMessage(queue, "TestChannel", "Test")));
            Assert.True(queue.TryRead(out var message));
            Assert.Equal("TestChannel", message.Channel);
            Assert.Equal("Test", message.Message);
            Assert.Equal("TestChannel", message.SubscriptionChannel);
        }

        [Fact]
        public void ActualChannelIsProvidedInChannelMessageChanelProperty()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var queue = new ChannelMessageQueue(
                "TestChannel.*",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            Assert.True(channel.Writer.TryWrite(new ChannelMessage(queue, "TestChannel.A", "Test")));
            Assert.True(queue.TryRead(out var message));
            Assert.Equal("TestChannel.A", message.Channel);
            Assert.Equal("Test", message.Message);
            Assert.Equal("TestChannel.*", message.SubscriptionChannel);
        }

        [Fact]
        public void ReadAsyncYieldsItemWhenOneIsAvailable()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            var readTask = queue.ReadAsync();

            Assert.True(channel.Writer.TryWrite(new ChannelMessage(queue, "TestChannel", "Test")));
            Assert.True(readTask.IsCompleted);
            var message = readTask.GetAwaiter().GetResult();

            Assert.Equal("TestChannel", message.Channel);
            Assert.Equal("Test", message.Message);
            Assert.Equal("TestChannel", message.SubscriptionChannel);
        }

        [Fact]
        public void UnsubscribeSignalsProvidedDelegate()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            CommandFlags? unsubscribeFlags = null;
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => unsubscribeFlags = f,
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            queue.Unsubscribe(CommandFlags.FireAndForget);

            Assert.Equal(CommandFlags.FireAndForget, unsubscribeFlags);
        }

        [Fact]
        public void UnsubscribeAsyncSignalsProvidedDelegate()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            CommandFlags? unsubscribeFlags = null;
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) =>
                {
                    unsubscribeFlags = f;
                    return Task.CompletedTask;
                },
                ex => throw new NotImplementedException());

            Assert.True(queue.UnsubscribeAsync(CommandFlags.FireAndForget).IsCompleted);
            Assert.Equal(CommandFlags.FireAndForget, unsubscribeFlags);
        }

        [Fact]
        public async Task OnMessageCreatesMessageLoop()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var received = Channel.CreateUnbounded<ChannelMessage>();
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            // Start the message loop
            // NOTE: The loop runs on the thread pool
            queue.OnMessage(m =>
            {
                Assert.True(received.Writer.TryWrite(m));
            });

            // Write an item and verify that it comes through the loop
            Assert.True(channel.Writer.TryWrite(new ChannelMessage(queue, "TestChannel", "Test")));

            var message = await received.Reader.ReadAsync();
            Assert.Equal("TestChannel", message.Channel);
            Assert.Equal("Test", message.Message);
            Assert.Equal("TestChannel", message.SubscriptionChannel);

            // Shut down the loop.
            channel.Writer.TryComplete();
        }

        [Fact]
        public async Task OnMessageAsyncCreatesMessageLoop()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var received = Channel.CreateUnbounded<ChannelMessage>();
            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => throw new NotImplementedException());

            // Start the message loop
            // NOTE: The loop runs on the thread pool
            queue.OnMessage(m =>
            {
                Assert.True(received.Writer.TryWrite(m));
                return Task.CompletedTask;
            });

            // Write an item and verify that it comes through the loop
            Assert.True(channel.Writer.TryWrite(new ChannelMessage(queue, "TestChannel", "Test")));

            var message = await received.Reader.ReadAsync();
            Assert.Equal("TestChannel", message.Channel);
            Assert.Equal("Test", message.Message);
            Assert.Equal("TestChannel", message.SubscriptionChannel);

            // Shut down the loop.
            channel.Writer.TryComplete();
        }

        [Fact]
        public async Task CompletionExceptionGoesToInternalErrorHandlerWhenUsingOnMessage()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>();
            var received = Channel.CreateUnbounded<ChannelMessage>();
            var errorOccurred = new TaskCompletionSource<Exception>();

            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => errorOccurred.TrySetResult(ex));

            // Start the message loop
            // NOTE: The loop runs on the thread pool
            queue.OnMessage(m =>
            {
                throw new NotImplementedException();
            });

            // Complete with an error, triggering the exception
            channel.Writer.TryComplete(new Exception("BARF!"));
            Assert.Equal("BARF!", (await errorOccurred.Task).Message);
        }

        [Fact]
        public async Task CompletionExceptionGoesToInternalErrorHandlerWhenUsingOnMessageAsync()
        {
            var channel = Channel.CreateUnbounded<ChannelMessage>(new UnboundedChannelOptions());
            var received = Channel.CreateUnbounded<ChannelMessage>();
            var errorOccurred = new TaskCompletionSource<Exception>();

            var queue = new ChannelMessageQueue(
                "TestChannel",
                channel.Reader,
                (f) => throw new NotImplementedException(),
                (f) => throw new NotImplementedException(),
                ex => errorOccurred.TrySetResult(ex));

            // Start the message loop
            // NOTE: The loop runs on the thread pool
            queue.OnMessage(m =>
            {
                return Task.FromException(new NotImplementedException());
            });

            // Complete with an error, triggering the exception
            channel.Writer.TryComplete(new Exception("BARF!"));
            Assert.Equal("BARF!", (await errorOccurred.Task).Message);
        }
    }
}
