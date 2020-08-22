using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class PubSubCommand : TestBase
    {
        public PubSubCommand(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        [Fact]
        public void SubscriberCount()
        {
            using (var conn = Create())
            {
                RedisChannel channel = Me() + Guid.NewGuid();
                var server = conn.GetServer(conn.GetEndPoints()[0]);

                var channels = server.SubscriptionChannels(Me() + "*");
                Assert.DoesNotContain(channel, channels);

                _ = server.SubscriptionPatternCount();
                var count = server.SubscriptionSubscriberCount(channel);
                Assert.Equal(0, count);
                conn.GetSubscriber().Subscribe(channel, delegate { });
                count = server.SubscriptionSubscriberCount(channel);
                Assert.Equal(1, count);

                channels = server.SubscriptionChannels(Me() + "*");
                Assert.Contains(channel, channels);
            }
        }

        [Fact]
        public async Task SubscriberCountAsync()
        {
            using (var conn = Create())
            {
                RedisChannel channel = Me() + Guid.NewGuid();
                var server = conn.GetServer(conn.GetEndPoints()[0]);

                var channels = await server.SubscriptionChannelsAsync(Me() + "*").WithTimeout(2000);
                Assert.DoesNotContain(channel, channels);

                _ = await server.SubscriptionPatternCountAsync().WithTimeout(2000);
                var count = await server.SubscriptionSubscriberCountAsync(channel).WithTimeout(2000);
                Assert.Equal(0, count);
                await conn.GetSubscriber().SubscribeAsync(channel, delegate { }).WithTimeout(2000);
                count = await server.SubscriptionSubscriberCountAsync(channel).WithTimeout(2000);
                Assert.Equal(1, count);

                channels = await server.SubscriptionChannelsAsync(Me() + "*").WithTimeout(2000);
                Assert.Contains(channel, channels);
            }
        }
    }
    static class Util
    {
        public static async Task WithTimeout(this Task task, int timeoutMs,
            [CallerMemberName] string caller = null, [CallerLineNumber] int line = 0)
        {
            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token)).ForAwait())
            {
                cts.Cancel();
                await task.ForAwait();
            }
            else
            {
                throw new TimeoutException($"timout from {caller} line {line}");
            }
        }
        public static async Task<T> WithTimeout<T>(this Task<T> task, int timeoutMs,
            [CallerMemberName] string caller = null, [CallerLineNumber] int line = 0)
        {
            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token)).ForAwait())
            {
                cts.Cancel();
                return await task.ForAwait();
            }
            else
            {
                throw new TimeoutException($"timout from {caller} line {line}");
            }
        }
    }
}
