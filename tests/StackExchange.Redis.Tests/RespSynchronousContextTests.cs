using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>Synchronous()</c>: a context whose sends complete before they return, so a command group's asynchronous
/// method serves a synchronous caller - <c>.GetAwaiter().GetResult()</c> - without a synchronous twin.
/// </summary>
public class RespSynchronousContextTests
{
    private static readonly RespCommand Ping = "PING".Command();

    /// <summary>Answers a blocking send at once, and never answers an asynchronous one: which path ran is visible.</summary>
    private sealed class SyncOnlyExecutor(params string[] replies) : FakeExecutor(replies)
    {
        public override ValueTask<RespPayload> SendAsync(RespRequest request, System.Threading.CancellationToken cancellationToken = default)
            => new(new TaskCompletionSource<RespPayload>().Task); // never completes
    }

    private static RespDatabaseContext Context(FakeExecutor executor) => new(new RespContext().WithExecutor(executor));

    [Fact]
    public void AnOrdinaryContextLeavesTheSendPending()
    {
        // the control: without Synchronous(), the group method goes the asynchronous way
        var pending = Context(new SyncOnlyExecutor("$1\r\nv\r\n")).Strings.GetAsync("k");
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public void ASynchronousContextCompletesBeforeReturning()
    {
        var executor = new SyncOnlyExecutor("$1\r\nv\r\n");
        var done = Context(executor).Synchronous().Strings.GetAsync("k");

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal("v", (string?)done.GetAwaiter().GetResult());
        Assert.Equal("*2|$3|GET|$1|k|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void TheVoidSendCompletesToo()
    {
        var ctx = (RespContext)Context(new SyncOnlyExecutor("+PONG\r\n")).Synchronous();
        Assert.True(ctx.SendAsync($"{Ping}").IsCompletedSuccessfully);
    }

    [Fact]
    public void AFailureIsAFaultedTaskNotAThrow()
    {
        // as from the asynchronous path: a caller may hold the task before reading it
        var done = Context(new SyncOnlyExecutor("-ERR boom\r\n")).Synchronous().Strings.GetAsync("k");

        Assert.True(done.IsFaulted);

        // RespException here, because the fake hands back raw payloads; the real connection turns an error reply
        // into a RedisServerException before this point
        Assert.Throws<RESPite.RespException>(() => done.GetAwaiter().GetResult());
    }

    [Fact]
    public void SynchronousComposesWithTheRestOfTheContext()
    {
        // a service like any other: a key prefix applied before or after still applies
        var executor = new SyncOnlyExecutor("$1\r\nv\r\n");
        var done = Context(executor).Synchronous().AppendKeyPrefix("t:").Strings.GetAsync("k");

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal("*2|$3|GET|$3|t:k|", Assert.Single(executor.Sent));
    }
}
