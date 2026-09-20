using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Messages;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace RESPite.Tests;

// see the note in RespOperationTests; the analyzers misfire on this type's API shape
#pragma warning disable xUnit1031, xUnit1051

/// <summary>
/// One connection: a FIFO of operations over a transport. Design notes section 7, phase 3.
/// </summary>
public class RespConnectionTests
{
    /// <summary>A transport that is two byte arrays and nothing else.</summary>
    private sealed class FakeTransport : DuplexTransport
    {
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal int Flushes;

        internal string Written => Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|");

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, Math.Max(_out.Length * 2, _length + sizeHint));
            return _out.AsMemory(_length);
        }

        public override void Advance(int count) => _length += count;

        public override bool Flush()
        {
            Flushes++;
            return true;
        }

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        /// <summary>Deliver inbound bytes, as the wire would.</summary>
        internal void Receive(string text) => Receive(Encoding.UTF8.GetBytes(text));

        internal void Receive(ReadOnlySpan<byte> payload) => _receiver!.OnReceived(payload);

        internal void Close(Exception? fault) => _receiver!.OnClosed(fault);

        public override ValueTask DisposeAsync() => default;
    }

    private sealed class TextMessage : RespMessageBase<string?>
    {
        protected override string? Parse(ref RespReader reader)
            => reader.TryGetSpan(out var span) ? Encoding.UTF8.GetString(span.ToArray()) : null;

        internal static TextMessage For(string command)
        {
            var message = new TextMessage();
            message.SetRequest(Encoding.UTF8.GetBytes(command), null, default);
            return message;
        }
    }

    private static (RespConnection Connection, FakeTransport Transport) Connect()
    {
        var transport = new FakeTransport();
        return (new RespConnection(transport), transport);
    }

    /// <summary>A transport that answers during <c>Flush</c>, as a loopback routinely does.</summary>
    private sealed class EchoingTransport(string reply) : DuplexTransport
    {
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 1024);
            return _out.AsMemory(_length);
        }

        public override void Advance(int count) => _length += count;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        public override bool Flush()
        {
            // THE race: the reply arrives before Send has returned
            _receiver!.OnReceived(Encoding.UTF8.GetBytes(reply));
            return true;
        }

        public override ValueTask DisposeAsync() => default;
    }

    [Fact]
    public void AReplyArrivingDuringFlushStillFindsItsOperation()
    {
        // enqueue precedes write, and both are inside the lock. Queue the operation AFTER writing it and
        // this reply arrives with nothing pending - which, on a loopback, is every single call.
        var connection = new RespConnection(new EchoingTransport("+PONG\r\n"));
        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");

        Assert.True(connection.Send(message));
        Assert.False(connection.IsClosed);
        Assert.Equal(0, connection.PendingCount);
        Assert.Equal("PONG", message.GetResult(message.Token));
    }

    [Fact]
    public void ARequestIsWrittenAndItsReplyCompletesIt()
    {
        var (connection, transport) = Connect();
        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");

        Assert.True(connection.Send(message));
        Assert.Equal("*1|$4|PING|", transport.Written);
        Assert.Equal(1, transport.Flushes);
        Assert.Equal(1, connection.PendingCount);

        transport.Receive("+PONG\r\n");

        Assert.Equal(0, connection.PendingCount);
        Assert.Equal("PONG", message.GetResult(message.Token));
    }

    [Fact]
    public void RepliesMatchRequestsInOrder()
    {
        // the whole of RESP's correlation model: the nth reply answers the nth request, which is why
        // the queue is a queue and not a dictionary
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        var third = TextMessage.For("*1\r\n$1\r\nC\r\n");

        foreach (var message in new[] { first, second, third }) Assert.True(connection.Send(message));

        var (t1, t2, t3) = (first.Token, second.Token, third.Token);
        transport.Receive("+one\r\n+two\r\n+three\r\n");

        Assert.Equal("one", first.GetResult(t1));
        Assert.Equal("two", second.GetResult(t2));
        Assert.Equal("three", third.GetResult(t3));
    }

    [Fact]
    public void AFrameSplitAcrossReadsIsReassembled()
    {
        // the scan state carries across calls, so a frame arriving in pieces resumes rather than
        // restarting; getting this wrong looks like a hang rather than an error
        var (connection, transport) = Connect();
        var message = TextMessage.For("*1\r\n$3\r\nGET\r\n");
        connection.Send(message);

        transport.Receive("$11\r\nhel");
        Assert.Equal(1, connection.PendingCount); // nothing complete yet

        transport.Receive("lo ");
        Assert.Equal(1, connection.PendingCount);

        transport.Receive("world\r\n");
        Assert.Equal(0, connection.PendingCount);
        Assert.Equal("hello world", message.GetResult(message.Token));
    }

    private sealed class CountingMessage : RespMessageBase<int>
    {
        protected override int Parse(ref RespReader reader) => reader.AggregateLength();

        internal static CountingMessage Create()
        {
            var message = new CountingMessage();
            message.SetRequest(Encoding.UTF8.GetBytes("*1\r\n$1\r\nA\r\n"), null, default);
            return message;
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(11)]
    public void AnAggregateSplitAtEveryBoundaryIsReassembled(int chunk)
    {
        // the split-frame test above uses a bulk string, which is forgiving: one length prefix and a
        // payload. An AGGREGATE makes the scanner track a running element count, which is what actually
        // breaks if the scan state is carried across reads AND the bytes are re-fed to it.
        var (connection, transport) = Connect();
        var message = CountingMessage.Create();
        connection.Send(message);

        var reply = Encoding.UTF8.GetBytes("*3\r\n:1\r\n:2\r\n:3\r\n");
        for (var offset = 0; offset < reply.Length; offset += chunk)
        {
            transport.Receive(reply.AsSpan(offset, Math.Min(chunk, reply.Length - offset)));
        }

        Assert.False(connection.IsClosed);
        Assert.Equal(0, connection.PendingCount);
        Assert.Equal(3, message.GetResult(message.Token));
    }

    [Fact]
    public void TwoRepliesInOneReadAreBothDelivered()
    {
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);
        var (t1, t2) = (first.Token, second.Token);

        transport.Receive("+one\r\n+two\r\n");

        Assert.Equal("one", first.GetResult(t1));
        Assert.Equal("two", second.GetResult(t2));
    }

    [Fact]
    public void APushBetweenRepliesIsNotMistakenForOne()
    {
        // the desynchronising bug, and the reason pushes are dropped by default: an out-of-band frame
        // handed to the queue answers somebody's request with it, and every reply after is wrong
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);
        var (t1, t2) = (first.Token, second.Token);

        transport.Receive("+one\r\n>2\r\n$10\r\ninvalidate\r\n$1\r\nk\r\n+two\r\n");

        Assert.Equal("one", first.GetResult(t1));
        Assert.Equal("two", second.GetResult(t2)); // NOT the push
    }

    private sealed class PushWatchingConnection(DuplexTransport transport) : RespConnection(transport)
    {
        internal readonly List<string> Pushes = [];

        protected override bool OnOutOfBand(ReadOnlySpan<byte> frame)
        {
            Pushes.Add(Encoding.UTF8.GetString(frame.ToArray()).Replace("\r\n", "|"));
            return true;
        }
    }

    [Fact]
    public void AnOutOfBandFrameReachesTheHook()
    {
        var transport = new FakeTransport();
        var connection = new PushWatchingConnection(transport);
        var message = TextMessage.For("*1\r\n$1\r\nA\r\n");
        connection.Send(message);

        transport.Receive(">2\r\n$10\r\ninvalidate\r\n$1\r\nk\r\n+one\r\n");

        Assert.Equal(">2|$10|invalidate|$1|k|", Assert.Single(connection.Pushes));
        Assert.Equal("one", message.GetResult(message.Token));
    }

    /// <summary>A message whose result RETAINS the frame, as the payload operation does.</summary>
    private sealed class RetainingMessage : RespMessageBase<RetainingMessage.Held>
    {
        internal sealed class Held(PayloadReservation reservation)
        {
            internal string Text
            {
                get
                {
                    var buffer = (RefCountedBuffer)reservation.Owner;
                    return Encoding.UTF8.GetString(
                        buffer.GetSpan().Slice(reservation.Offset, reservation.Length).ToArray()).Replace("\r\n", "|");
                }
            }

            internal void Release() => reservation.Owner.Dispose();
        }

        protected override Held ParseFrame(scoped ReadOnlySpan<byte> frame, IPayloadReservationProvider? source)
        {
            Assert.NotNull(source);
            Assert.True(source!.TryReserve(frame, out var reservation), "the frame should be a window onto the sender's buffer");
            return new Held(reservation);
        }

        internal static RetainingMessage For(string command)
        {
            var message = new RetainingMessage();
            message.SetRequest(Encoding.UTF8.GetBytes(command), null, default);
            return message;
        }
    }

    [Fact]
    public void ARetainedReplySurvivesTheBufferRollingUnderneathIt()
    {
        // the point of sharing rather than copying: the frame stays valid because the RESERVATION keeps
        // the buffer alive, even once the connection has moved on to a new one. Holding every reply
        // forces exactly that - the buffer can never be compacted in place, so it must roll.
        var (connection, transport) = Connect();
        const int Count = 500;

        var messages = new RetainingMessage[Count];
        var tokens = new short[Count];
        for (var i = 0; i < Count; i++)
        {
            messages[i] = RetainingMessage.For("*1\r\n$1\r\nP\r\n");
            Assert.True(connection.Send(messages[i]));
            tokens[i] = messages[i].Token;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < Count; i++) builder.Append($"+reply-{i}\r\n");
        var all = Encoding.UTF8.GetBytes(builder.ToString());
        for (var offset = 0; offset < all.Length; offset += 37)
        {
            transport.Receive(all.AsSpan(offset, Math.Min(37, all.Length - offset)));
        }

        Assert.False(connection.IsClosed);

        // read them ALL only now, long after the buffers they point into stopped being current
        var held = new RetainingMessage.Held[Count];
        for (var i = 0; i < Count; i++) held[i] = messages[i].GetResult(tokens[i]);
        for (var i = 0; i < Count; i++)
        {
            Assert.Equal($"+reply-{i}|", held[i].Text);
        }

        foreach (var item in held) item.Release();
    }

    [Fact]
    public void ManyRepliesAcrossBufferRollsStayMatchedToTheirOwnRequests()
    {
        // enough traffic to make the receive buffer fill and roll several times, which is where a shared
        // (rather than copied) reply can go wrong: the buffer must not move or be reused under a frame
        // somebody is still holding
        var (connection, transport) = Connect();
        const int Count = 2000;

        var messages = new TextMessage[Count];
        for (var i = 0; i < Count; i++)
        {
            messages[i] = TextMessage.For($"*1\r\n$1\r\nP\r\n");
            Assert.True(connection.Send(messages[i]));
        }

        var tokens = new short[Count];
        for (var i = 0; i < Count; i++) tokens[i] = messages[i].Token;

        // deliver in awkward chunks so frames straddle reads
        var builder = new StringBuilder();
        for (var i = 0; i < Count; i++) builder.Append($"+reply-{i}\r\n");
        var all = Encoding.UTF8.GetBytes(builder.ToString());
        for (var offset = 0; offset < all.Length; offset += 37)
        {
            transport.Receive(all.AsSpan(offset, Math.Min(37, all.Length - offset)));
        }

        Assert.False(connection.IsClosed);
        for (var i = 0; i < Count; i++)
        {
            Assert.Equal($"reply-{i}", messages[i].GetResult(tokens[i]));
        }
    }

    [Fact]
    public void ClosingFaultsEveryOutstandingOperationIndefinitely()
    {
        // THE case the definite/indefinite split exists for: a write may have reached the server and had
        // its reply lost with the connection, so these are not provably unapplied and must not be pooled
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);

        var boom = new InvalidOperationException("connection reset");
        transport.Close(boom);

        Assert.True(connection.IsClosed);
        Assert.False(first.IsRecyclable);
        Assert.False(second.IsRecyclable);
        Assert.Same(boom, Assert.Throws<InvalidOperationException>(() => first.GetResult(first.Token)));
        Assert.Same(boom, Assert.Throws<InvalidOperationException>(() => second.GetResult(second.Token)));
    }

    [Fact]
    public void SendingOnAClosedConnectionFailsRatherThanQueues()
    {
        var (connection, transport) = Connect();
        transport.Close(null);

        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");
        Assert.False(connection.Send(message));
        Assert.Equal(0, connection.PendingCount);
        Assert.Throws<InvalidOperationException>(() => message.GetResult(message.Token));
    }

    [Fact]
    public void AnUnexpectedReplyClosesTheConnection()
    {
        // there is no correct recovery from a reply nobody asked for: the stream position is no longer
        // known, so every subsequent reply would be mis-addressed. Failing loudly beats guessing.
        var (connection, transport) = Connect();

        transport.Receive("+surprise\r\n");

        Assert.True(connection.IsClosed);
    }

    [Fact]
    public void ByteCountersTrackBothDirections()
    {
        var (connection, transport) = Connect();
        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");
        connection.Send(message);

        Assert.Equal(14, connection.BytesSent);

        transport.Receive("+PONG\r\n");
        Assert.Equal(7, connection.BytesReceived);
        message.GetResult(message.Token);
    }

    [Fact]
    public void WritingMarksTheOperationSent()
    {
        // the status ladder is what lets retry know a command never reached a socket; the connection is
        // what moves it
        var (connection, _) = Connect();
        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");

        Assert.Equal(RespCommandStatus.WaitingToBeSent, message.Diagnostics.Status);
        Assert.True(message.Diagnostics.IsKnownNotApplied);

        connection.Send(message);

        Assert.Equal(RespCommandStatus.Sent, message.Diagnostics.Status);
        Assert.False(message.Diagnostics.IsKnownNotApplied);
    }

    [Fact]
    public void SendingRecordsTheConnectionAndItsCountersAtThatMoment()
    {
        // the half of "realistic" that is easy to leave out: without this a timeout report knows the
        // command was sent but not where, when, or whether the connection moved at all afterwards
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$4\r\nPING\r\n");   // 14 bytes
        connection.Send(first);
        transport.Receive("+PONG\r\n");                        // 7 bytes
        first.GetResult(first.Token);

        var second = TextMessage.For("*1\r\n$4\r\nPING\r\n");
        connection.Send(second);

        Assert.Same(connection, second.Diagnostics.EnqueuedTo);
        Assert.Equal(14, second.Diagnostics.QueuedStampSent);      // what had gone out BEFORE this one
        Assert.Equal(7, second.Diagnostics.QueuedStampReceived);
        Assert.NotEqual(0, second.Diagnostics.WriteTickCount);
    }

    [Fact]
    public void TheEnqueueStampExcludesTheRequestsOwnBytes()
    {
        // otherwise every command looks like progress on a connection that has actually stalled
        var (connection, _) = Connect();
        var message = TextMessage.For("*1\r\n$4\r\nPING\r\n");

        connection.Send(message);

        Assert.Equal(0, message.Diagnostics.QueuedStampSent);
        Assert.Equal(14, connection.BytesSent); // the connection counted them; the stamp predates them
    }

    [Fact]
    public async Task ConcurrentSendersDoNotInterleaveTheirBytes()
    {
        // queue order must equal wire order; enqueueing outside the write lock lets two senders each be
        // matched to the other's reply, silently
        var (connection, transport) = Connect();
        var messages = new TextMessage[64];
        for (var i = 0; i < messages.Length; i++) messages[i] = TextMessage.For($"*1\r\n$2\r\n{i:D2}\r\n");

        await Task.WhenAll(Array.ConvertAll(messages, m => Task.Run(() => connection.Send(m))));

        // every frame is intact and none is spliced into another
        var written = transport.Written;
        for (var i = 0; i < messages.Length; i++) Assert.Contains($"*1|$2|{i:D2}|", written);
        Assert.Equal(messages.Length, connection.PendingCount);
    }

}
