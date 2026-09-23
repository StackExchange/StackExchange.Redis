using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Turning an out-of-band frame into a delivery, a verdict, or nothing.
/// </summary>
/// <remarks>
/// <para>
/// Parsed from the real bytes rather than a mock: the shapes differ between kinds in a way that is easy
/// to get subtly wrong. A <c>pmessage</c> carries TWO channels - the pattern that was subscribed and the
/// channel that actually matched - and they are not interchangeable: handlers are registered against the
/// pattern, while the caller is told what matched.
/// </para>
/// <para>
/// <b>Deliveries are awaited, not asserted immediately</b>: <c>OnMessage</c> hands the handler to the
/// completion manager, which may run it on a worker. Asserting inline passes or fails on a race.
/// </para>
/// </remarks>
public class RespPushDispatchTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static RespOutOfBandResult Dispatch(ConnectionMultiplexer muxer, string frame)
        => RespPushDispatch.Dispatch(Encoding.UTF8.GetBytes(frame), muxer);

    private static async Task<(string Channel, string Payload)> Delivered(
        Task<(string Channel, string Payload)> pending)
    {
        var done = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(done, pending), "no message was delivered");
        return await pending;
    }

    /// <summary>Subscribe, and hand back the first delivery as a task.</summary>
    private static Task<(string Channel, string Payload)> Listen(ConnectionMultiplexer muxer, RedisChannel channel)
    {
        var source = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        muxer.GetSubscriber().Subscribe(channel, (ch, payload) => source.TrySetResult(((string?)ch ?? "", (string?)payload ?? "")));
        return source.Task;
    }

    [Fact]
    public async Task AMessageIsDelivered()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);
        var pending = Listen(muxer, RedisChannel.Literal(Me()));

        var verdict = Dispatch(muxer, $">3\r\n$7\r\nmessage\r\n${Me().Length}\r\n{Me()}\r\n$5\r\nhello\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);
        Assert.Equal((Me(), "hello"), await Delivered(pending));
    }

    /// <summary>A pattern delivery reports the pattern subscribed AND the channel that matched.</summary>
    [Fact]
    public async Task APatternMessageCarriesBothChannels()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        var pattern = Me() + "*";
        var matched = Me() + "-actual";
        var pending = Listen(muxer, RedisChannel.Pattern(pattern));

        var verdict = Dispatch(
            muxer,
            $">4\r\n$8\r\npmessage\r\n${pattern.Length}\r\n{pattern}\r\n${matched.Length}\r\n{matched}\r\n$2\r\nhi\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);

        // the handler fired for the PATTERN, and was told the channel that matched - not the pattern again
        Assert.Equal((matched, "hi"), await Delivered(pending));
    }

    /// <summary>
    /// A sharded delivery is not the same channel as an unsharded one of the same name, so it must not be
    /// handed to a plain subscriber.
    /// </summary>
    [Fact]
    public async Task AShardedMessageIsDeliveredToTheShardedSubscription()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        var plain = Listen(muxer, RedisChannel.Literal(Me()));
        var sharded = Listen(muxer, RedisChannel.Sharded(Me()));

        var verdict = Dispatch(muxer, $">3\r\n$8\r\nsmessage\r\n${Me().Length}\r\n{Me()}\r\n$5\r\nshard\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);
        Assert.Equal((Me(), "shard"), await Delivered(sharded));
        Assert.False(plain.IsCompleted, "a sharded message reached the unsharded subscription");
    }

    /// <summary>
    /// A confirmation answers a command, so it is matched rather than consumed - otherwise the subscribe
    /// call it belongs to never completes.
    /// </summary>
    [Theory]
    [InlineData("subscribe")]
    [InlineData("unsubscribe")]
    [InlineData("psubscribe")]
    [InlineData("punsubscribe")]
    [InlineData("ssubscribe")]
    [InlineData("sunsubscribe")]
    public async Task AConfirmationIsMatchedToItsCommand(string kind)
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        var verdict = Dispatch(muxer, $">3\r\n${kind.Length}\r\n{kind}\r\n$2\r\nch\r\n:1\r\n");

        Assert.Equal(RespOutOfBandResult.MatchToCommand, verdict);
    }

    /// <summary>Anything else is refused rather than guessed at, so a push of it is dropped.</summary>
    [Theory]
    [InlineData(">2\r\n$7\r\nnovelty\r\n$1\r\nx\r\n")] // not a kind we know
    [InlineData(">1\r\n$7\r\nmessage\r\n")] // too few elements to be a delivery
    [InlineData(">3\r\n$7\r\nmessage\r\n:1\r\n$5\r\nhello\r\n")] // a channel that is not a string
    [InlineData("+OK\r\n")] // not an aggregate at all
    public async Task AFrameThatCannotBeUnderstoodIsNotRecognised(string frame)
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        Assert.Equal(RespOutOfBandResult.NotRecognized, Dispatch(muxer, frame));
    }

    /// <summary>
    /// The RESP2 shape of a delivery is an ordinary array, not a push; the same dispatcher reads both,
    /// because on a RESP2 subscription connection that array IS the out-of-band frame.
    /// </summary>
    [Fact]
    public async Task ARESP2ArrayDeliveryIsUnderstoodToo()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);
        var pending = Listen(muxer, RedisChannel.Literal(Me()));

        var verdict = Dispatch(muxer, $"*3\r\n$7\r\nmessage\r\n${Me().Length}\r\n{Me()}\r\n$5\r\nresp2\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);
        Assert.Equal((Me(), "resp2"), await Delivered(pending));
    }
}
