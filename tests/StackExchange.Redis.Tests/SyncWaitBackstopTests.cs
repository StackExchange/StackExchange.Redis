using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RESPite;
using RESPite.Operations;
using RESPite.Transports;
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
        Assert.InRange(watch.ElapsedMilliseconds, 200, 30_000); // the backstop, not the configured timeout
    }

    /// <summary>
    /// The same promise, kept by the blocking send itself: a synchronous call that never leaves the calling
    /// thread has no outer wait to put a backstop on, so the executor's own wait carries the limit.
    /// </summary>
    [Fact]
    public void ABlockingSendThatIsNeverAnsweredStillEnds()
    {
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespClientConnection(new SilentTransport(), static (in RespRedirect _, RespPayloadOperation _) => false)))
        {
            SyncTimeoutMilliseconds = () => 100, // not heartbeat-driven, so this is the only timeout there is
        };

        var watch = Stopwatch.StartNew();
        var pending = new RespDatabaseContext(new RespContext().WithExecutor(executor)).Blocking().Strings.GetAsync("k");
        watch.Stop();

        Assert.True(pending.IsFaulted);
        Assert.Throws<RedisTimeoutException>(() => pending.GetAwaiter().GetResult());
        Assert.InRange(watch.ElapsedMilliseconds, 50, 30_000);
    }

    /// <summary>Accepts every write and never answers.</summary>
    private sealed class SilentTransport : DuplexTransport
    {
        private readonly byte[] _sink = new byte[64 * 1024];

        public override Memory<byte> GetMemory(int sizeHint = 0) => sizeHint > _sink.Length ? new byte[sizeHint] : _sink;

        public override void Advance(int count) { }

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) { }

        public override ValueTask DisposeAsync() => default;
    }

    /// <summary>Claims to enforce timeouts, and never completes anything.</summary>
    private sealed class NeverAnswers : RespExecutorBase
    {
        public override int Database => 0;

        internal override bool EnforcesTimeouts => true;

        // so the synchronous surface takes the asynchronous path and blocks on its task, which is where this
        // backstop lives; a blocking send carries its own (below)
        internal override bool CanSendBlocking => false;

        public override RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(new TaskCompletionSource<RespPayload>().Task);
    }
}
