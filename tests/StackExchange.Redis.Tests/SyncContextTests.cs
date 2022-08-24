using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SyncContextTests : TestBase
    {
        public SyncContextTests(ITestOutputHelper testOutput) : base(testOutput) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DetectSyncContextUsafe(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext();
            Assert.Equal(0, ctx.OpCount);
            await Task.Delay(100).ConfigureAwait(continueOnCapturedContext);
            if (continueOnCapturedContext)
            {
                Assert.True(ctx.OpCount > 0, $"Opcount: {ctx.OpCount}");
            }
            else
            {
                Assert.Equal(0, ctx.OpCount);
            }
        }

        [Fact]
        public void SyncPing()
        {
            using var ctx = new MySyncContext();
            using var conn = Create();
            var db = conn.GetDatabase();
            db.Ping();
            Assert.Equal(0, ctx.OpCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AsyncPing(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext();
            using var conn = Create();
            var db = conn.GetDatabase();
            await db.PingAsync().ConfigureAwait(continueOnCapturedContext);
            if (continueOnCapturedContext)
            {
                Assert.True(ctx.OpCount > 0, $"Opcount: {ctx.OpCount}");
            }
            else
            {
                Assert.Equal(0, ctx.OpCount);
            }
        }

        [Fact]
        public void SyncConfigure()
        {
            using var ctx = new MySyncContext();
            using var conn = Create();
            Assert.True(conn.Configure());
        }

        [Theory]
        [InlineData(true)] // net472: pass; net6.0: fail (expected 0, actual 1)
        [InlineData(false)] // net472\net6.0: fail (expected 0, actual 1)
        public async Task AsyncConfigure(bool continueOnCapturedContext)
        {
            using var ctx = new MySyncContext();
            using var conn = Create();
            Assert.True(await conn.ConfigureAsync(Writer).ConfigureAwait(continueOnCapturedContext));
            if (continueOnCapturedContext)
            {
                Assert.True(ctx.OpCount > 0, $"Opcount: {ctx.OpCount}");
            }
            else
            {
                Assert.Equal(0, ctx.OpCount);
            }
        }

        public sealed class MySyncContext : SynchronizationContext, IDisposable
        {
            private readonly SynchronizationContext? _previousContext;
            public MySyncContext()
            {
                _previousContext = Current;
                SetSynchronizationContext(this);
            }
            public int OpCount => Thread.VolatileRead(ref _opCount);
            private int _opCount;
            private void Incr() => Interlocked.Increment(ref _opCount);

            void IDisposable.Dispose() => SetSynchronizationContext(_previousContext);

            public override void Post(SendOrPostCallback d, object? state)
            {
                Incr();
                base.Post(d, state);
            }
            public override void Send(SendOrPostCallback d, object? state)
            {
                Incr();
                base.Send(d, state);
            }

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
