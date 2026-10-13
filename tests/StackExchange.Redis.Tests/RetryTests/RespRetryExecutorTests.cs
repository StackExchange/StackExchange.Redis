using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

/// <summary>
/// Retry as a decorator on the executor, which is how the context surface gets it.
/// </summary>
/// <remarks>
/// <para>
/// <c>RetryDatabase</c> replays a <i>method call</i>, so it has to capture every argument into a
/// generated state struct and own pooled copies of anything the caller might reuse. This replays a
/// rendered frame, which already exists and is already owned - so what is left to test is the loop, the
/// policy hand-off, and the one property that is not obvious: <b>the frame is still readable on the
/// second attempt</b>.
/// </para>
/// <para>
/// The last of those is worth stating precisely, because it is a lifetime question and lifetime questions
/// in this codebase have a habit of failing far away. The caller of
/// <c>RespExecutorBase.SendAsync</c> - <c>RespExecutor.AwaitUncached</c> - holds one reference and disposes
/// it in a <c>finally</c> once the send completes; for a retrying executor that single reference spans
/// every attempt, so no attempt has to retain and none may dispose. <c>RefCountedBuffer</c> throws on a
/// span taken after the last reference has gone, so a replay that got this wrong would throw here rather
/// than read somebody else's rent.
/// </para>
/// </remarks>
public class RespRetryExecutorTests
{
    /// <summary>Fails the first <c>failures</c> sends with a transient fault, then succeeds.</summary>
    private sealed class FlakyExecutor(int failures, CommandStatus status = CommandStatus.WaitingToBeSent) : RespExecutorBase
    {
        private int _sent;

        /// <summary>The bytes of every attempt, so a replay can be compared with the original.</summary>
        public List<string> Sent { get; } = [];

        public override int Database => 0;

        /// <summary>How many sends came through the synchronous path.</summary>
        public int SyncSends { get; private set; }

        // the same script either way: the synchronous path throws what the asynchronous one faults with
        public override RespPayload Send(in RespRequest request)
        {
            SyncSends++;
            var pending = SendAsync(request);
            return pending.IsCompletedSuccessfully ? pending.Result : throw pending.AsTask().Exception!.InnerException!;
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            // reading the span is the point: on attempt two this is a buffer somebody could have freed
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));

            if (_sent++ < failures)
            {
                return new ValueTask<RespPayload>(Task.FromException<RespPayload>(Transient(status)));
            }

            return new ValueTask<RespPayload>(RespPayload.Create(Encoding.UTF8.GetBytes("$4\r\nmarc\r\n")));
        }
    }

    /// <summary>
    /// A fault the default policy agrees to retry: a socket failure on a message that never left.
    /// </summary>
    /// <remarks>
    /// <c>WaitingToBeSent</c> is what makes it retryable regardless of the command's category -
    /// <c>FaultContext.NotApplied</c> - which is the case <c>CommandRetryPolicyUnitTests</c> pins. Using
    /// it here keeps this file about the loop rather than about the policy.
    /// </remarks>
    private static Exception Transient(CommandStatus status = CommandStatus.WaitingToBeSent)
        => new RedisConnectionException(
            ConnectionFailureType.SocketFailure,
            CommandFlags.CommandRetryWriteAccumulating,
            "boom",
            innerException: null,
            commandStatus: status);

    /// <summary>No delay between attempts, so the tests are about the loop rather than the clock.</summary>
    private static RetryPolicy Fast(int maxAttempts) => new RetryPolicy.Builder
    {
        MaxAttempts = maxAttempts,
        RetryDelay = TimeSpan.Zero,
        JitterMax = TimeSpan.Zero,
    }.Create();

    private static RespDatabaseContext Target(RespExecutorBase executor, RetryPolicy policy)
        => new RespDatabaseContext(new RespContext().WithExecutor(executor)).WithRetry(policy);

    [Fact]
    public async Task ATransientFaultIsRetriedAndTheFrameSurvivesTheReplay()
    {
        var executor = new FlakyExecutor(failures: 2);

        Assert.Equal("marc", (string?)await Target(executor, Fast(5)).Strings.GetAsync("k"));

        // three attempts, and every one of them read the same bytes off the same buffer
        Assert.Equal(3, executor.Sent.Count);
        Assert.All(executor.Sent, sent => Assert.Equal("*2|$3|GET|$1|k|", sent));
    }

    [Fact]
    public async Task AttemptsAreCappedAndTheLastFaultPropagates()
    {
        var executor = new FlakyExecutor(failures: 10);

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(
            async () => await Target(executor, Fast(3)).Strings.GetAsync("k"));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(3, executor.Sent.Count); // the cap is attempts, not retries
    }

    /// <summary>A fault the policy will not replay is not replayed.</summary>
    /// <remarks>
    /// <c>Sent</c> rather than <c>WaitingToBeSent</c>: the command may have taken effect, so the category
    /// applies again - and <c>CommandRetryWriteAccumulating</c> is beyond what the default policy allows.
    /// The decision is entirely <c>RetryPolicy</c>'s; what this pins is that the executor asks.
    /// </remarks>
    [Fact]
    public async Task AFaultThePolicyRefusesIsNotRetried()
    {
        var executor = new FlakyExecutor(failures: 10, status: CommandStatus.Sent);

        await Assert.ThrowsAsync<RedisConnectionException>(
            async () => await Target(executor, Fast(5)).Strings.GetAsync("k"));

        Assert.Single(executor.Sent);
    }

    /// <summary>A send that works first time costs nothing extra.</summary>
    /// <remarks>
    /// The executor starts the first attempt eagerly and hands back the inner <c>ValueTask</c> untouched
    /// when it completed synchronously, so the common case adds no state machine. Asserting the task is
    /// already complete is how that shows up from outside.
    /// </remarks>
    [Fact]
    public void ASynchronousSuccessIsNotWrapped()
    {
        var pending = Target(new FlakyExecutor(failures: 0), Fast(5)).Strings.GetAsync("k");

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal("marc", (string?)pending.GetAwaiter().GetResult());
    }

    /// <summary>
    /// A synchronous send retries too, through the inner executor's synchronous send.
    /// </summary>
    /// <remarks>
    /// It used to refuse, because every pause was asynchronous; but a synchronous caller has already agreed
    /// to block, and refusing made a retrying context the one kind a blocking context could not serve.
    /// </remarks>
    [Fact]
    public void ASynchronousSendRetries()
    {
        var executor = new FlakyExecutor(failures: 2);

        Assert.Equal("marc", (string?)Target(executor, Fast(5)).Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));

        Assert.Equal(3, executor.SyncSends); // every attempt synchronous: nothing went the asynchronous way
        Assert.All(executor.Sent, sent => Assert.Equal("*2|$3|GET|$1|k|", sent));
    }

    [Fact]
    public void ASynchronousSendCapsAttemptsAndPropagatesTheLastFault()
    {
        var executor = new FlakyExecutor(failures: 10);

        var ex = Assert.Throws<RedisConnectionException>(
            () => Target(executor, Fast(3)).Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(3, executor.SyncSends);
    }

    [Fact]
    public void ASynchronousSendHonoursAPolicyThatRefuses()
    {
        var executor = new FlakyExecutor(failures: 10, status: CommandStatus.Sent);

        Assert.Throws<RedisConnectionException>(
            () => Target(executor, Fast(5)).Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));

        Assert.Equal(1, executor.SyncSends);
    }

    /// <summary>
    /// A blocking context over a retrying one blocks, retries included: the group method hands back a task
    /// that has already completed, which is what a library's synchronous method relies on.
    /// </summary>
    [Fact]
    public void ABlockingContextRetriesAndCompletesInline()
    {
        var executor = new FlakyExecutor(failures: 2);

        var done = Target(executor, Fast(5)).Blocking().Strings.GetAsync("k");

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal("marc", (string?)done.GetAwaiter().GetResult());
        Assert.Equal(3, executor.SyncSends);
    }

    /// <summary>The synchronous pause is a real pause: the policy's delay still separates the attempts.</summary>
    [Fact]
    public void ASynchronousRetryWaitsTheConfiguredDelay()
    {
        var executor = new FlakyExecutor(failures: 2);
        var policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.FromMilliseconds(50), JitterMax = TimeSpan.Zero }.Create();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("marc", (string?)Target(executor, policy).Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));

        Assert.True(watch.ElapsedMilliseconds >= 90, $"two pauses of 50ms took {watch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// A policy that can never retry is forwarded, not looped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attempt cap is tested before the policy is consulted, so a single-attempt controller refuses
    /// every fault without asking - which makes "could this ever retry?" an exact question rather than a
    /// guess, and safe to answer once per executor instead of once per fault. What it buys is the async
    /// machinery: no eager start, no state machine, the inner task handed straight back.
    /// </para>
    /// <para>
    /// <b>The tempting bigger short-circuit is not safe.</b> Reading the command's retry category and
    /// skipping the loop for <c>CommandRetryNever</c> would catch far more sends - but
    /// <see cref="RetryPolicy.CanRetry"/> is virtual, and a derived policy may ignore the category
    /// entirely, so the executor would be deciding something the policy had reserved for itself.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APolicyThatCanNeverRetryIsForwardedWithoutTheLoop()
    {
        var executor = new FlakyExecutor(failures: 1);

        await Assert.ThrowsAsync<RedisConnectionException>(
            async () => await Target(executor, Fast(1)).Strings.GetAsync("k"));

        Assert.Single(executor.Sent);
    }

    /// <summary>And a faulted send is faulted, not wrapped, on that path.</summary>
    /// <remarks>
    /// The forward hands back the inner <see cref="ValueTask{TResult}"/> as it is, so a fault that arrived
    /// synchronously is still a synchronously-faulted task - which is what says nothing was interposed.
    /// </remarks>
    [Fact]
    public void ANeverRetryingSendIsNotWrapped()
    {
        var pending = Target(new FlakyExecutor(failures: 1), Fast(1))
            .Raw.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None);

        Assert.True(pending.IsFaulted);
    }

    /// <summary>
    /// A command whose own category forbids replay is forwarded, and the decision is taken before sending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fixture's fault would otherwise have been retried</b>, which is what makes this show the
    /// request-side veto rather than the fault-side one: it is a socket failure on a message that never
    /// left, so <c>FaultContext.NotApplied</c> would have bypassed the category cap and the policy would
    /// have said yes. One send says the question was never asked.
    /// </para>
    /// <para>
    /// <c>WithDefaultCategory</c> is caller-wins, so an explicit <c>CommandRetryNever</c> survives the
    /// categorisation the send path applies from the command table.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACommandThatForbidsReplayIsForwardedWithoutTheLoop()
    {
        var executor = new FlakyExecutor(failures: 1);

        await Assert.ThrowsAsync<RedisConnectionException>(
            async () => await Target(executor, Fast(5)).Strings.GetAsync("k", CommandFlags.CommandRetryNever));

        Assert.Single(executor.Sent);
    }

    /// <summary>And the same command without that flag does retry, so the veto is what did it.</summary>
    [Fact]
    public async Task TheSameCommandWithoutTheVetoIsRetried()
    {
        var executor = new FlakyExecutor(failures: 1);

        Assert.Equal("marc", (string?)await Target(executor, Fast(5)).Strings.GetAsync("k"));
        Assert.Equal(2, executor.Sent.Count);
    }

    /// <summary>
    /// Fire-and-forget is not retried, because nobody is waiting for the outcome to improve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retry exists to improve an outcome somebody is waiting for. A replayed fire-and-forget cannot be
    /// observed to have helped, and the cost - a backoff delay on a call advertised as returning
    /// immediately - can. <c>RespOperationBatchExecutor</c> makes the same point structurally: a fire-and-forget
    /// there is answered before it is sent, so nothing that fails afterwards has anybody to tell.
    /// </para>
    /// <para>
    /// <b>Against the real pipeline this changes nothing</b>, which is exactly why it is asserted here
    /// rather than assumed: a fire-and-forget message has no result box, so
    /// <c>ConnectionMultiplexer.ThrowFailed</c> swallows its write failure and nothing ever faults. The
    /// fake below does fault one, which is the only way to show the veto is real - and the only way a
    /// future executor that started faulting them would be caught quietly adding delays.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FireAndForgetIsNotRetried()
    {
        var executor = new FlakyExecutor(failures: 1);

        await Assert.ThrowsAsync<RedisConnectionException>(
            async () => await Target(executor, Fast(5)).Strings.GetAsync("k", CommandFlags.FireAndForget));

        Assert.Single(executor.Sent);
    }

    [Fact]
    public void RetryCannotBeNested()
    {
        var once = Target(new FlakyExecutor(failures: 0), Fast(5));

        var ex = Assert.Throws<InvalidOperationException>(() => once.WithRetry(Fast(2)));
        Assert.Contains("already retrying", ex.Message);
    }

    [Fact]
    public void AContextWithNoExecutorHasNothingToRetry()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new RespDatabaseContext(new RespContext()).WithRetry());
        Assert.Contains("no executor", ex.Message);
    }

    /// <summary>The wrapping is a new context; the one it was built from still has no retry.</summary>
    /// <remarks>
    /// The contexts are immutable, so this is really a statement about <c>WithExecutor</c> - but it is
    /// the property a caller relies on when they hold both, and it is one line to pin.
    /// </remarks>
    [Fact]
    public async Task WrappingDoesNotChangeTheOriginalContext()
    {
        var executor = new FlakyExecutor(failures: 1);
        var plain = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        _ = plain.WithRetry(Fast(5));

        await Assert.ThrowsAsync<RedisConnectionException>(async () => await plain.Strings.GetAsync("k"));
        Assert.Single(executor.Sent);
    }

    /// <summary>
    /// A retrying <b>context</b> gets its next-failover token from the executor chain, so failover-aware
    /// retry works without going through <see cref="RetryDatabase"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before this, <c>WithRetry</c> on a context had nowhere to get one: the token was only reachable via
    /// <c>IDatabaseAsync.GetNextFailover()</c>, so a context-level retry ran with
    /// <c>TracksFailover</c> false and the failover rungs of the policy quietly unreachable.
    /// </para>
    /// <para>
    /// <b>Only the group executor can answer</b>, and that is not an accident of layering: moving between
    /// members IS the failover, and the group is the thing that moves. Everything below it is a single
    /// multiplexer, which has nothing to fail over to.
    /// </para>
    /// </remarks>
    [Fact]
    public void AContextRetryTakesItsFailoverSourceFromTheChain()
    {
        var inner = new FakeExecutor(":1\r\n");
        using CancellationTokenSource first = new(), second = new();

        var current = first;
        var group = new RespGroupExecutor(() => inner, nextFailover: () => current.Token);

        var retrying = new RespDatabaseContext(new RespContext().WithExecutor(group)).WithRetry(RetryPolicy.Default);
        var executor = Assert.IsType<RespRetryExecutor>(retrying.Raw.Executor);

        var source = executor.GetFailoverSource();
        Assert.NotNull(source);
        Assert.Equal(first.Token, source!());

        // and it is re-fetched rather than captured: a failover REPLACES the token, so anything holding the
        // old one would be watching the failover that already happened instead of the next one
        current = second;
        Assert.Equal(second.Token, source!());
    }

    /// <summary>And a chain with no group in it reports no source at all, rather than one that never fires.</summary>
    /// <remarks>
    /// The distinction is load-bearing: <c>RetryController</c> treats "no failover source" as "the failover
    /// rungs are unreachable", where a token that simply never fires would leave the policy waiting for
    /// something that cannot arrive.
    /// </remarks>
    [Fact]
    public void AChainWithNoGroupHasNoFailoverSource()
    {
        var retrying = new RespDatabaseContext(
            new RespContext().WithExecutor(new FakeExecutor(":1\r\n"))).WithRetry(RetryPolicy.Default);

        Assert.Null(Assert.IsType<RespRetryExecutor>(retrying.Raw.Executor).GetFailoverSource());
    }
}
