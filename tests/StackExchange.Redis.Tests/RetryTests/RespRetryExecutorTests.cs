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
/// <c>IRespExecutor.SendAsync</c> - <c>RespExecutor.AwaitUncached</c> - holds one reference and disposes
/// it in a <c>finally</c> once the send completes; for a retrying executor that single reference spans
/// every attempt, so no attempt has to retain and none may dispose. <c>RefCountedBuffer</c> throws on a
/// span taken after the last reference has gone, so a replay that got this wrong would throw here rather
/// than read somebody else's rent.
/// </para>
/// </remarks>
public class RespRetryExecutorTests
{
    /// <summary>Fails the first <c>failures</c> sends with a transient fault, then succeeds.</summary>
    private sealed class FlakyExecutor(int failures, CommandStatus status = CommandStatus.WaitingToBeSent) : IRespExecutor
    {
        private int _sent;

        /// <summary>The bytes of every attempt, so a replay can be compared with the original.</summary>
        public List<string> Sent { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
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

    private static RespDatabaseContext Target(IRespExecutor executor, RetryPolicy policy)
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
    /// A synchronous send through a retrying context refuses, rather than quietly not retrying.
    /// </summary>
    /// <remarks>
    /// The same position the shipped retrying database takes by implementing <see cref="IDatabaseAsync"/>
    /// and not <see cref="IDatabase"/>: every pause a retry takes is asynchronous, so there is no honest
    /// synchronous answer. Silently forwarding would lose the retry invisibly, which is the failure
    /// <c>RetryDatabase.GetContextCore</c> refuses to ship.
    /// </remarks>
    [Fact]
    public void ASynchronousSendRefuses()
    {
        // through the raw context, because the group surface has no synchronous spelling to reach it by -
        // which is itself most of the reason refusing here is tolerable
        var target = Target(new FlakyExecutor(failures: 0), Fast(5));

        var ex = Assert.Throws<InvalidOperationException>(
            () => target.Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));
        Assert.Contains("no synchronous send", ex.Message);
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
    /// immediately - can. <c>RespBatchExecutor</c> makes the same point structurally: a fire-and-forget
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
}
