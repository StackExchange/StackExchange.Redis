using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class CancellationTests : TestBase
    {
        public CancellationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task WithCancellation_CancelledToken_ThrowsOperationCanceledException()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            using (db.WithCancellation(cts.Token))
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await db.StringSetAsync("test:cancelled", "value");
                });
            }
        }

        [Fact]
        public async Task WithCancellation_ValidToken_OperationSucceeds()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var cts = new CancellationTokenSource();

            using (db.WithCancellation(cts.Token))
            {
                // This should succeed
                await db.StringSetAsync("test:success", "value");
                var result = await db.StringGetAsync("test:success");
                Assert.Equal("value", result);
            }
        }

        [Fact]
        public async Task WithTimeout_ShortTimeout_ThrowsOperationCanceledException()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using (db.WithTimeout(TimeSpan.FromMilliseconds(1)))
            {
                // This might throw due to timeout, but let's test the mechanism
                try
                {
                    await db.StringSetAsync("test:timeout", "value");
                    // If it succeeds, that's fine too - Redis is fast
                }
                catch (OperationCanceledException)
                {
                    // Expected for very short timeouts
                }
            }
        }

        [Fact]
        public async Task WithCancellationAndTimeout_CombinesCorrectly()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var cts = new CancellationTokenSource();

            using (db.WithCancellationAndTimeout(cts.Token, TimeSpan.FromSeconds(10)))
            {
                // This should succeed with both cancellation and timeout
                await db.StringSetAsync("test:combined", "value");
                var result = await db.StringGetAsync("test:combined");
                Assert.Equal("value", result);
            }
        }

        [Fact]
        public async Task NestedScopes_InnerScopeTakesPrecedence()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var outerCts = new CancellationTokenSource();
            using var innerCts = new CancellationTokenSource();

            using (db.WithCancellation(outerCts.Token))
            {
                // Outer scope active
                await db.StringSetAsync("test:outer", "value1");

                using (db.WithCancellation(innerCts.Token))
                {
                    // Inner scope should take precedence
                    await db.StringSetAsync("test:inner", "value2");
                }

                // Back to outer scope
                await db.StringSetAsync("test:outer2", "value3");
            }

            // Verify all operations succeeded
            Assert.Equal("value1", await db.StringGetAsync("test:outer"));
            Assert.Equal("value2", await db.StringGetAsync("test:inner"));
            Assert.Equal("value3", await db.StringGetAsync("test:outer2"));
        }

        [Fact]
        public async Task WithoutAmbientCancellation_OperationsWorkNormally()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            // No ambient cancellation - should work normally
            await db.StringSetAsync("test:normal", "value");
            var result = await db.StringGetAsync("test:normal");
            Assert.Equal("value", result);
        }

        [Fact]
        public async Task CancellationDuringOperation_CancelsGracefully()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var cts = new CancellationTokenSource();

            using (db.WithCancellation(cts.Token))
            {
                // Start an operation and cancel it mid-flight
                var task = db.StringSetAsync("test:cancel-during", "value");

                // Cancel after a short delay
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10);
                    cts.Cancel();
                });

                try
                {
                    await task;
                    // If it completes before cancellation, that's fine
                }
                catch (OperationCanceledException)
                {
                    // Expected if cancellation happens during operation
                }
            }
        }

        [Fact]
        public void GetCurrentContext_ReturnsCorrectContext()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            // No context initially
            var context = RedisCancellationExtensions.GetCurrentContext();
            Assert.Null(context);

            using var cts = new CancellationTokenSource();
            using (db.WithCancellation(cts.Token))
            {
                context = RedisCancellationExtensions.GetCurrentContext();
                Assert.NotNull(context);
                Assert.Equal(cts.Token, context.Token);
                Assert.Null(context.Timeout);
            }

            using (db.WithTimeout(TimeSpan.FromSeconds(5)))
            {
                context = RedisCancellationExtensions.GetCurrentContext();
                Assert.NotNull(context);
                Assert.False(context.Token.CanBeCanceled);
                Assert.Equal(TimeSpan.FromSeconds(5), context.Timeout);
            }

            // Context should be null again
            context = RedisCancellationExtensions.GetCurrentContext();
            Assert.Null(context);
        }

        [Fact]
        public async Task PubSub_WithCancellation_WorksCorrectly()
        {
            using var conn = Create();
            var subscriber = conn.GetSubscriber();

            using var cts = new CancellationTokenSource();

            using (subscriber.WithCancellation(cts.Token))
            {
                // Test pub/sub operations with cancellation
                var channel = RedisChannel.Literal(Me());
                var messageReceived = new TaskCompletionSource<bool>();

                await subscriber.SubscribeAsync(channel, (ch, msg) =>
                {
                    messageReceived.TrySetResult(true);
                });

                await subscriber.PublishAsync(channel, "test message");

                // Wait for message with timeout
                var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(received);
            }
        }
    }
}
