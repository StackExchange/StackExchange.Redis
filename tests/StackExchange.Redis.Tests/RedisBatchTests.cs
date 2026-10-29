using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="IBatch"/> and <see cref="ITransaction"/> on the new core - the last two members to leave
/// <c>RedisDatabase</c>'s fallback, and the gate on the whole deletion. Design notes 7r.
/// </summary>
public class RedisBatchTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

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

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        public override ValueTask DisposeAsync() => default;
    }

    private static async Task<(IDatabase Database, FakeTransport Transport)> ConnectedAsync()
    {
        var transport = new FakeTransport();
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)),
            endpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379));

        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        IDatabase database = new RedisDatabase(context, null!, null);

        // force the connection up, so later assertions see only what the batch wrote
        var warming = database.StringGetAsync("warm");
        await WaitFor(() => transport.Written.Length > 0);
        transport.Reply("$4\r\nwarm\r\n");
        await warming;
        return (database, transport);
    }

    [Fact]
    public async Task ABatchWritesEverythingQueuedAsOneRun()
    {
        var (database, transport) = await ConnectedAsync();
        var before = transport.Written;

        var batch = database.CreateBatch();
        var first = batch.StringGetAsync("a");
        var second = batch.StringIncrementAsync("b");

        Assert.Equal(before, transport.Written); // nothing has gone out yet - that IS the batch

        batch.Execute();
        await WaitFor(() => transport.Written.Length > before.Length);
        Assert.Equal("*2|$3|GET|$1|a|*2|$4|INCR|$1|b|", transport.Written.Substring(before.Length));

        transport.Reply("$1\r\nA\r\n:7\r\n");
        Assert.Equal("A", (string?)await first);
        Assert.Equal(7, await second);
    }

    [Fact]
    public async Task ABatchIsAnOrdinaryDatabaseWithADifferentExecutor()
    {
        // the point of the shape: ~504 members are INHERITED, not forwarded, because the batching lives
        // entirely in the executor. Nothing on the command path was told it was in a batch.
        var (database, _) = await ConnectedAsync();
        var batch = database.CreateBatch();

        Assert.IsAssignableFrom<IDatabaseAsync>(batch);
        Assert.Equal(database.Database, ((IDatabaseAsync)batch).Database);
    }

    [Fact]
    public async Task ScanningInsideABatchIsRefusedRatherThanOfferedBroken()
    {
        // the cursor for page two is read out of page one's REPLY, and a batch has not been sent; the
        // shipped surface inherits RedisDatabase's implementation here and offers a scan that cannot move
        var (database, _) = await ConnectedAsync();
        var batch = database.CreateBatch();

        var ex = Assert.Throws<NotSupportedException>(() => batch.HashScanAsync("k"));
        Assert.Contains("cannot run inside a batch", ex.Message);
    }

    [Fact]
    public async Task ATransactionRunsThroughTheSurfaceAsMultiExec()
    {
        var (database, transport) = await ConnectedAsync();
        var before = transport.Written;

        var tran = database.CreateTransaction();
        var pending = tran.StringIncrementAsync("counter");

        var executing = tran.ExecuteAsync();
        await WaitFor(() => transport.Written.Length > before.Length);
        Assert.Equal("*1|$5|MULTI|*2|$4|INCR|$7|counter|*1|$4|EXEC|", transport.Written.Substring(before.Length));

        transport.Reply("+OK\r\n+QUEUED\r\n*1\r\n:1\r\n");
        Assert.True(await executing);
        Assert.Equal(1, await pending);
        Assert.False(tran.WasWatchConflict);
    }

    [Fact]
    public async Task AConditionResultIsFilledInWhenItsCheckIsAnswered()
    {
        // handed back when the condition is ADDED and filled in later, which is what keeps "which one
        // failed?" answerable - a single bool from Execute cannot say that
        var (database, transport) = await ConnectedAsync();

        var tran = database.CreateTransaction();
        var ok = tran.AddCondition(Condition.KeyExists("present"));
        var bad = tran.AddCondition(Condition.KeyExists("absent"));
        _ = tran.StringIncrementAsync("counter");

        Assert.False(ok.WasSatisfied); // not yet known
        Assert.False(bad.WasSatisfied);

        var executing = tran.ExecuteAsync();
        transport.Reply("+OK\r\n:1\r\n+OK\r\n:0\r\n");

        Assert.False(await executing);
        Assert.True(ok.WasSatisfied);
        Assert.False(bad.WasSatisfied);

        // a condition that did not hold is NOT a watch conflict: the state was not what you wanted,
        // rather than having been changed underneath you. Only the latter is worth retrying.
        Assert.False(tran.WasWatchConflict);
    }

    [Fact]
    public async Task AWatchConflictIsDistinctFromAFailedCondition()
    {
        var (database, transport) = await ConnectedAsync();

        var tran = database.CreateTransaction();
        var condition = tran.AddCondition(Condition.KeyExists("guard"));
        var pending = tran.StringIncrementAsync("counter");

        var executing = tran.ExecuteAsync();
        transport.Reply("+OK\r\n:1\r\n");                  // the condition HELD
        await WaitFor(() => transport.Written.Contains("MULTI"));
        transport.Reply("+OK\r\n+QUEUED\r\n*-1\r\n");      // ...and EXEC aborted anyway

        Assert.False(await executing);
        Assert.True(condition.WasSatisfied);
        Assert.True(tran.WasWatchConflict);
        await Assert.ThrowsAnyAsync<Exception>(async () => await pending);
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
