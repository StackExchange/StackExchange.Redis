using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The context surface over a real connection, with no <c>Message</c> anywhere in the path. Design notes
/// section 7, phase 3b.
/// </summary>
/// <remarks>
/// Everything else on this surface still reaches a server through <c>RespMessageExecutor</c>, which wraps
/// the rendered frame in a <c>Message</c> for the existing pipeline. These tests drive the whole stack -
/// interpolated writer, context, executor, connection, transport - with none of it.
/// </remarks>
public class RespConnectionExecutorTests
{
    /// <summary>A transport that is two byte arrays, and answers on demand.</summary>
    private sealed class LoopbackTransport : DuplexTransport
    {
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal string Written => Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|");

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 1024);
            return _out.AsMemory(_length);
        }

        public override void Advance(int count) => _length += count;

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        internal void Close(Exception? fault) => _receiver!.OnClosed(fault);

        public override ValueTask DisposeAsync() => default;
    }

    private static (RespDatabaseContext Context, LoopbackTransport Transport, RespConnection Connection) Connect()
    {
        var transport = new LoopbackTransport();
        var connection = new RespConnection(transport);
        var context = new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0));
        return (new RespDatabaseContext(context), transport, connection);
    }

    [Fact]
    public async Task AStringGetGoesAllTheWayToTheWireAndBack()
    {
        var (context, transport, _) = Connect();

        var pending = context.Strings.GetAsync("user:1");
        Assert.Equal("*2|$3|GET|$6|user:1|", transport.Written);   // the interpolated writer's bytes

        transport.Reply("$4\r\nmarc\r\n");
        Assert.Equal("marc", (string?)await pending);
    }

    [Fact]
    public async Task RepliesAreMatchedToTheirOwnRequests()
    {
        // pipelining, which is the reason any of this is worth doing: three requests on the wire before
        // the first reply comes back, and each caller gets its own answer
        var (context, transport, _) = Connect();

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        var third = context.Strings.GetAsync("c");

        transport.Reply("$1\r\nA\r\n$1\r\nB\r\n$1\r\nC\r\n");

        Assert.Equal("A", (string?)await first);
        Assert.Equal("B", (string?)await second);
        Assert.Equal("C", (string?)await third);
    }

    [Fact]
    public async Task AServerErrorSurfacesAsAnException()
    {
        var (context, transport, _) = Connect();

        var pending = context.Strings.GetAsync("user:1");
        transport.Reply("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n");

        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await pending);
        Assert.StartsWith("WRONGTYPE", ex.Message);
    }

    [Fact]
    public async Task ALostConnectionFaultsWhatWasInFlight()
    {
        var (context, transport, connection) = Connect();

        var pending = context.Strings.GetAsync("user:1");
        Assert.Equal(1, connection.PendingCount);

        transport.Close(new InvalidOperationException("connection reset"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Equal("connection reset", ex.Message);
    }

    [Fact]
    public async Task SendingOnAClosedConnectionNamesHowFarItGot()
    {
        // the payoff for mirroring CommandStatus exactly: the exception can say the command was never
        // written, which is what lets FaultContext.NotApplied bypass retry's side-effect cap
        var (context, transport, _) = Connect();
        transport.Close(null);

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await context.Strings.GetAsync("user:1"));
        Assert.Equal(CommandStatus.WaitingToBeSent, ex.CommandStatus);
        Assert.Equal(ConnectionFailureType.SocketClosed, ex.FailureType);
    }

    [Fact]
    public void TheSynchronousPathUsesTheSameObject()
    {
        // no result box and no second mechanism: Wait blocks on the same value-task core the async path
        // awaits. Driven through the executor directly because the context surface is async-only - the
        // sync bridge above it is TransitionalDatabase's business, not this layer's.
        var (context, transport, connection) = Connect();
        var executor = new RespConnectionExecutor(connection, 0);

        var reply = Task.Run(async () =>
        {
            while (connection.PendingCount == 0) await Task.Yield();
            transport.Reply("$4\r\nmarc\r\n");
        });

        using var frame = context.Raw.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}");
        using var payload = executor.Send(frame.Detach());

        Assert.Equal("$4\r\nmarc\r\n", Encoding.UTF8.GetString(payload.Span.ToArray()));
        reply.Wait();
    }

    [Fact]
    public async Task APushBetweenRepliesDoesNotAnswerACommand()
    {
        // RESP3 invalidation arriving mid-stream; matching it to the queue would answer this GET with it
        var (context, transport, _) = Connect();

        var pending = context.Strings.GetAsync("user:1");
        transport.Reply(">2\r\n$10\r\ninvalidate\r\n*1\r\n$5\r\nother\r\n$4\r\nmarc\r\n");

        Assert.Equal("marc", (string?)await pending);
    }

    [Fact]
    public async Task TheOperationCarriesItsOwnCopyOfTheRequest()
    {
        // the caller's rendered bytes are theirs only until the call completes, so the operation copies.
        // Holding a reference would be a use-after-free the moment the writer recycles its buffer - and
        // the symptom would be a correctly-framed request with somebody else's arguments in it.
        var (context, transport, _) = Connect();

        var first = context.Strings.GetAsync("user:1");
        var second = context.Strings.GetAsync("user:2");

        // both frames intact, in order, neither overwritten by the other's render
        Assert.Equal("*2|$3|GET|$6|user:1|*2|$3|GET|$6|user:2|", transport.Written);

        transport.Reply("$1\r\n1\r\n$1\r\n2\r\n");
        Assert.Equal("1", (string?)await first);
        Assert.Equal("2", (string?)await second);
    }
}
