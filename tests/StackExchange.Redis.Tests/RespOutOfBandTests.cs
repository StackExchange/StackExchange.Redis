using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Telling a delivery from a reply on the new core's connection.
/// </summary>
/// <remarks>
/// <para>
/// RESP3 marks a delivery with its own prefix, so the protocol answers the question. RESP2 does not: a
/// delivery is an ordinary array, and the only thing separating it from a reply is that the connection is
/// a subscriber. That makes recognition a decision rather than a fact, and a wrong one is expensive in
/// both directions - a reply consumed as a delivery strands whoever was waiting for it, and a delivery
/// matched as a reply answers somebody's command with a message they never asked for.
/// </para>
/// <para>
/// The <c>PING</c> case is the sharp edge: on a subscriber connection its reply is <i>also</i> an array.
/// </para>
/// </remarks>
public class RespOutOfBandTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            lock (_sync)
            {
                if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 1024);
                return _out.AsMemory(_length);
            }
        }

        public override void Advance(int count)
        {
            lock (_sync) _length += count;
        }

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        internal bool HasWritten
        {
            get { lock (_sync) return _length > 0; }
        }

        public override ValueTask DisposeAsync() => default;
    }

    private static (FakeTransport Transport, RespClientConnection Connection, List<string> Pushes) Connected(bool deliversArrays)
    {
        var transport = new FakeTransport();
        var pushes = new List<string>();
        var connection = new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)
        {
            DeliversArrays = deliversArrays,
        };

        connection.OnPush = frame =>
        {
            pushes.Add(Encoding.UTF8.GetString(frame.ToArray()).Replace("\r\n", "|"));
            return true;
        };

        return (transport, connection, pushes);
    }

    [Fact]
    public void APushFrameIsAlwaysOutOfBand()
    {
        var (transport, _, pushes) = Connected(deliversArrays: false);

        transport.Reply(">3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$2\r\nhi\r\n");

        Assert.Equal([">3|$7|message|$2|ch|$2|hi|"], pushes);
    }

    /// <summary>A RESP2 delivery is a bare array, recognised only because this connection subscribes.</summary>
    [Fact]
    public void AnArrayIsADeliveryOnlyWhenTheConnectionSubscribes()
    {
        var (subscriber, _, subscriberPushes) = Connected(deliversArrays: true);
        subscriber.Reply("*3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$2\r\nhi\r\n");
        Assert.Single(subscriberPushes);

        // the same bytes on an ordinary connection are a reply, and must stay one
        var (ordinary, _, ordinaryPushes) = Connected(deliversArrays: false);
        ordinary.Reply("*3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$2\r\nhi\r\n");
        Assert.Empty(ordinaryPushes);
    }

    /// <summary>
    /// <b>The one that strands a caller.</b> PING on a subscriber connection replies with an array, and
    /// consuming it as a delivery means whoever pinged waits for ever.
    /// </summary>
    [Theory]
    [InlineData("*2\r\n$4\r\npong\r\n$0\r\n\r\n")]
    [InlineData("*2\r\n$4\r\nPONG\r\n$0\r\n\r\n")]
    public void APongIsNeverADelivery(string frame)
    {
        var (transport, _, pushes) = Connected(deliversArrays: true);

        transport.Reply(frame);

        Assert.Empty(pushes);
    }

    /// <summary>An unclaimed array falls back to being a reply rather than being dropped.</summary>
    /// <remarks>
    /// The opposite of a push: this connection only GUESSED the array was a delivery, so if nothing
    /// recognises it the honest outcome is to let it be matched. Dropping it would lose a real reply on
    /// the strength of a guess.
    /// </remarks>
    [Fact]
    public void AnUnclaimedArrayIsNotConsumed()
    {
        var transport = new FakeTransport();
        var connection = new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)
        {
            DeliversArrays = true,
            OnPush = static _ => false, // recognises nothing
        };

        var pending = RespPayloadOperation.Rent();
        pending.Attach("*1\r\n$4\r\nPING\r\n"u8, CommandFlags.None, default);
        Assert.True(connection.Send(pending));

        transport.Reply("*1\r\n$2\r\nhi\r\n");

        // it reached the pending operation rather than vanishing
        Assert.True(new ValueTask<RespPayload>(pending, pending.Token).IsCompleted);
    }
}
