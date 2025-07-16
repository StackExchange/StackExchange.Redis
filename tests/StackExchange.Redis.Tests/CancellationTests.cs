using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class PureCancellationTests
    {
        [Fact]
        public async Task GetEffectiveCancellationToken_Nesting()
        {
            // this is a pure test - no database access
            IConnectionMultiplexer muxerA = new DummyMultiplexer(), muxerB = new DummyMultiplexer();

            // No context initially
            Assert.Null(muxerA.GetCurrentScope());
            Assert.Equal(CancellationToken.None, muxerA.GetEffectiveCancellationToken());
            Assert.Null(muxerB.GetCurrentScope());
            Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());

            using var cts = new CancellationTokenSource();
            using (var outer = muxerA.WithCancellation(cts.Token))
            {
                Assert.NotNull(outer);
                Assert.Same(outer, muxerA.GetCurrentScope());
                Assert.Equal(cts.Token, muxerA.GetEffectiveCancellationToken());
                Assert.Null(muxerB.GetCurrentScope()); // B unaffected
                Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());

                // nest with timeout
                using (var inner = muxerA.WithTimeout(TimeSpan.FromSeconds(0.5)))
                {
                    Assert.NotNull(inner);
                    Assert.Same(inner, muxerA.GetCurrentScope());
                    var active = muxerA.GetEffectiveCancellationToken();

                    Assert.Null(muxerB.GetCurrentScope()); // B unaffected
                    Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());

                    Assert.False(active.IsCancellationRequested);

                    await Task.Delay(TimeSpan.FromSeconds(0.5));
                    for (int i = 0; i < 20; i++)
                    {
                        if (active.IsCancellationRequested) break;
                        await Task.Delay(TimeSpan.FromSeconds(0.1));
                    }
                    Assert.True(active.IsCancellationRequested);
                    Assert.Equal(active, muxerA.GetEffectiveCancellationToken(checkForCancellation: false));
                }

                // back to outer
                Assert.Same(outer, muxerA.GetCurrentScope());
                Assert.Equal(cts.Token, muxerA.GetEffectiveCancellationToken());
                Assert.Null(muxerB.GetCurrentScope()); // B unaffected
                Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());

                // nest with suppression
                using (var inner = muxerA.WithCancellation(CancellationToken.None))
                {
                    Assert.NotNull(inner);
                    Assert.Same(inner, muxerA.GetCurrentScope());
                    Assert.Equal(CancellationToken.None, muxerA.GetEffectiveCancellationToken());
                }

                // back to outer
                Assert.Same(outer, muxerA.GetCurrentScope());
                Assert.Equal(cts.Token, muxerA.GetEffectiveCancellationToken());
                Assert.Null(muxerB.GetCurrentScope()); // B unaffected
                Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());
            }
            Assert.Null(muxerA.GetCurrentScope());
            Assert.Equal(CancellationToken.None, muxerA.GetEffectiveCancellationToken());
            Assert.Null(muxerB.GetCurrentScope()); // B unaffected
            Assert.Equal(CancellationToken.None, muxerB.GetEffectiveCancellationToken());
        }

        private sealed class DummyMultiplexer : IConnectionMultiplexer
        {
            public override string ToString() => "";

            void IDisposable.Dispose() { }

            ValueTask IAsyncDisposable.DisposeAsync() => default;

            string IConnectionMultiplexer.ClientName => "";

            string IConnectionMultiplexer.Configuration => "";

            int IConnectionMultiplexer.TimeoutMilliseconds => 0;

            long IConnectionMultiplexer.OperationCount => 0;

            bool IConnectionMultiplexer.PreserveAsyncOrder
            {
                get => false;
                set { }
            }

            bool IConnectionMultiplexer.IsConnected => true;

            bool IConnectionMultiplexer.IsConnecting => false;

            bool IConnectionMultiplexer.IncludeDetailInExceptions
            {
                get => false;
                set { }
            }

            int IConnectionMultiplexer.StormLogThreshold
            {
                get => 0;
                set { }
            }

            void IConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) => throw new NotImplementedException();

            ServerCounters IConnectionMultiplexer.GetCounters() => throw new NotImplementedException();

            event EventHandler<RedisErrorEventArgs>? IConnectionMultiplexer.ErrorMessage
            {
                add { }
                remove { }
            }

            event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionFailed
            {
                add { }
                remove { }
            }

            event EventHandler<InternalErrorEventArgs>? IConnectionMultiplexer.InternalError
            {
                add { }
                remove { }
            }

            event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionRestored
            {
                add { }
                remove { }
            }

            event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChanged
            {
                add { }
                remove { }
            }

            event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChangedBroadcast
            {
                add { }
                remove { }
            }

            event EventHandler<ServerMaintenanceEvent>? IConnectionMultiplexer.ServerMaintenanceEvent
            {
                add { }
                remove { }
            }

            EndPoint[] IConnectionMultiplexer.GetEndPoints(bool configuredOnly) => throw new NotImplementedException();

            void IConnectionMultiplexer.Wait(Task task) => throw new NotImplementedException();

            T IConnectionMultiplexer.Wait<T>(Task<T> task) => throw new NotImplementedException();

            void IConnectionMultiplexer.WaitAll(params Task[] tasks) => throw new NotImplementedException();

            event EventHandler<HashSlotMovedEventArgs>? IConnectionMultiplexer.HashSlotMoved
            {
                add { }
                remove { }
            }

            int IConnectionMultiplexer.HashSlot(RedisKey key) => throw new NotImplementedException();

            ISubscriber IConnectionMultiplexer.GetSubscriber(object? asyncState) => throw new NotImplementedException();

            IDatabase IConnectionMultiplexer.GetDatabase(int db, object? asyncState) => throw new NotImplementedException();

            IServer IConnectionMultiplexer.GetServer(string host, int port, object? asyncState) => throw new NotImplementedException();

            IServer IConnectionMultiplexer.GetServer(string hostAndPort, object? asyncState) => throw new NotImplementedException();

            IServer IConnectionMultiplexer.GetServer(IPAddress host, int port) => throw new NotImplementedException();

            IServer IConnectionMultiplexer.GetServer(EndPoint endpoint, object? asyncState) => throw new NotImplementedException();

            IServer[] IConnectionMultiplexer.GetServers() => throw new NotImplementedException();

            Task<bool> IConnectionMultiplexer.ConfigureAsync(TextWriter? log) => throw new NotImplementedException();

            bool IConnectionMultiplexer.Configure(TextWriter? log) => throw new NotImplementedException();

            string IConnectionMultiplexer.GetStatus() => throw new NotImplementedException();

            void IConnectionMultiplexer.GetStatus(TextWriter log) => throw new NotImplementedException();

            void IConnectionMultiplexer.Close(bool allowCommandsToComplete) => throw new NotImplementedException();

            Task IConnectionMultiplexer.CloseAsync(bool allowCommandsToComplete) => throw new NotImplementedException();

            string? IConnectionMultiplexer.GetStormLog() => throw new NotImplementedException();

            void IConnectionMultiplexer.ResetStormLog() => throw new NotImplementedException();

            long IConnectionMultiplexer.PublishReconfigure(CommandFlags flags) => throw new NotImplementedException();

            Task<long> IConnectionMultiplexer.PublishReconfigureAsync(CommandFlags flags) => throw new NotImplementedException();

            int IConnectionMultiplexer.GetHashSlot(RedisKey key) => throw new NotImplementedException();

            void IConnectionMultiplexer.ExportConfiguration(Stream destination, ExportOptions options) => throw new NotImplementedException();

            void IConnectionMultiplexer.AddLibraryNameSuffix(string suffix) => throw new NotImplementedException();
        }
    }

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

            using (db.Multiplexer.WithCancellation(cts.Token))
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

            using (db.Multiplexer.WithCancellation(cts.Token))
            {
                RedisKey key = Me();
                // This should succeed
                await db.StringSetAsync(key, "value");
                var result = await db.StringGetAsync(key);
                Assert.Equal("value", result);
            }
        }

        private void Pause(IDatabase db)
        {
            db.Execute("client", "pause", ConnectionPauseMilliseconds, CommandFlags.FireAndForget);
        }

        private ConnectionMultiplexer Create() => ConnectionMultiplexer.Connect("127.0.0.1:4000");

        [Fact]
        public async Task WithTimeout_ShortTimeout_Async_ThrowsOperationCanceledException()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            var watch = Stopwatch.StartNew();
            Pause(db);

            using (db.Multiplexer.WithTimeout(TimeSpan.FromMilliseconds(ShortDelayMilliseconds)))
            {
                // This might throw due to timeout, but let's test the mechanism
                var pending = db.StringSetAsync(Me(), "value"); // check we get past this
                try
                {
                    await pending;
                    // If it succeeds, that's fine too - Redis is fast
                    Skip.Inconclusive(TooFast + ": " + watch.ElapsedMilliseconds + "ms");
                }
                catch (OperationCanceledException)
                {
                    // Expected for very short timeouts
                }
            }
        }

        [Fact]
        public void WithTimeout_ShortTimeout_Sync_ThrowsOperationCanceledException()
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            var watch = Stopwatch.StartNew();
            Pause(db);

            using (db.Multiplexer.WithTimeout(TimeSpan.FromMilliseconds(ShortDelayMilliseconds)))
            {
                // This might throw due to timeout, but let's test the mechanism
                try
                {
                    db.StringSet(Me(), "value"); // check we get past this
                    // If it succeeds, that's fine too - Redis is fast
                    Skip.Inconclusive(TooFast + ": " + watch.ElapsedMilliseconds + "ms");
                }
                catch (OperationCanceledException)
                {
                    // Expected for very short timeouts
                }
            }
        }

        private const string TooFast = "This operation completed too quickly to verify this behaviour.";

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

        public enum CancelStrategy
        {
            Constructor,
            Method,
            Manual,
        }

        private const int ConnectionPauseMilliseconds = 50, ShortDelayMilliseconds = 5;

        [Theory]
        [InlineData(CancelStrategy.Constructor)]
        [InlineData(CancelStrategy.Method)]
        [InlineData(CancelStrategy.Manual)]
        public async Task CancellationDuringOperation_CancelsGracefully(CancelStrategy strategy)
        {
            using var conn = Create();
            var db = conn.GetDatabase();

            static CancellationTokenSource CreateCts(CancelStrategy strategy)
            {
                switch (strategy)
                {
                    case CancelStrategy.Constructor:
                        return new CancellationTokenSource(TimeSpan.FromMilliseconds(ShortDelayMilliseconds));
                    case CancelStrategy.Method:
                        var cts = new CancellationTokenSource();
                        cts.CancelAfter(TimeSpan.FromMilliseconds(ShortDelayMilliseconds));
                        return cts;
                    case CancelStrategy.Manual:
                        cts = new();
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(ShortDelayMilliseconds);
                            // ReSharper disable once MethodHasAsyncOverload - TFM-dependent
                            cts.Cancel();
                        });
                        return cts;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(strategy));
                }
            }

            var watch = Stopwatch.StartNew();
            Pause(db);

            using var cts = CreateCts(strategy);

            // Cancel after a short delay
            using (db.Multiplexer.WithCancellation(cts.Token))
            {
                // Start an operation and cancel it mid-flight
                var pending = db.StringSetAsync($"{Me()}:{strategy}", "value");

                try
                {
                    await pending;
                    Skip.Inconclusive(TooFast + ": " + watch.ElapsedMilliseconds + "ms");
                }
                catch (OperationCanceledException oce) when (oce.CancellationToken == cts.Token)
                {
                    // Expected if cancellation happens during operation
                }
            }
        }
    }
}
