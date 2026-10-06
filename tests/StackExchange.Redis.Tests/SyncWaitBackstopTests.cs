using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RESPite;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A synchronous call on an executor that promises to time operations out itself must still not wait for
/// ever when that promise is broken.
/// </summary>
/// <remarks>
/// On CI a synchronous <c>HashFieldGetAndDelete</c> waited for over ten minutes on a connection that had stopped
/// answering - the async callers on the same connection were each timed out at five seconds - and the hang
/// watchdog killed the whole run. The sync wait trusted the executor with an infinite wait; it now has a
/// backstop well beyond the configured timeout, and says plainly that the executor's own timeout was missed.
/// </remarks>
public class SyncWaitBackstopTests
{
    [Fact]
    public void ASyncCallTheExecutorNeverTimesOutStillEnds()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.TimeoutMilliseconds.Returns(100);
        IDatabase db = new RespDatabaseContext(new RespContext().WithExecutor(new NeverAnswers())).AsDatabase(multiplexer);

        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<RedisTimeoutException>(() => db.StringGet("k"));
        watch.Stop();

        Assert.Contains("not timed out by its connection", ex.Message);
        Assert.Contains("Pump:", ex.Message);
        Assert.InRange(watch.ElapsedMilliseconds, 200, 30_000); // the backstop, not the configured timeout
    }

    /// <summary>Claims to enforce timeouts, and never completes anything.</summary>
    private sealed class NeverAnswers : RespExecutorBase
    {
        public override int Database => 0;

        internal override bool EnforcesTimeouts => true;

        public override RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(new TaskCompletionSource<RespPayload>().Task);
    }
}
