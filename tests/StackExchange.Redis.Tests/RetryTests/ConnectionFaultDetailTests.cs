using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

/// <summary>
/// When a connection dies, one exception is built to describe the *connection*, and it used to be handed
/// verbatim to every message that was in flight. Sharing one exception instance across unrelated callers is
/// dubious in itself (<c>Exception.Data</c> is mutable), and it discards the per-message facts the retry
/// machinery needs: the command's retry category, and whether this particular message had actually been
/// written. Without them nothing at all is retryable after a connection failure, not even a plain
/// <c>GET</c>. <see cref="ExceptionFactory.PerMessage"/> is what splits them apart.
/// </summary>
public class ConnectionFaultDetailTests
{
    private static RedisConnectionException SharedConnectionFault()
    {
        // as built by PhysicalConnection.RecordConnectionFailed: describes the connection, knows nothing
        // about any individual message
        var ex = new RedisConnectionException(
            ConnectionFailureType.SocketClosed,
            CommandFlags.None,
            "SocketClosed on 127.0.0.1:6379/Interactive");
        ex.Data["Redis-Version"] = "1.2.3";
        ex.Data["Redis-Server"] = "127.0.0.1:6379";
        return ex;
    }

    private static Message Read() => Message.Create(0, CommandFlags.None, RedisCommand.GET, (RedisKey)"key");

    private static Message AccumulatingWrite() => Message.Create(0, CommandFlags.None, RedisCommand.INCR, (RedisKey)"key");

    // A message that was written before the socket died: the outcome is genuinely ambiguous, so the sent
    // status must survive as-is, but the command's category has to come through - otherwise the policy sees
    // "no category" and refuses to retry even a pure read.
    [Fact]
    public void SentMessage_CarriesCategoryAndSentStatus()
    {
        var shared = SharedConnectionFault();
        var message = Read();
        message.SetRequestSent();

        var per = Assert.IsType<RedisConnectionException>(ExceptionFactory.PerMessage(shared, message));

        Assert.NotSame(shared, per);
        Assert.Equal(ConnectionFailureType.SocketClosed, per.FailureType);
        Assert.Equal(CommandStatus.Sent, per.CommandStatus);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, per.Flags & Message.MaskRetryCategory);

        var ctx = new FaultContext(per);
        Assert.False(ctx.NotApplied); // it was on the wire; we cannot know whether the server ran it
        Assert.NotEqual(RetryResult.None, RetryPolicy.Default.CanRetry(in ctx)); // ...but a read is safe

        // for contrast: the shared exception the message used to receive is retryable for nothing at all
        var sharedCtx = new FaultContext(shared);
        Assert.Equal(RetryResult.None, RetryPolicy.Default.CanRetry(in sharedCtx));
    }

    // Same situation, accumulating write: the category comes through and correctly *blocks* the retry, since
    // a replay could double-apply. The caller can still opt in by raising the cap.
    [Fact]
    public void SentAccumulatingWrite_RemainsGatedByCategory()
    {
        var message = AccumulatingWrite();
        message.SetRequestSent();

        var per = Assert.IsType<RedisConnectionException>(ExceptionFactory.PerMessage(SharedConnectionFault(), message));
        Assert.Equal(CommandFlags.CommandRetryWriteAccumulating, per.Flags & Message.MaskRetryCategory);

        var ctx = new FaultContext(per);
        Assert.False(ctx.NotApplied);
        Assert.Equal(RetryResult.None, RetryPolicy.Default.CanRetry(in ctx));

        RetryPolicy permissive = new RetryPolicy.Builder { MaxCommandRetryCategory = CommandFlags.CommandRetryWriteAccumulating };
        Assert.NotEqual(RetryResult.None, permissive.CanRetry(in ctx));
    }

    // A message that never left the client - still waiting to be written, or sitting in the backlog - is
    // *provably* unapplied, which is the one case where even an accumulating write can be safely re-issued.
    // That fact lives on the message, so sharing the connection's exception threw it away.
    [Theory]
    [InlineData(false)] // never handed to the bridge
    [InlineData(true)] // queued in the backlog awaiting a healthy connection
    public void UnsentMessage_IsKnownNotApplied(bool backlogged)
    {
        var message = AccumulatingWrite();
        if (backlogged) message.SetBacklogged();

        var per = Assert.IsType<RedisConnectionException>(ExceptionFactory.PerMessage(SharedConnectionFault(), message));

        Assert.Equal(backlogged ? CommandStatus.WaitingInBacklog : CommandStatus.WaitingToBeSent, per.CommandStatus);

        var ctx = new FaultContext(per);
        Assert.True(ctx.NotApplied);
        // accumulating, i.e. beyond the default cap - but nothing was applied, so there is nothing to repeat
        Assert.NotEqual(RetryResult.None, RetryPolicy.Default.CanRetry(in ctx));
    }

    // The connection-level diagnostics are the useful part of these exceptions, so they have to come across;
    // but the dictionaries must be independent, or one caller's annotations show up on another's exception.
    [Fact]
    public void SharedDiagnosticsAreCopied_ButNotShared()
    {
        var shared = SharedConnectionFault();
        var first = ExceptionFactory.PerMessage(shared, Read());
        var second = ExceptionFactory.PerMessage(shared, AccumulatingWrite());

        Assert.NotSame(first, second);
        Assert.Equal(shared.Message, first.Message);
        Assert.Equal("1.2.3", first.Data["Redis-Version"]);
        Assert.Equal("1.2.3", second.Data["Redis-Version"]);

        first.Data["mine"] = "only-mine";
        Assert.False(second.Data.Contains("mine"));
        Assert.False(shared.Data.Contains("mine"));
    }

    // The per-message status is recorded in the diagnostic data too, so a user reading the exception's Data
    // sees this message's status rather than whatever the connection-level exception happened to say.
    [Fact]
    public void SentStatusIsRecordedInDiagnosticData()
    {
        var shared = SharedConnectionFault();
        shared.Data["request-sent-status"] = CommandStatus.Unknown;

        var message = Read();
        message.SetRequestSent();
        var per = ExceptionFactory.PerMessage(shared, message);

        Assert.Equal(CommandStatus.Sent, per.Data["request-sent-status"]);
    }

    // Only the shared connection-failure shape needs splitting; anything else already describes a single
    // operation, and an exception that already matches the message is passed straight through (no needless
    // allocation on a teardown that may be failing thousands of messages).
    [Fact]
    public void UnrelatedOrAlreadyMatchingExceptions_ArePassedThrough()
    {
        var message = Read();
        message.SetRequestSent();

        var serverFault = new RedisServerException(RedisErrorKind.Loading, message.Flags, "LOADING");
        Assert.Same(serverFault, ExceptionFactory.PerMessage(serverFault, message));

        var alreadySpecific = new RedisConnectionException(
            ConnectionFailureType.SocketClosed,
            message.Flags,
            "already describes this message",
            innerException: null,
            commandStatus: CommandStatus.Sent);
        Assert.Same(alreadySpecific, ExceptionFactory.PerMessage(alreadySpecific, message));
    }
}
