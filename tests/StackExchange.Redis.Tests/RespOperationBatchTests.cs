using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Batching, rebuilt on operations. Design notes 3c: element 3 of 5 <i>is</i> an operation, so there is
/// no return channel to design.
/// </summary>
public class RespOperationBatchTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal int Flushes;

        internal string Written
        {
            get { lock (_sync) return Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|"); }
        }

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

        public override bool Flush()
        {
            Interlocked.Increment(ref Flushes);
            return true;
        }

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        public override ValueTask DisposeAsync() => default;
    }

    private static async Task<(RespEndpointExecutor Endpoint, FakeTransport Transport)> ConnectedAsync()
    {
        var transport = new FakeTransport();
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespConnection(transport)),
            endpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379));

        // warm it with an ordinary command, as real traffic would
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        var warming = context.Strings.GetAsync("warm");
        await WaitFor(() => transport.Written.Length > 0);
        transport.Reply("$4\r\nwarm\r\n");
        await warming;
        return (executor, transport);
    }

    [Fact]
    public async Task NothingIsSentUntilTheBatchIsExecuted()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");

        Assert.Equal(2, batch.Count);
        Assert.Equal(before, transport.Written.Length); // still nothing on the wire

        await batch.ExecuteAsync();
        Assert.Equal(0, batch.Count);

        transport.Reply("$1\r\nA\r\n$1\r\nB\r\n");
        Assert.Equal("A", (string?)await first);
        Assert.Equal("B", (string?)await second);
    }

    [Fact]
    public async Task TheWholeBatchIsOneContiguousWriteAndOneFlush()
    {
        // contiguity is the point: anything interleaved between these would break the adjacency a caller
        // batched for, and a flush per element would defeat the round-trip saving entirely
        var (endpoint, transport) = await ConnectedAsync();
        var flushesBefore = transport.Flushes;
        var writtenBefore = transport.Written;

        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        var pending = new[] { context.Strings.GetAsync("a"), context.Strings.GetAsync("b"), context.Strings.GetAsync("c") };
        await batch.ExecuteAsync();

        var added = transport.Written.Substring(writtenBefore.Length);
        Assert.Equal("*2|$3|GET|$1|a|*2|$3|GET|$1|b|*2|$3|GET|$1|c|", added);
        Assert.Equal(1, transport.Flushes - flushesBefore);

        transport.Reply("$1\r\nA\r\n$1\r\nB\r\n$1\r\nC\r\n");
        Assert.Equal("A", (string?)await pending[0]);
        Assert.Equal("B", (string?)await pending[1]);
        Assert.Equal("C", (string?)await pending[2]);
    }

    [Fact]
    public async Task EachElementCompletesItselfIncludingFailures()
    {
        // THE thing section 3c said would stop being a problem: no result collection, no scatter-back.
        // A server error on element two does not disturb one or three.
        var (endpoint, transport) = await ConnectedAsync();
        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.IncrementAsync("b");
        var third = context.Strings.GetAsync("c");
        await batch.ExecuteAsync();

        transport.Reply("$1\r\nA\r\n-WRONGTYPE nope\r\n$1\r\nC\r\n");

        Assert.Equal("A", (string?)await first);
        await Assert.ThrowsAsync<RedisServerException>(async () => await second);
        Assert.Equal("C", (string?)await third);
    }

    [Fact]
    public async Task AnAbandonedBatchFailsEveryElementAsNeverSent()
    {
        var (endpoint, _) = await ConnectedAsync();
        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");

        batch.Abandon(new InvalidOperationException("discarded"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
    }

    [Fact]
    public async Task ExecutingTwiceIsRefused()
    {
        var (endpoint, _) = await ConnectedAsync();
        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        await batch.ExecuteAsync();

        // adding to a batch that has already gone is a programming error, not a runtime condition - it
        // says nothing about the server, and silently starting a second batch would be worse
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await context.Strings.GetAsync("a"));
    }

    [Fact]
    public async Task AnEmptyBatchSendsNothing()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var batch = new RespOperationBatchExecutor(endpoint);

        await batch.ExecuteAsync();

        Assert.Equal(before, transport.Written.Length);
    }

    private static async Task WaitFor(Func<bool> condition, int millis = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > millis) Assert.Fail("condition not met within the time allowed");
            await Task.Delay(5);
        }
    }
}
