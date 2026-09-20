using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Transactions on the new core: <c>MULTI</c>, the queued commands, <c>EXEC</c> - one contiguous run,
/// with the results distributed from <c>EXEC</c>'s array. Design notes 3c and 7r.
/// </summary>
public class RespTransactionTests
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
            _ => Task.FromResult<RespConnection>(new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)),
            endpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379));

        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        var warming = context.Strings.GetAsync("warm");
        await WaitFor(() => transport.Written.Length > 0);
        transport.Reply("$4\r\nwarm\r\n");
        await warming;
        return (executor, transport);
    }

    [Fact]
    public async Task TheWholeTransactionIsOneContiguousRun()
    {
        // MULTI is per-connection state: anything interleaved between it and EXEC would JOIN the
        // transaction rather than run beside it, which is a stronger requirement than a batch's
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written;
        var flushes = transport.Flushes;

        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.IncrementAsync("b");
        Assert.Equal(2, tran.Count);

        var executing = tran.ExecuteAsync();
        var added = transport.Written.Substring(before.Length);
        Assert.Equal("*1|$5|MULTI|*2|$3|GET|$1|a|*2|$4|INCR|$1|b|*1|$4|EXEC|", added);
        Assert.Equal(1, transport.Flushes - flushes);

        // +OK for MULTI, a receipt per command, then the array
        transport.Reply("+OK\r\n+QUEUED\r\n+QUEUED\r\n*2\r\n$1\r\nA\r\n:7\r\n");

        Assert.True(await executing);
        Assert.Equal("A", (string?)await first);
        Assert.Equal(7, await second);
    }

    [Fact]
    public async Task AQueuedReceiptIsNotMistakenForAResult()
    {
        // the difference from a batch, and the reason the hand-off hook earns its keep: a queued command
        // gets TWO replies, and completing it with the first would hand the caller "QUEUED"
        var (endpoint, transport) = await ConnectedAsync();
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var pending = context.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        transport.Reply("+OK\r\n+QUEUED\r\n");
        Assert.False(pending.IsCompleted); // the receipt did NOT complete it

        transport.Reply("*1\r\n$5\r\nvalue\r\n");
        Assert.True(await executing);
        Assert.Equal("value", (string?)await pending);
    }

    [Fact]
    public async Task AnAbortedTransactionReportsThatNothingRan()
    {
        // a null EXEC reply means a watched key changed: the commands did not run, which is a distinct
        // outcome from failing, and every queued command has to be told
        var (endpoint, transport) = await ConnectedAsync();
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        var executing = tran.ExecuteAsync();

        transport.Reply("+OK\r\n+QUEUED\r\n+QUEUED\r\n*-1\r\n");

        Assert.False(await executing);
        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await first);
        Assert.Contains("aborted", ex.Message);
        await Assert.ThrowsAsync<RedisServerException>(async () => await second);
    }

    [Fact]
    public async Task AnAggregateResultIsDistributedWhole()
    {
        // the case that would break a hand-rolled slice: a queued LRANGE returns an aggregate of its own,
        // nested inside EXEC's array, so the elements have to be SCANNED rather than assumed to be scalars
        var (endpoint, transport) = await ConnectedAsync();
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var list = context.Lists.RangeAsync("k", 0, -1);
        var count = context.Strings.IncrementAsync("n");
        var executing = tran.ExecuteAsync();

        transport.Reply("+OK\r\n+QUEUED\r\n+QUEUED\r\n*2\r\n*3\r\n$1\r\nx\r\n$1\r\ny\r\n$1\r\nz\r\n:42\r\n");

        Assert.True(await executing);
        using var values = await list;
        Assert.Equal(3, values.Length);
        Assert.Equal("x", (string?)values.Span[0]);
        Assert.Equal("z", (string?)values.Span[2]);
        Assert.Equal(42, await count);
    }

    [Fact]
    public async Task TooFewResultsFailsTheCommandsThatHaveNone()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        var executing = tran.ExecuteAsync();

        transport.Reply("+OK\r\n+QUEUED\r\n+QUEUED\r\n*1\r\n$1\r\nA\r\n");

        await executing;
        Assert.Equal("A", (string?)await first);
        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await second);
        Assert.Contains("fewer results", ex.Message);
    }

    [Fact]
    public async Task AnAbandonedTransactionFailsEveryQueuedCommand()
    {
        var (endpoint, _) = await ConnectedAsync();
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var pending = context.Strings.GetAsync("a");
        tran.Abandon(new InvalidOperationException("discarded"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
    }

    [Fact]
    public async Task AnEmptyTransactionSucceedsWithoutSendingAnything()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var tran = new RespTransactionExecutor(endpoint);

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal(before, transport.Written.Length);
    }

    [Fact]
    public async Task AConditionWatchesItsKeyAndIsCheckedBeforeMultiIsSent()
    {
        // the whole of design notes 3c: with a condition this is TWO contiguous runs, because whether to
        // send the MULTI at all is not known until the check has been answered
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written;

        var tran = new RespTransactionExecutor(endpoint, new RespContext());
        tran.AddCondition(Condition.KeyExists("guard"));
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var pending = context.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        // run one: the watch and its check, and NOTHING else - no MULTI yet
        var first = transport.Written.Substring(before.Length);
        Assert.Equal("*2|$5|WATCH|$5|guard|*2|$6|EXISTS|$5|guard|", first);

        transport.Reply("+OK\r\n:1\r\n"); // watched, and the key exists
        await WaitFor(() => transport.Written.Length > before.Length + first.Length);

        // run two, only now that the condition is known to hold
        var second = transport.Written.Substring(before.Length + first.Length);
        Assert.Equal("*1|$5|MULTI|*2|$3|GET|$1|a|*1|$4|EXEC|", second);

        transport.Reply("+OK\r\n+QUEUED\r\n*1\r\n$1\r\nA\r\n");
        Assert.True(await executing);
        Assert.Equal("A", (string?)(await pending).ToString());
    }

    [Fact]
    public async Task AFailedConditionSendsNoMultiAtAllAndReleasesTheWatch()
    {
        // not "sent and rolled back" - never sent. The queued commands did not run, which is a distinct
        // outcome from failing, so they complete as cancelled rather than faulted
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written;

        var tran = new RespTransactionExecutor(endpoint, new RespContext());
        tran.AddCondition(Condition.KeyExists("guard"));
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var pending = context.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        var first = transport.Written.Substring(before.Length);
        transport.Reply("+OK\r\n:0\r\n"); // watched, and the key does NOT exist

        Assert.False(await executing);

        var second = transport.Written.Substring(before.Length + first.Length);
        Assert.Equal("*1|$7|UNWATCH|", second); // no MULTI, and the watch does not leak onto the connection
        Assert.DoesNotContain("MULTI", second);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task EveryConditionIsWatchedBeforeItsOwnCheck()
    {
        // interleaved, not batched: a check that ran before its own watch reads a value the watch is not
        // yet guarding, which reopens the exact window the condition exists to close
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written;

        var tran = new RespTransactionExecutor(endpoint, new RespContext());
        tran.AddCondition(Condition.KeyExists("one"));
        tran.AddCondition(Condition.KeyNotExists("two"));
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        _ = context.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        Assert.Equal(
            "*2|$5|WATCH|$3|one|*2|$6|EXISTS|$3|one|*2|$5|WATCH|$3|two|*2|$6|EXISTS|$3|two|",
            transport.Written.Substring(before.Length));

        transport.Reply("+OK\r\n:1\r\n+OK\r\n:0\r\n"); // one exists, two does not: both hold
        await WaitFor(() => transport.Written.Contains("MULTI"));
        transport.Reply("+OK\r\n+QUEUED\r\n*1\r\n$1\r\nA\r\n");
        Assert.True(await executing);
    }

    [Fact]
    public async Task AConditionWithNothingQueuedStillReleasesItsWatch()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written;

        var tran = new RespTransactionExecutor(endpoint, new RespContext());
        tran.AddCondition(Condition.KeyExists("guard"));

        var executing = tran.ExecuteAsync();
        var first = transport.Written.Substring(before.Length);
        transport.Reply("+OK\r\n:1\r\n");

        Assert.True(await executing);
        Assert.Equal("*1|$7|UNWATCH|", transport.Written.Substring(before.Length + first.Length));
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
