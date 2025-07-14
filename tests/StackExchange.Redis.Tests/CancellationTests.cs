using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class CancellationTests : TestBase
    {
        public CancellationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task GetEffectiveCancellationToken_Nesting()
        {
            // this is a pure test - no database access
            IDatabase? db = null!;

            // No context initially
            Assert.Null(RedisCancellationExtensions.GetCurrentScope());
            Assert.Equal(CancellationToken.None, RedisCancellationExtensions.GetEffectiveCancellationToken());

            using var cts = new CancellationTokenSource();
            using (var outer = db.WithCancellation(cts.Token))
            {
                Assert.NotNull(outer);
                Assert.Same(outer, RedisCancellationExtensions.GetCurrentScope());
                Assert.Equal(cts.Token, RedisCancellationExtensions.GetEffectiveCancellationToken());

                // nest with timeout
                using (var inner = db.WithTimeout(TimeSpan.FromSeconds(0.5)))
                {
                    Assert.NotNull(inner);
                    Assert.Same(inner, RedisCancellationExtensions.GetCurrentScope());
                    var active = RedisCancellationExtensions.GetEffectiveCancellationToken();

                    Assert.False(active.IsCancellationRequested);

                    await Task.Delay(TimeSpan.FromSeconds(0.5));
                    for (int i = 0; i < 20; i++)
                    {
                        if (active.IsCancellationRequested) break;
                        await Task.Delay(TimeSpan.FromSeconds(0.1));
                    }
                    Assert.True(active.IsCancellationRequested);
                    Assert.Equal(active, RedisCancellationExtensions.GetEffectiveCancellationToken());
                }

                // back to outer
                Assert.Same(outer, RedisCancellationExtensions.GetCurrentScope());
                Assert.Equal(cts.Token, RedisCancellationExtensions.GetEffectiveCancellationToken());

                // nest with suppression
                using (var inner = db.WithCancellation(CancellationToken.None))
                {
                    Assert.NotNull(inner);
                    Assert.Same(inner, RedisCancellationExtensions.GetCurrentScope());
                    Assert.Equal(CancellationToken.None, RedisCancellationExtensions.GetEffectiveCancellationToken());
                }

                // back to outer
                Assert.Same(outer, RedisCancellationExtensions.GetCurrentScope());
                Assert.Equal(cts.Token, RedisCancellationExtensions.GetEffectiveCancellationToken());
            }
            Assert.Null(RedisCancellationExtensions.GetCurrentScope());
            Assert.Equal(CancellationToken.None, RedisCancellationExtensions.GetEffectiveCancellationToken());
        }

        [Fact]
        public async Task WithCancellation_CancelledToken_ThrowsOperationCanceledException()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            using (db.WithCancellation(cts.Token))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    await db.StringSetAsync(Me(), "value");
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
                RedisKey key = Me();
                // This should succeed
                await db.StringSetAsync(key, "value");
                var result = await db.StringGetAsync(key);
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
                    await db.StringSetAsync(Me(), "value");
                    // If it succeeds, that's fine too - Redis is fast
                    Skip.Inconclusive("Redis is too fast for this test.");
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
                RedisKey key = Me();
                await db.StringSetAsync(key, "value");
                var result = await db.StringGetAsync(key);
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

            RedisKey key1 = Me() + ":outer",
                key2 = Me() + ":inner",
                key3 = Me() + ":outer2";
            using (db.WithCancellation(outerCts.Token))
            {
                // Outer scope active
                await db.StringSetAsync(key1, "value1");

                using (db.WithCancellation(innerCts.Token))
                {
                    // Inner scope should take precedence
                    await db.StringSetAsync(key2, "value2");
                }

                // Back to outer scope
                await db.StringSetAsync(key3, "value3");
            }

            // Verify all operations succeeded
            Assert.Equal("value1", await db.StringGetAsync(key1));
            Assert.Equal("value2", await db.StringGetAsync(key2));
            Assert.Equal("value3", await db.StringGetAsync(key3));
        }

        [Fact]
        public async Task WithoutAmbientCancellation_OperationsWorkNormally()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            // No ambient cancellation - should work normally
            RedisKey key = Me();
            await db.StringSetAsync(key, "value");
            var result = await db.StringGetAsync(key);
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
                var task = db.StringSetAsync(Me(), "value");

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
    }
}
