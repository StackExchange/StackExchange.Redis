using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using RESPite.Operations;
using Xunit;

namespace RESPite.Tests;

// xUnit1031/xUnit1051 both misfire on the type under test: the analyzers match any `GetResult()` as
// `Task.GetAwaiter().GetResult()`, and any optional CancellationToken parameter as one that should come
// from TestContext. Here GetResult IS the API being tested, and the cancellation tokens are the subject
// of the test rather than an ambient courtesy.
#pragma warning disable xUnit1031, xUnit1051

/// <summary>
/// The token life-cycle of the operation core: completion, double-completion, recycle-then-stale, and
/// cancellation races. See design notes section 7, phase 1 - this is the piece most worth getting right
/// before anything consumes it.
/// </summary>
public class RespOperationTests
{
    /// <summary>A message that parses a RESP bulk string, and counts its own recycles.</summary>
    private sealed class StringMessage(RespParseOptions options = RespParseOptions.Parse)
        : RespMessageBase<string?>(options)
    {
        internal int Recycled;
        internal int Resets;

        protected override string? Parse(ref RespReader reader)
            => reader.TryGetSpan(out var span) ? Encoding.UTF8.GetString(span.ToArray()) : null;

        protected override void OnRecyclable() => Interlocked.Increment(ref Recycled);

        protected override void OnReset() => Interlocked.Increment(ref Resets);

        /// <summary>Arm the message the way a connection would, so it is "sent" and awaitable.</summary>
        internal StringMessage Arm(CancellationToken cancellationToken = default, byte[]? request = null, ArrayPool<byte>? pool = null)
        {
            SetRequest(request ?? DefaultRequest, pool, cancellationToken);
            Assert.True(TryReserveRequest(Token, out _));
            ReleaseRequest();
            return this;
        }

        /// <summary>Attach a request without reserving it, so the message is NOT yet "sent".</summary>
        internal void SetRequestForTest(byte[]? request = null, ArrayPool<byte>? pool = null)
            => SetRequest(request ?? DefaultRequest, pool, default);

        private static readonly byte[] DefaultRequest = Encoding.UTF8.GetBytes("*1\r\n$4\r\nPING\r\n");
    }

    private static ReadOnlySpan<byte> Hello => "$5\r\nhello\r\n"u8;

    [Fact]
    public void AParsedReplyCompletesTheOperation()
    {
        var message = new StringMessage().Arm();
        var operation = new RespOperation<string?>(message);

        Assert.False(operation.IsCompleted);
        Assert.True(message.TrySetResult(message.Token, Hello));
        Assert.True(operation.IsCompletedSuccessfully);

        Assert.Equal("hello", operation.GetResult());
    }

    [Fact]
    public void ADiscardingMessageIgnoresItsReply()
    {
        // fire-and-forget's shape: there is no parser, so the reply is dropped and the result is default
        var message = new StringMessage(RespParseOptions.Discard).Arm();
        Assert.True(message.TrySetResult(message.Token, Hello));
        Assert.Null(message.GetResult(message.Token));
    }

    [Fact]
    public void OnlyTheFirstOutcomeCounts()
    {
        var message = new StringMessage().Arm();
        var token = message.Token;

        Assert.True(message.TrySetResult(token, Hello));
        Assert.False(message.TrySetResult(token, "$5\r\nworld\r\n"u8));
        Assert.False(message.TrySetException(token, new InvalidOperationException("late")));
        Assert.False(message.TrySetCanceled(token));

        Assert.Equal("hello", message.GetResult(token));
    }

    [Fact]
    public void ConsumingTheResultMovesTheToken()
    {
        // the version moves at GetResult rather than at reuse, so a second read fails immediately rather
        // than appearing to work under light load
        var message = new StringMessage().Arm();
        var token = message.Token;

        message.TrySetResult(token, Hello);
        Assert.Equal("hello", message.GetResult(token));

        Assert.NotEqual(token, message.Token);
        Assert.Throws<InvalidOperationException>(() => message.GetResult(token));
    }

    [Fact]
    public void AStaleHolderCannotCompleteTheNextLife()
    {
        // THE hazard that pooling introduces: something still holding the previous token must not be
        // able to answer the command that now occupies this instance
        var message = new StringMessage().Arm();
        var stale = message.Token;

        message.TrySetResult(stale, Hello);
        Assert.Equal("hello", message.GetResult(stale));

        message.Arm(); // a second life, as a pool would hand out
        var live = message.Token;
        Assert.NotEqual(stale, live);

        Assert.False(message.TrySetResult(stale, "$5\r\nwrong\r\n"u8));
        Assert.False(message.TrySetException(stale, new InvalidOperationException("wrong")));
        Assert.False(message.TrySetCanceled(stale));

        Assert.True(message.TrySetResult(live, "$5\r\nright\r\n"u8));
        Assert.Equal("right", message.GetResult(live));
    }

    [Fact]
    public void ARecycledMessageStillKnowsHowToParse()
    {
        // the parse capability belongs to the type, not the life; a reset that cleared it would make
        // every command after the first silently return default
        var message = new StringMessage().Arm();
        message.TrySetResult(message.Token, Hello);
        Assert.Equal("hello", message.GetResult(message.Token));

        message.Arm();
        message.TrySetResult(message.Token, "$5\r\nagain\r\n"u8);
        Assert.Equal("again", message.GetResult(message.Token));
    }

    [Fact]
    public void ADefiniteOutcomeIsRecyclableAndAnIndefiniteOneIsNot()
    {
        var parsed = new StringMessage().Arm();
        parsed.TrySetResult(parsed.Token, Hello);
        Assert.True(parsed.IsRecyclable);

        var serverError = new StringMessage().Arm();
        serverError.TrySetException(serverError.Token, new InvalidOperationException("ERR nope"), definite: true);
        Assert.True(serverError.IsRecyclable);

        var canceled = new StringMessage().Arm();
        canceled.TrySetCanceled(canceled.Token);
        Assert.True(canceled.IsRecyclable);

        // a connection fault says nothing about whether the write is still in flight
        var fault = new StringMessage().Arm();
        fault.TrySetException(fault.Token, new IOException_("connection reset"), definite: false);
        Assert.False(fault.IsRecyclable);
    }

    [Fact]
    public void OnlyDefiniteOutcomesOfferTheInstanceBack()
    {
        var definite = new StringMessage().Arm();
        definite.TrySetResult(definite.Token, Hello);
        definite.GetResult(definite.Token);
        Assert.Equal(1, definite.Recycled);

        var indefinite = new StringMessage().Arm();
        indefinite.TrySetException(indefinite.Token, new IOException_("reset"), definite: false);
        Assert.Throws<IOException_>(() => indefinite.GetResult(indefinite.Token));
        Assert.Equal(0, indefinite.Recycled);
        Assert.Equal(1, indefinite.Resets); // still cleared, for GC - just not offered to a pool
    }

    [Fact]
    public void AFailedParseFaultsTheCallerButStaysDefinite()
    {
        // the server answered; that we could not read it is our problem, and the queue is still done
        var message = new ThrowingMessage().Arm();
        Assert.True(message.TrySetResult(message.Token, Hello));
        Assert.True(message.IsRecyclable);
        Assert.Throws<FormatException>(() => message.GetResult(message.Token));
    }

    private sealed class ThrowingMessage : RespMessageBase<string?>
    {
        protected override string? Parse(ref RespReader reader) => throw new FormatException("bad");

        internal ThrowingMessage Arm()
        {
            SetRequest(Encoding.UTF8.GetBytes("*1\r\n$4\r\nPING\r\n"), null, default);
            Assert.True(TryReserveRequest(Token, out _));
            ReleaseRequest();
            return this;
        }
    }

    [Fact]
    public async Task TheOperationCanBeAwaitedAsAValueTask()
    {
        var message = new StringMessage().Arm();
        ValueTask<string?> pending = new RespOperation<string?>(message);

        Assert.False(pending.IsCompleted);
        message.TrySetResult(message.Token, Hello);

        Assert.Equal("hello", await pending);
    }

    [Fact]
    public async Task TheOperationCanBeAwaitedDirectly()
    {
        var message = new StringMessage().Arm();
        var operation = new RespOperation<string?>(message);

        var consumer = Task.Run(async () => await operation);
        while (!operation.IsCompleted && !message.TrySetResult(message.Token, Hello))
        {
            await Task.Yield();
        }

        Assert.Equal("hello", await consumer);
    }

    [Fact]
    public void DroppingTheResultTypeIsAReinterpret()
    {
        // RespOperation<T> and RespOperation must stay layout-identical: the batch API holds untyped
        // handles while each element still completes its own typed caller
        var message = new StringMessage().Arm();
        var typed = new RespOperation<string?>(message);
        RespOperation untyped = typed;

        Assert.Same(message, untyped.Message);
        Assert.Equal(typed.Token, untyped.Token);

        message.TrySetResult(message.Token, Hello);
        Assert.True(untyped.IsCompletedSuccessfully);
    }

    [Fact]
    public void ConfigureAwaitKeepsTheTokenItWasGiven()
    {
        // re-reading the message's token here would rebind a stale handle to a later life
        var message = new StringMessage().Arm();
        var operation = new RespOperation<string?>(message);
        var token = operation.Token;

        Assert.Equal(token, operation.ConfigureAwait(false).Token);
        Assert.Equal(token, operation.ConfigureAwait(true).Token);
    }

    [Fact]
    public void AnUnsentOperationCannotBeAwaited()
    {
        // a batched command has no meaning until the batch is executed; saying so beats hanging
        var message = new StringMessage();
        message.SetRequestForTest();
        var operation = new RespOperation<string?>(message);

        var ex = Assert.Throws<InvalidOperationException>(() => operation.IsCompleted);
        Assert.Contains("has not been sent", ex.Message);
    }

    [Fact]
    public void TheRequestBufferReturnsToThePoolExactlyOnce()
    {
        var pool = new CountingPool();
        var message = new StringMessage();
        message.SetRequestForTest(pool.Rent(32), pool); // SetRequest itself takes the first reference

        Assert.True(message.TryReserveRequest(message.Token, out _));  // 2
        Assert.True(message.TryReserveRequest(message.Token, out _));  // 3
        message.ReleaseRequest();                                      // 2
        message.ReleaseRequest();                                      // 1
        Assert.Equal(0, pool.Returned);

        message.ReleaseRequest();                                      // 0
        Assert.Equal(1, pool.Returned); // the last reference hands the bytes back

        Assert.False(message.TryReserveRequest(message.Token, out _)); // and nobody can take another
        Assert.Throws<InvalidOperationException>(message.ReleaseRequest);
    }

    [Fact]
    public void AWriterStillHoldingTheRequestOutlivesTheReset()
    {
        // the reply can land - and the caller can consume it - while the writer is still mid-write and
        // holding a reservation. Reset must drop only ITS OWN reference: clearing the buffer fields
        // outright strands the writer, whose Release then has nothing to hand back, and the rented array
        // is leaked rather than pooled.
        var pool = new CountingPool();
        var message = new StringMessage();
        message.SetRequestForTest(pool.Rent(32), pool);
        var token = message.Token;

        Assert.True(message.TryReserveRequest(token, out _)); // the writer takes the bytes

        message.TrySetResult(token, Hello);
        Assert.Equal("hello", message.GetResult(token));      // the caller consumes, and Reset runs
        Assert.Equal(0, pool.Returned);                       // the writer still has them

        message.ReleaseRequest();                             // the writer finishes
        Assert.Equal(1, pool.Returned);
    }

    [Fact]
    public void AStaleHolderCannotReserveTheRequest()
    {
        var message = new StringMessage().Arm();
        var stale = message.Token;
        message.TrySetResult(stale, Hello);
        message.GetResult(stale);

        message.Arm();
        Assert.False(message.TryReserveRequest(stale, out _));
        Assert.True(message.TryReserveRequest(message.Token, out _));
        message.ReleaseRequest();
    }

    [Fact]
    public void CancellationCompletesTheOperation()
    {
        using var cts = new CancellationTokenSource();
        var message = new StringMessage().Arm(cts.Token);

        cts.Cancel();

        Assert.True(message.IsRecyclable); // observed cancellation is a definite outcome
        var ex = Assert.Throws<OperationCanceledException>(() => message.GetResult(message.Token));
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public void ARepliedOperationIgnoresLaterCancellation()
    {
        // the reply won; cancelling afterwards must not turn a delivered result into a fault
        using var cts = new CancellationTokenSource();
        var message = new StringMessage().Arm(cts.Token);

        Assert.True(message.TrySetResult(message.Token, Hello));
        cts.Cancel();

        Assert.Equal("hello", message.GetResult(message.Token));
    }

    [Fact]
    public void CancellingAfterRecycleDoesNotTouchTheNextLife()
    {
        // the registration is dropped when the outcome is claimed, so a token cancelled later has
        // nothing to fire at - and even if it did, the claim is version-checked
        using var cts = new CancellationTokenSource();
        var message = new StringMessage().Arm(cts.Token);

        message.TrySetResult(message.Token, Hello);
        Assert.Equal("hello", message.GetResult(message.Token));

        message.Arm(); // second life, NOT bound to cts
        cts.Cancel();

        Assert.True(message.TrySetResult(message.Token, "$4\r\nsafe\r\n"u8));
        Assert.Equal("safe", message.GetResult(message.Token));
    }

    [Fact]
    public async Task ExactlyOneOfAReplyAndACancellationWins()
    {
        // the race the flag word exists to settle: run it enough times to see both winners
        var replies = 0;
        var cancels = 0;

        for (var i = 0; i < 500; i++)
        {
            using var cts = new CancellationTokenSource();
            var message = new StringMessage().Arm(cts.Token);
            var token = message.Token;

            var ready = new ManualResetEventSlim();
            var canceller = Task.Run(() =>
            {
                ready.Wait();
                cts.Cancel();
            });

            ready.Set();
            var delivered = message.TrySetResult(token, Hello);
            await canceller;

            if (delivered)
            {
                replies++;
                Assert.Equal("hello", message.GetResult(token));
            }
            else
            {
                cancels++;
                Assert.Throws<OperationCanceledException>(() => message.GetResult(token));
            }
        }

        Assert.Equal(500, replies + cancels); // the point: never both, never neither
    }

    [Fact]
    public void ASynchronousWaiterIsWokenByTheReply()
    {
        var message = new StringMessage().Arm();
        var token = message.Token;

        var writer = Task.Run(async () =>
        {
            await Task.Delay(50);
            message.TrySetResult(token, Hello);
        });

        Assert.Equal("hello", message.Wait(token, TimeSpan.FromSeconds(10)));
        writer.Wait();
    }

    [Fact]
    public void ASynchronousWaitCanTimeOutAndIsThenNotRecyclable()
    {
        // "timeouts are undefined chaos": the pipeline may still write and complete this, so the
        // instance must not go back to a pool
        var message = new StringMessage().Arm();

        Assert.Throws<TimeoutException>(() => message.Wait(message.Token, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(0, message.Recycled);
    }

    [Fact]
    public void AnUnsentMessageCannotBeWaitedOn()
    {
        var message = new StringMessage();
        message.SetRequestForTest();
        Assert.Throws<InvalidOperationException>(() => message.Wait(message.Token, TimeSpan.FromMilliseconds(10)));
    }

    private sealed class CountingPool : ArrayPool<byte>
    {
        internal int Returned;

        public override byte[] Rent(int minimumLength) => new byte[minimumLength];

        public override void Return(byte[] array, bool clearArray = false) => Interlocked.Increment(ref Returned);
    }

    /// <summary>A distinct exception type, so the tests cannot pass by catching something else.</summary>
    private sealed class IOException_(string message) : Exception(message);
}
