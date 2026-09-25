using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    /// One delivery can carry several messages, and each is delivered separately.
    /// </summary>
    /// <remarks>
    /// An array payload is not one value that happens to be a list - it is several messages batched into a
    /// frame (StackExchange.Redis#2507). Reading it as a single value hands the handler something it
    /// cannot use, and loses every message after the first.
    /// </remarks>
    [Fact]
    public async Task AnArrayPayloadIsSeveralMessages()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        var seen = new List<string>();
        var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        muxer.GetSubscriber().Subscribe(RedisChannel.Literal(Me()), (_, payload) =>
        {
            lock (seen)
            {
                seen.Add((string?)payload ?? "");
                if (seen.Count == 2) both.TrySetResult(true);
            }
        });

        var verdict = Dispatch(
            muxer,
            $">3\r\n$7\r\nmessage\r\n${Me().Length}\r\n{Me()}\r\n*2\r\n$5\r\nfirst\r\n$6\r\nsecond\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);

        var done = await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(done, both.Task), $"expected two messages, saw {seen.Count}");

        // as a set, not a sequence: each delivery is completed independently, and a handler may be run on
        // a worker - so the two can land in either order, and asserting one of them is asserting a
        // scheduling detail. What this owns is that BOTH arrived, separately, which is the subject
        lock (seen)
        {
            Assert.Equal(["first", "second"], seen.OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// An invalidation is consumed, never matched: it is not the reply to anything we sent, so falling
    /// through to command matching would hand it to whoever happened to be at the front of the queue.
    /// </summary>
    [Fact]
    public async Task AnInvalidationIsConsumed()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        // what it does to a cache is RespCacheInvalidationFrameTests; what matters here is that it is
        // recognised at all - this connection has no cache, and the frame must still not be matched
        Assert.Equal(RespOutOfBandResult.Handled, Dispatch(muxer, ">2\r\n$10\r\ninvalidate\r\n*1\r\n$1\r\nk\r\n"));
        Assert.Equal(RespOutOfBandResult.Handled, Dispatch(muxer, ">2\r\n$10\r\ninvalidate\r\n_\r\n"));
    }

    /// <summary>
    /// A configured channel prefix is stripped on the way in, as it was added on the way out.
    /// </summary>
    /// <remarks>
    /// <b>This fails silently when it is wrong</b>, which is what makes it worth a test of its own: the
    /// registry is keyed by the names the caller used, so a delivery read without stripping simply
    /// matches nothing, and a deployment with a prefix quietly stops receiving. No error, no log, no
    /// exception - just no messages.
    /// </remarks>
    [Fact]
    public async Task AChannelPrefixIsStrippedFromDeliveries()
    {
        const string Prefix = "pfx:";
        await using var conn = Create(shared: false, channelPrefix: Prefix);
        var muxer = TestMultiplexer.Unwrap(conn);

        // the caller subscribes to the unprefixed name; the wire carries the prefixed one
        var pending = Listen(muxer, RedisChannel.Literal(Me()));

        var onTheWire = Prefix + Me();
        var verdict = Dispatch(
            muxer,
            $">3\r\n$7\r\nmessage\r\n${onTheWire.Length}\r\n{onTheWire}\r\n$8\r\nprefixed\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);

        // and the caller is told the name IT used, not the one the wire used
        Assert.Equal((Me(), "prefixed"), await Delivered(pending));
    }

    /// <summary>
    /// A peer's "the configuration changed" broadcast is acted on, not merely delivered.
    /// </summary>
    /// <remarks>
    /// This channel is not kept in the pub/sub registry, so nothing is listening for it: a dispatcher that
    /// only invoked handlers would drop the one message whose whole purpose is to make us re-read the
    /// topology - and the client would keep talking to the old primary until something else told it.
    /// </remarks>
    [Fact]
    public async Task AConfigurationBroadcastAsksForAReconfigure()
    {
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        var blamed = new TaskCompletionSource<EndPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        muxer.ConfigurationChangedBroadcast += (_, args) => blamed.TrySetResult(args.EndPoint);

        var channel = Encoding.UTF8.GetString(muxer.ConfigurationChangedChannel!);
        var source = "127.0.0.1:6380";
        var verdict = Dispatch(
            muxer,
            $">3\r\n$7\r\nmessage\r\n${channel.Length}\r\n{channel}\r\n${source.Length}\r\n{source}\r\n");

        Assert.Equal(RespOutOfBandResult.Handled, verdict);

        var done = await Task.WhenAny(blamed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(done, blamed.Task), "the broadcast was delivered but not acted on");

        // and it names who to blame, which is what steers the reconfigure that follows
        Assert.Equal(source, Format.ToString(await blamed.Task));
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
