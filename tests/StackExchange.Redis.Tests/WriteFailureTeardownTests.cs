using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class WriteFailureTeardownTests(ITestOutputHelper output) : TestBase(output)
{
    private sealed class ThrowingMessage : Message
    {
        private readonly Exception _toThrow;
        public ThrowingMessage(int db, CommandFlags flags, RedisCommand command, Exception toThrow)
            : base(db, flags, command)
        {
            _toThrow = toThrow;
        }

        public override int ArgCount => 0;

        protected override void WriteImpl(PhysicalConnection physical) => throw _toThrow;
    }

    [Fact]
    public void WriteTo_PropagatesWriteImplException()
    {
        var inner = new InvalidOperationException("simulated write failure");
        var msg = new ThrowingMessage(-1, CommandFlags.None, RedisCommand.PING, inner);

        // The new behavior: WriteTo must rethrow so the bridge's outer catch can record a connection
        // failure. Passing null for physical is safe because WriteTo null-conditionals every member
        // access on it.
        var thrown = Assert.Throws<InvalidOperationException>(() => msg.WriteTo(null!));
        Assert.Same(inner, thrown);
    }

    [Fact]
    public void WriteTo_DoesNotWrapRedisCommandException()
    {
        // RedisCommandException is excluded from the catch filter (it carries its own meaning),
        // so it must surface unchanged from WriteImpl through WriteTo.
        var inner = new RedisCommandException("intentional");
        var msg = new ThrowingMessage(-1, CommandFlags.None, RedisCommand.PING, inner);

        var thrown = Assert.Throws<RedisCommandException>(() => msg.WriteTo(null!));
        Assert.Same(inner, thrown);
    }

    [Fact]
    public async Task WriteFailure_TearsDownPhysicalConnection()
    {
        // We deliberately raise InternalError + ConnectionFailed events here, so don't fail
        // the test on ambient failures.
        SetExpectedAmbientFailureCount(-1);

        await using var conn = Create(shared: false, allowAdmin: true);

        int failedCount = 0;
        ConnectionFailureType? observedFailure = null;
        conn.ConnectionFailed += (_, e) =>
        {
            Interlocked.Increment(ref failedCount);
            observedFailure ??= e.FailureType;
        };

        await conn.GetDatabase().PingAsync();

        var muxer = conn.UnderlyingMultiplexer;
        var server = muxer.GetServerSnapshot()[0];

        var boom = new InvalidOperationException("simulated WriteImpl failure");
        var throwingMsg = new ThrowingMessage(-1, CommandFlags.None, RedisCommand.PING, boom);
        muxer.CheckMessage(throwingMsg);

        var sendTask = muxer.ExecuteAsyncImpl(throwingMsg, ResultProcessor.ResponseTimer, state: null, server);

        // The throwing message should fault the awaiter (HandleWriteException calls SetExceptionAndComplete),
        // wrapping the inner exception in a RedisConnectionException with InternalFailure.
        var redisEx = await Assert.ThrowsAsync<RedisConnectionException>(async () => await sendTask);
        Assert.Equal(ConnectionFailureType.InternalFailure, redisEx.FailureType);

        // The new behavior: HandleWriteException calls RecordConnectionFailed on the physical
        // connection, which raises ConnectionFailed. Before this fix, no failure was raised
        // and the connection was left corrupt — the next response could match the wrong
        // in-flight message.
        await UntilConditionAsync(TimeSpan.FromSeconds(3), () => Volatile.Read(ref failedCount) > 0);
        Assert.True(Volatile.Read(ref failedCount) > 0, "ConnectionFailed event did not fire after write failure");
        Assert.Equal(ConnectionFailureType.InternalFailure, observedFailure);
    }
}
