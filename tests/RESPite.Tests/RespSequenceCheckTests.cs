using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using RESPite.Messages;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace RESPite.Tests;

#pragma warning disable xUnit1031, xUnit1051

/// <summary>
/// The sequence check behind a request (<see cref="RespConnection.SequenceCheckHead"/>): the connection
/// primitive under the shipped client's high-integrity mode.
/// </summary>
public class RespSequenceCheckTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal ReadOnlySpan<byte> WrittenBytes => _out.AsSpan(0, _length);

        /// <summary>The bytes written, with CRLF shown as '|' and a token's four bytes as their decimal value in braces.</summary>
        internal string Written => Render(WrittenBytes);

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, Math.Max(_out.Length * 2, _length + sizeHint));
            return _out.AsMemory(_length);
        }

        public override void Advance(int count) => _length += count;

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Receive(string text) => Receive(Encoding.UTF8.GetBytes(text));

        internal void Receive(ReadOnlySpan<byte> payload) => _receiver!.OnReceived(payload);

        internal void Close(Exception? fault) => _receiver!.OnClosed(fault);

        public override ValueTask DisposeAsync() => default;

        internal void Reset() => _length = 0;
    }

    private const string EchoHead = "*2\r\n$4\r\nECHO\r\n$4\r\n";

    /// <summary>The check frame the connection should have written for <paramref name="token"/>, in <see cref="FakeTransport.Written"/>'s notation.</summary>
    private static string Check(uint token) => $"*2|$4|ECHO|$4|{{{token}}}|";

    /// <summary>The server's reply to a check carrying <paramref name="token"/>.</summary>
    private static byte[] CheckReply(uint token)
    {
        var reply = new byte[10];
        "$4\r\n"u8.CopyTo(reply);
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(4), token);
        "\r\n"u8.CopyTo(reply.AsSpan(8));
        return reply;
    }

    /// <summary>Render bytes for comparison: CRLF as '|', and the four bytes after a "$4|" as "{token}".</summary>
    private static string Render(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < bytes.Length)
        {
            if (bytes.Slice(i).StartsWith("$4\r\n"u8) && i + 10 <= bytes.Length && bytes.Slice(i + 8).StartsWith("\r\n"u8)
                && !IsText(bytes.Slice(i + 4, 4)))
            {
                sb.Append("$4|{").Append(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i + 4, 4))).Append("}|");
                i += 10;
            }
            else if (bytes.Slice(i).StartsWith("\r\n"u8))
            {
                sb.Append('|');
                i += 2;
            }
            else
            {
                sb.Append((char)bytes[i++]);
            }
        }

        return sb.ToString();

        static bool IsText(ReadOnlySpan<byte> span)
        {
            foreach (var b in span)
            {
                if (b < 0x20 || b > 0x7E) return false;
            }

            return true;
        }
    }

    private sealed class TextMessage : RespMessageBase<string?>
    {
        protected override string? Parse(ref RespReader reader)
            => reader.TryGetSpan(out var span) ? Encoding.UTF8.GetString(span.ToArray()) : null;

        internal static TextMessage For(string command, bool checked_ = true, string? prefix = null)
        {
            var message = new TextMessage();
            message.SetRequest(Encoding.UTF8.GetBytes(command), null, default);
            message.SequenceChecked = checked_;
            if (prefix is not null) message.Prefix = Encoding.UTF8.GetBytes(prefix);
            return message;
        }

        internal ValueTaskSourceStatus Status => ((IValueTaskSource<string?>)this).GetStatus(Token);
    }

    private static (RespConnection Connection, FakeTransport Transport) Connect(bool checks = true)
    {
        var transport = new FakeTransport();
        var connection = new RespConnection(transport);
        if (checks) connection.SequenceCheckHead = Encoding.ASCII.GetBytes(EchoHead);
        return (connection, transport);
    }

    private const string Ping = "*1\r\n$4\r\nPING\r\n";

    [Fact]
    public void ACheckFollowsEveryCheckedRequestUnderTheSameWrite()
    {
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);

        Assert.True(connection.Send(message));
        Assert.Equal("*1|$4|PING|" + Check(1), transport.Written);
        Assert.Equal(1, connection.PendingCount); // the check has no entry of its own: a bit on the request's
        Assert.Equal(1u, message.Diagnostics.HighIntegrityToken);
    }

    [Fact]
    public void TheReplyIsHeldUntilItsCheckValidates()
    {
        // the point of the mode: a caller never sees a value that the next frame then proves was not theirs
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+PONG\r\n");
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status);
        Assert.Equal(0, connection.PendingCount); // taken from the queue, held by the reader
        Assert.Equal(0, connection.SequenceChecksPassed);

        transport.Receive(CheckReply(1));
        Assert.Equal(ValueTaskSourceStatus.Succeeded, message.Status);
        Assert.Equal("PONG", message.GetResult(message.Token));
        Assert.Equal(0, connection.PendingCount);
        Assert.Equal(1, connection.SequenceChecksPassed);
        Assert.False(connection.IsClosed);
    }

    [Fact]
    public void TokensRunInWireOrderAcrossSends()
    {
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);
        Assert.Equal("*1|$1|A|" + Check(1) + "*1|$1|B|" + Check(2), transport.Written);

        transport.Receive("+a\r\n"u8);
        transport.Receive(CheckReply(1));
        transport.Receive("+b\r\n"u8);
        transport.Receive(CheckReply(2));
        Assert.Equal("a", first.GetResult(first.Token));
        Assert.Equal("b", second.GetResult(second.Token));
        Assert.Equal(2, connection.SequenceChecksPassed);
    }

    [Fact]
    public void AWrongTokenFaultsTheHeldReplyAndClosesTheConnection()
    {
        var (connection, transport) = Connect();
        var closed = false;
        connection.Closed = _ => closed = true;
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);

        transport.Receive("+a\r\n"u8);
        transport.Receive(CheckReply(7)); // not 1

        var ex = Assert.Throws<InvalidOperationException>(() => first.GetResult(first.Token));
        Assert.Contains("expected check 1", ex.Message);
        Assert.True(connection.IsClosed);
        Assert.True(closed);
        Assert.Same(ex, connection.Fault);

        // everything behind it fails with the same fault: the stream cannot be trusted from here
        Assert.Same(ex, Assert.Throws<InvalidOperationException>(() => second.GetResult(second.Token)));
        Assert.Equal(0, connection.PendingCount);
        Assert.Equal(0, connection.SequenceChecksPassed);
    }

    [Fact]
    public void AWrongTokenArrivingWithItsReplyFailsTheSameWay()
    {
        // the look-ahead path: reply and check in one receive, so the check is settled without a hold - and a
        // wrong token there must fail exactly as one arriving later does
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        var bytes = new byte[7 + 10];
        "+PONG\r\n"u8.CopyTo(bytes);
        CheckReply(7).CopyTo(bytes, 7);
        transport.Receive(bytes);

        var ex = Assert.Throws<InvalidOperationException>(() => message.GetResult(message.Token));
        Assert.Contains("expected check 1", ex.Message);
        Assert.True(connection.IsClosed);
        Assert.Equal(0, connection.PendingCount);
    }

    [Fact]
    public void ABulkStringThatIsNotTheCheckFailsEvenBeforeItIsComplete()
    {
        // in-band and a bulk string, so it can only be the check - and it is the wrong shape; the look-ahead
        // does not need the rest of it to know
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+PONG\r\n$12\r\nsomebody els");
        Assert.True(connection.IsClosed);
        var ex = Assert.Throws<InvalidOperationException>(() => message.GetResult(message.Token));
        Assert.Contains("BulkString", ex.Message);
    }

    [Fact]
    public void AMissingReplyIsCaughtByTheVeryNextCheck()
    {
        // the failure mode the mode exists for: a frame vanished from the stream, so B's reply arrives where
        // A's check should be; without the check, A would have completed with B's value and B would hang
        var (connection, transport) = Connect();
        var first = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var second = TextMessage.For("*1\r\n$1\r\nB\r\n");
        connection.Send(first);
        connection.Send(second);

        transport.Receive("+a\r\n+b\r\n");
        Assert.True(connection.IsClosed);
        var ex = Assert.Throws<InvalidOperationException>(() => first.GetResult(first.Token));
        Assert.Contains("SimpleString frame of 4 bytes", ex.Message);
        Assert.Throws<InvalidOperationException>(() => second.GetResult(second.Token));
    }

    [Fact]
    public void AnUncheckedRequestInARunGetsNoCheckAndStillGetsItsReply()
    {
        // what a transaction needs: MULTI and the queued commands are not checked, EXEC is
        var (connection, transport) = Connect();
        var a = TextMessage.For("*1\r\n$1\r\nA\r\n");
        var b = TextMessage.For("*1\r\n$1\r\nB\r\n", checked_: false);
        var c = TextMessage.For("*1\r\n$1\r\nC\r\n");

        Assert.True(connection.Send([RespRunEntry.Of(a), RespRunEntry.Of(b), RespRunEntry.Of(c)], 3));
        Assert.Equal("*1|$1|A|" + Check(1) + "*1|$1|B|*1|$1|C|" + Check(2), transport.Written);
        Assert.Equal(0u, b.Diagnostics.HighIntegrityToken);

        transport.Receive("+a\r\n"u8);
        transport.Receive(CheckReply(1));
        transport.Receive("+b\r\n+c\r\n"u8);
        Assert.Equal("b", b.GetResult(b.Token)); // not held: nothing follows it to wait for
        Assert.Equal(ValueTaskSourceStatus.Pending, c.Status);
        transport.Receive(CheckReply(2));
        Assert.Equal("a", a.GetResult(a.Token));
        Assert.Equal("c", c.GetResult(c.Token));
        Assert.Equal(0, connection.PendingCount);
    }

    [Fact]
    public void APushBetweenAReplyAndItsCheckIsSkipped()
    {
        // RESP3 can interleave a push anywhere; the check is the next IN-BAND frame
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+PONG\r\n>2\r\n$7\r\nmessage\r\n$2\r\nhi\r\n");
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status);
        transport.Receive(CheckReply(1));
        Assert.Equal("PONG", message.GetResult(message.Token));
        Assert.False(connection.IsClosed);
    }

    [Fact]
    public void ACheckArrivingInALaterReceiveCompletesTheHeldReply()
    {
        // the reply and its check can straddle a socket read; the hold survives the boundary - and a buffer
        // roll, since a held reply keeps a reservation on the buffer it arrived in
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+PONG\r\n"u8);
        transport.Receive(new byte[0]); // a receive with nothing in it changes nothing
        var reply = CheckReply(1);
        transport.Receive(reply.AsSpan(0, 3)); // and a check split across reads is fine too
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status);
        transport.Receive(reply.AsSpan(3));
        Assert.Equal("PONG", message.GetResult(message.Token));
    }

    [Fact]
    public void ALargeHeldReplySurvivesTheBufferRollingUnderneathIt()
    {
        // enough traffic after the held reply to force the inbound buffer to roll to a new one: the hold is
        // a reservation, so the old buffer stays put until the check lands and the reply is delivered
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);
        var filler = TextMessage.For("*1\r\n$1\r\nF\r\n", checked_: false);
        connection.Send(filler);

        var big = new string('x', 70_000);
        transport.Receive($"${big.Length}\r\n{big}\r\n");
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status);
        transport.Receive(CheckReply(1));
        Assert.Equal(big, message.GetResult(message.Token));
        transport.Receive("+f\r\n");
        Assert.Equal("f", filler.GetResult(filler.Token));
    }

    private sealed class QueuingConnection(DuplexTransport transport) : RespConnection(transport)
    {
        protected override bool TryHandOff(ReadOnlySpan<byte> frame, IRespMessage message) => frame.StartsWith("+QUEUED"u8);
    }

    [Fact]
    public void AHandedOffReplyStillHasItsCheckValidated()
    {
        // a frame the subclass takes (a redirect, a +QUEUED receipt) is not held - there is nothing to deliver -
        // but the check behind it is still consumed and validated, so the counter stays in step
        var transport = new FakeTransport();
        var connection = new QueuingConnection(transport) { SequenceCheckHead = Encoding.ASCII.GetBytes(EchoHead) };
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+QUEUED\r\n"u8);
        transport.Receive(CheckReply(1));
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status); // still owed its real reply, by the subclass's rules
        Assert.Equal(1, connection.SequenceChecksPassed);
        Assert.Equal(0, connection.PendingCount);
        Assert.False(connection.IsClosed);
    }

    [Fact]
    public void APrefixAndACheckBracketTheRequest()
    {
        // the two frame mechanisms compose: CLIENT CACHING YES in front, the check behind, three replies owed
        var (connection, transport) = Connect();
        const string CachingYes = "*3\r\n$6\r\nCLIENT\r\n$7\r\nCACHING\r\n$3\r\nYES\r\n";
        var message = TextMessage.For(Ping, prefix: CachingYes);
        connection.Send(message);

        Assert.Equal("*3|$6|CLIENT|$7|CACHING|$3|YES|*1|$4|PING|" + Check(1), transport.Written);
        Assert.Equal(2, connection.PendingCount); // the prefix's reply, and the request's
        transport.Receive("+OK\r\n+PONG\r\n"u8);
        transport.Receive(CheckReply(1));
        Assert.Equal("PONG", message.GetResult(message.Token));
        Assert.Equal(0, connection.PendingCount);
    }

    [Fact]
    public void ClosingWhileAReplyIsHeldFaultsIt()
    {
        // answered but unverified is still pending: a close must not leave the caller waiting on a reply the
        // connection has and will never release
        var (connection, transport) = Connect();
        var message = TextMessage.For(Ping);
        connection.Send(message);

        transport.Receive("+PONG\r\n"u8);
        Assert.Equal(ValueTaskSourceStatus.Pending, message.Status);
        transport.Close(null);
        Assert.Equal(ValueTaskSourceStatus.Faulted, message.Status);
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public void WithoutAHeadNothingChanges()
    {
        var (connection, transport) = Connect(checks: false);
        var message = TextMessage.For(Ping);
        connection.Send(message);
        Assert.Equal("*1|$4|PING|", transport.Written);
        Assert.Equal(1, connection.PendingCount);
        transport.Receive("+PONG\r\n");
        Assert.Equal("PONG", message.GetResult(message.Token));
        Assert.Equal(0u, message.Diagnostics.HighIntegrityToken);
    }

    [Fact]
    public void ARecycledMessageIsCheckedAgainByDefault()
    {
        // the exemption is per life: an instance that carried an unchecked queued command must not carry the
        // exemption into its next life as an ordinary command
        var message = TextMessage.For(Ping, checked_: false);
        Assert.False(message.SequenceChecked);
        var (connection, transport) = Connect();
        connection.Send(message);
        transport.Receive("+PONG\r\n");
        Assert.Equal("PONG", message.GetResult(message.Token)); // ends the life; the instance resets
        Assert.True(message.SequenceChecked);
    }
}
