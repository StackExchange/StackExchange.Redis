using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>Blocking()</c>: a context whose sends complete before they return, so a command group's asynchronous
/// method serves a synchronous caller - <c>.GetAwaiter().GetResult()</c> - without a synchronous twin.
/// </summary>
public class RespBlockingContextTests
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
        // the control: without Blocking(), the group method goes the asynchronous way
        var pending = Context(new SyncOnlyExecutor("$1\r\nv\r\n")).Strings.GetAsync("k");
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public void ABlockingContextCompletesBeforeReturning()
    {
        var executor = new SyncOnlyExecutor("$1\r\nv\r\n");
        var done = Context(executor).Blocking().Strings.GetAsync("k");

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal("v", (string?)done.GetAwaiter().GetResult());
        Assert.Equal("*2|$3|GET|$1|k|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void TheVoidSendCompletesToo()
    {
        var ctx = (RespContext)Context(new SyncOnlyExecutor("+PONG\r\n")).Blocking();
        Assert.True(ctx.SendAsync($"{Ping}").IsCompletedSuccessfully);
    }

    [Fact]
    public void AFailureIsAFaultedTaskNotAThrow()
    {
        // as from the asynchronous path: a caller may hold the task before reading it
        var done = Context(new SyncOnlyExecutor("-ERR boom\r\n")).Blocking().Strings.GetAsync("k");

        Assert.True(done.IsFaulted);

        // RespException here, because the fake hands back raw payloads; the real connection turns an error reply
        // into a RedisServerException before this point
        Assert.Throws<RESPite.RespException>(() => done.GetAwaiter().GetResult());
    }

    [Fact]
    public void BlockingComposesWithTheRestOfTheContext()
    {
        // a service like any other: a key prefix applied before or after still applies
        var executor = new SyncOnlyExecutor("$1\r\nv\r\n");
        var done = Context(executor).Blocking().AppendKeyPrefix("t:").Strings.GetAsync("k");

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal("*2|$3|GET|$3|t:k|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void APairedCommandCompletesToo()
    {
        // EVALSHA behind SCRIPT LOAD goes through its own send, not the single-request funnel; this fake cannot
        // pair, so it takes the sequential path - the preamble, then the request, each answered at once
        var executor = new SyncOnlyExecutor("$40\r\ne0e1f9fabfc9d4800c877a703b823ac0578ff8db\r\n", ":1\r\n");
        var done = Context(executor).Blocking().Scripts.EvaluateAsync("return 1");

        Assert.True(done.IsCompletedSuccessfully);
        using var result = done.GetAwaiter().GetResult();
        Assert.Equal(1, (long)result.ReadScalar().ReadRedisValue());
        Assert.Equal(
            ["*3|$6|SCRIPT|$4|LOAD|$8|return 1|", "*3|$7|EVALSHA|$40|e0e1f9fabfc9d4800c877a703b823ac0578ff8db|$1|0|"],
            executor.Sent);
    }

    [Fact]
    public async Task AHashImportCompletesToo()
    {
        // the other paired command: HIMPORT PREPARE, then the row
        await using var fieldSet = HashImport.Create("a");
        var executor = new SyncOnlyExecutor("+OK\r\n");
        var done = Context(executor).Blocking().Hashes.ImportAsync("k", fieldSet, ["v"]);

        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal(2, executor.Sends);
    }

    [Fact]
    public void ARetryingContextSendsAsynchronouslyInstead()
    {
        // a retry pauses asynchronously, so it cannot block; the context falls back to the ordinary path
        // rather than refusing, and a synchronous caller blocks on that task as it always did
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor("$1\r\nv\r\n"))).WithRetry();
        var done = ctx.Blocking().Strings.GetAsync("k");

        Assert.Equal("v", (string?)done.AsTask().GetAwaiter().GetResult());
    }

    [Fact]
    public void TheSynchronousDatabaseOverARetryingContextStillWorks()
    {
        var multiplexer = NSubstitute.Substitute.For<IConnectionMultiplexer>();
        var db = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor("$1\r\nv\r\n"))).WithRetry().AsDatabase(multiplexer);

        Assert.Equal("v", (string?)db.StringGet("k"));
    }

    [Fact]
    public void TheSynchronousDatabaseSendsThroughTheBlockingPath()
    {
        // the point of the change: IDatabase's synchronous members never touch the asynchronous send
        var executor = new SyncOnlyExecutor("$1\r\nv\r\n", ":-1\r\n", "$1\r\nv\r\n"); // GET; then PTTL, GET
        var db = Context(executor).AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        Assert.Equal("v", (string?)db.StringGet("k"));
        Assert.Equal("v", (string?)db.StringGetWithExpiry("k").Value); // a composite: two sends, both blocking
    }
}
