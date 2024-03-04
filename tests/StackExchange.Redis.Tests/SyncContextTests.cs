using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SyncContextTests : TestBase
    {
        public SyncContextTests(ITestOutputHelper testOutput) : base(testOutput) { }

        /* Note A (referenced below)
         *
         * When sync-context is *enabled*, we don't validate OpCount > 0 - this is because *with the additional checks*,
         * it can genuinely happen that by the time we actually await it, it has completed - which results in a brittle test.
         */
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DetectSyncContextUnsafe(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext(Writer);
            Assert.Equal(0, ctx.OpCount);
            await Task.Delay(100).ConfigureAwait(continueOnCapturedContext);

            AssertState(continueOnCapturedContext, ctx);
        }

        private void AssertState(bool continueOnCapturedContext, MySyncContext ctx)
        {
            Log($"Context in AssertState: {ctx}");
            if (continueOnCapturedContext)
            {
                Assert.True(ctx.IsCurrent, nameof(ctx.IsCurrent));
                // see note A re OpCount
            }
            else
            {
                // no guarantees on sync-context still being current; depends on sync vs async
                Assert.Equal(0, ctx.OpCount);
            }
        }

        [Fact]
        public void SyncPing()
        {
            using var ctx = new MySyncContext(Writer);
            using var conn = Create();
            Assert.Equal(0, ctx.OpCount);
            var db = conn.GetDatabase();
            db.Ping();
            Assert.Equal(0, ctx.OpCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AsyncPing(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext(Writer);
            using var conn = Create();
            Assert.Equal(0, ctx.OpCount);
            var db = conn.GetDatabase();
            Log($"Context before await: {ctx}");
            await db.PingAsync().ConfigureAwait(continueOnCapturedContext);

            AssertState(continueOnCapturedContext, ctx);
        }

        [Fact]
        public void SyncConfigure()
        {
            using var ctx = new MySyncContext(Writer);
            using var conn = Create();
            Assert.Equal(0, ctx.OpCount);
            Assert.True(conn.Configure());
            Assert.Equal(0, ctx.OpCount);
        }

        [Theory]
        [InlineData(true)] // fail: Expected: Not RanToCompletion, Actual: RanToCompletion
        [InlineData(false)] // pass
        public async Task AsyncConfigure(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext(Writer);
            using var conn = Create();

            Log($"Context initial: {ctx}");
            await Task.Delay(500);
            await conn.GetDatabase().PingAsync(); // ensure we're all ready
            ctx.Reset();
            Log($"Context before: {ctx}");

            Assert.Equal(0, ctx.OpCount);
            Assert.True(await conn.ConfigureAsync(Writer).ConfigureAwait(continueOnCapturedContext), "config ran");

            AssertState(continueOnCapturedContext, ctx);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ConnectAsync(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext(Writer);
            var config = GetConfiguration(); // not ideal, but sufficient
            await ConnectionMultiplexer.ConnectAsync(config, Writer).ConfigureAwait(continueOnCapturedContext);

            AssertState(continueOnCapturedContext, ctx);
        }

        public sealed class MySyncContext : SynchronizationContext, IDisposable
        {
            private readonly SynchronizationContext? _previousContext;
            private readonly TextWriter _log;
            public MySyncContext(TextWriter log)
            {
                _previousContext = Current;
                _log = log;
                SetSynchronizationContext(this);
            }
            public int OpCount => Thread.VolatileRead(ref _opCount);
            private int _opCount;
            private void Incr() => Interlocked.Increment(ref _opCount);

            public void Reset() => Thread.VolatileWrite(ref _opCount, 0);

            public override string ToString() => $"Sync context ({(IsCurrent ? "active" : "inactive")}): {OpCount}";

            void IDisposable.Dispose() => SetSynchronizationContext(_previousContext);

            public override void Post(SendOrPostCallback d, object? state)
            {
                Log(_log, "sync-ctx: Post");
                Incr();
                ThreadPool.QueueUserWorkItem(static state =>
                {
                    var tuple = (Tuple<MySyncContext, SendOrPostCallback, object?>)state!;
                    tuple.Item1.Invoke(tuple.Item2, tuple.Item3);
                }, Tuple.Create<MySyncContext, SendOrPostCallback, object?>(this, d, state));
            }

            private void Invoke(SendOrPostCallback d, object? state)
            {
                Log(_log, "sync-ctx: Invoke");
                if (!IsCurrent) SetSynchronizationContext(this);
                d(state);
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                Log(_log, "sync-ctx: Send");
                Incr();
                Invoke(d, state);
            }

            public bool IsCurrent => ReferenceEquals(this, Current);

            public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
            {
                Incr();
                return base.Wait(waitHandles, waitAll, millisecondsTimeout);
            }
            public override void OperationStarted()
            {
                Incr();
                base.OperationStarted();
            }
            public override void OperationCompleted()
            {
                Incr();
                base.OperationCompleted();
            }
        }
    }
}
