using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>A pooled operation starts each life clean - its database included.</summary>
/// <remarks>
/// Only the endpoint executor's dispatch assigned an operation's database, so an operation rented by a
/// batch, a transaction or a connection executor kept the previous life's. A batched command re-sent after a
/// -MOVED is written by the endpoint executor, which reads that database to decide its SELECT - so it ran
/// against another caller's database (seen in the suite as "cannot switch to database: 33" on a cluster).
/// </remarks>
public class RespPayloadOperationPoolingTests
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

        public override ValueTask DisposeAsync() => default;
    }

    [Fact]
    public async Task ARecycledOperationDoesNotKeepItsDatabase()
    {
        var transport = new FakeTransport();
        var connection = new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false);

        // one full life with a database set, consumed so that it recycles
        var first = RespPayloadOperation.Rent();
        first.Attach("*1\r\n$4\r\nPING\r\n"u8, CommandFlags.None, default);
        first.Database = 33;
        Assert.True(connection.Send(first));
        transport.Reply("+PONG\r\n");
        (await new ValueTask<RespPayload>(first, first.Token))?.Release();

        // every instance the pool hands out from here on starts with no database - including, if the pool
        // returns it, the very instance that just held 33
        for (var i = 0; i < 16; i++)
        {
            var next = RespPayloadOperation.Rent();
            Assert.Equal(-1, next.Database);
        }
    }

    /// <summary>
    /// An operation cancelled after it was written keeps its slot in the connection's pending queue to absorb its
    /// late reply, and may meanwhile be recycled and rented again. Nothing done through that stale slot - the late
    /// reply, a heartbeat timeout - may reach the instance's NEXT life.
    /// </summary>
    /// <remarks>
    /// The queue held bare references and acted through each instance's current token, so the late reply to the
    /// cancelled PING completed the GET that reused the instance, and a heartbeat sweep could time out a pooled
    /// instance, so that its next renter failed at once with "0ms elapsed".
    /// </remarks>
    [Fact]
    public async Task AStaleQueueSlotCannotReachTheNextLifeOfItsOperation()
    {
        var transport = new FakeTransport();
        var connection = new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false);

        using var cancel = new CancellationTokenSource();
        var first = RespPayloadOperation.Rent();
        first.Attach("*1\r\n$4\r\nPING\r\n"u8, CommandFlags.None, cancel.Token);
        Assert.True(connection.Send(first));                     // written and queued
        cancel.Cancel();                                         // cancelled after the write
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new ValueTask<RespPayload>(first, first.Token));

        RespPayloadOperation? second = null;
        for (var i = 0; i < 64 && second is null; i++)
        {
            var next = RespPayloadOperation.Rent();
            if (ReferenceEquals(next, first)) second = next;
        }

        Assert.NotNull(second); // a definite outcome is recyclable; that is the case under test
        second.Attach("*2\r\n$3\r\nGET\r\n$1\r\nk\r\n"u8, CommandFlags.None, default);
        await Task.Delay(20);

        // a sweep through the stale slot must not time out the new life...
        connection.ExpirePending(TimeSpan.FromMilliseconds(1));
        Assert.False(((IRespMessage)second).IsFinished);

        Assert.True(connection.Send(second));
        transport.Reply("+PONG\r\n");    // the cancelled PING's late reply
        transport.Reply("$1\r\nx\r\n"); // the GET's own

        // ...and the late reply must not complete it
        using var payload = await new ValueTask<RespPayload>(second, second.Token);
        Assert.Equal("$1\r\nx\r\n", Encoding.UTF8.GetString(payload.Span.ToArray()));
    }
}
