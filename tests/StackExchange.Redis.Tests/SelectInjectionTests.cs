using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// One connection serving several databases, by writing <c>SELECT</c> immediately in front of any command
/// that belongs to a different one.
/// </summary>
/// <remarks>
/// <para>
/// This is what replaces a connection per (endpoint, database). The shipped core has always done it; the
/// new core could not, because the preamble mechanism took an old-core connection type - so it opened a
/// socket per database instead and <c>SELECT</c> never moved.
/// </para>
/// <para>
/// <b>Contiguity is the whole of the correctness here.</b> <c>SELECT</c> is sticky connection state, so
/// anything written between it and its command runs against the newly selected database instead. That is
/// silent - no error, just data in the wrong place - which is why the decision, the claim and the write
/// all happen inside one acquisition of the write lock.
/// </para>
/// </remarks>
public class SelectInjectionTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[4096];
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
                if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 4096);
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

    /// <summary>An executor bound to <paramref name="database"/>, over a connection that starts on 0.</summary>
    /// <remarks>
    /// One executor per database is still the shape: <c>RespContext.Database</c> is resolved from the
    /// executor whenever it has one, so a context cannot ask an executor for a database the executor is
    /// not already on. Sharing ONE connection between several such executors is the next step, and is what
    /// this injection exists to make safe.
    /// </remarks>
    private static (FakeTransport Transport, RespDatabaseContext Context) ForDatabase(int database)
    {
        var transport = new FakeTransport();
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)),
            database: database,
            endpoint: new IPEndPoint(IPAddress.Loopback, 6379),
            select: new SelectPreamble(new RespContext()));

        return (transport, new RespDatabaseContext(new RespContext(database: database).WithExecutor(executor)));
    }

    [Fact]
    public async Task ACommandForAnotherDatabaseCarriesItsSelect()
    {
        var (transport, context) = ForDatabase(3);
        var flushes = transport.Flushes;

        _ = context.Strings.GetAsync("k");
        await WaitFor(() => transport.Written.Contains("GET"));

        // one flush, and nothing between them: another sender taking the lock in the middle would run its
        // command against database 3, which is the silent failure this exists to prevent
        Assert.Equal(1, transport.Flushes - flushes);
        Assert.Equal("*2|$6|SELECT|$1|3|*2|$3|GET|$1|k|", transport.Written);
    }

    [Fact]
    public async Task ASecondCommandOnTheSameDatabaseCarriesNothing()
    {
        var (transport, context) = ForDatabase(3);

        _ = context.Strings.GetAsync("a");
        await WaitFor(() => transport.Written.Contains("SELECT"));
        transport.Reply("+OK\r\n$1\r\nA\r\n");

        var before = transport.Written;
        _ = context.Strings.GetAsync("b");
        await WaitFor(() => transport.Written.Length > before.Length);

        var second = transport.Written.Substring(before.Length);
        Assert.DoesNotContain("SELECT", second);
        Assert.Equal("*2|$3|GET|$1|b|", second);
    }

    /// <summary>
    /// A command written as a PAIR - a script and its <c>SCRIPT LOAD</c> - carries its <c>SELECT</c> too.
    /// </summary>
    /// <remarks>
    /// <b>The pair path had its own write and skipped the injection entirely</b>, so a script issued for
    /// database 3 ran against whatever the connection was last <c>SELECT</c>ed onto. Silent, and exactly the
    /// failure this class exists to prevent - found as <c>RespResultTests.ScriptEvaluateReadOnlyResp_Works</c>
    /// reading null from a key it had just written, intermittently, on a connection other tests were moving.
    /// </remarks>
    [Fact]
    public async Task APairedPreambleCarriesTheSelectToo()
    {
        var (transport, context) = ForDatabase(3);

        _ = context.Scripts.EvaluateAsync("return 1", default, default);
        await WaitFor(() => transport.Written.Contains("EVAL"));

        Assert.StartsWith("*2|$6|SELECT|$1|3|", transport.Written);
    }

    /// <summary>Database 0 needs nothing: a fresh connection is already there.</summary>
    [Fact]
    public async Task TheConnectionsOwnDatabaseNeedsNoSelect()
    {
        var (transport, context) = ForDatabase(0);

        _ = context.Strings.GetAsync("k");
        await WaitFor(() => transport.Written.Contains("GET"));

        Assert.Equal("*2|$3|GET|$1|k|", transport.Written);
    }

    /// <summary>
    /// <b>The property that makes it safe under load</b>: however many senders contend, exactly one
    /// <c>SELECT</c> goes out and every command sits behind it.
    /// </summary>
    [Fact]
    public async Task UnderContentionExactlyOneSelectIsWritten()
    {
        var (transport, context) = ForDatabase(7);

        const int Senders = 16;
        using var gun = new Barrier(Senders);
        for (var i = 0; i < Senders; i++)
        {
            var index = i;
            _ = Task.Run(() =>
            {
                gun.SignalAndWait();
                return context.Strings.GetAsync("k" + index).AsTask();
            });
        }

        await WaitFor(() => CountOf(transport.Written, "|GET|") == Senders, 10000);
        var wire = transport.Written;

        // one SELECT for the sixteen, and it is the FIRST thing on the wire - a second sender that decided
        // before the first had written would have put its GET in front of it
        Assert.Equal(1, CountOf(wire, "|SELECT|"));
        Assert.StartsWith("*2|$6|SELECT|$1|7|", wire);
    }

    /// <summary>
    /// A batch on another database is preceded by <b>one</b> <c>SELECT</c>, governing the whole run.
    /// </summary>
    /// <remarks>
    /// A run is composed through one context and so belongs to one database, and it is written with
    /// nothing of anybody else's in between - so a single SELECT at the front covers all of it. What has
    /// to hold is that the operations agree with each other, not that they agree with whatever the socket
    /// last selected.
    /// </remarks>
    [Fact]
    public async Task ABatchOnAnotherDatabaseIsPrecededByOneSelect()
    {
        var (transport, context) = ForDatabase(4);

        using var batch = context.CreateBatch();
        _ = batch.Context.Strings.GetAsync("a");
        _ = batch.Context.Strings.GetAsync("b");
        _ = batch.ExecuteAsync();

        await WaitFor(() => CountOf(transport.Written, "|GET|") == 2);

        // one SELECT, at the front, governing both commands. Contiguity comes from the write lock rather
        // than from the flush count - the connect path can flush on its own account - so the wire is what
        // is asserted here.
        Assert.Equal("*2|$6|SELECT|$1|4|*2|$3|GET|$1|a|*2|$3|GET|$1|b|", transport.Written);
    }

    /// <summary>And a transaction puts it in front of <c>MULTI</c>, the only place it can go.</summary>
    /// <remarks>
    /// Inside the transaction a <c>SELECT</c> would be queued and applied at <c>EXEC</c> like any other
    /// command - which is not what "run these against database 4" means.
    /// </remarks>
    [Fact]
    public async Task ATransactionSelectsBeforeMulti()
    {
        var (transport, context) = ForDatabase(4);

        var tran = new RespTransactionExecutor(ExecutorOf(context));
        var inside = new RespDatabaseContext(new RespContext().WithExecutor(tran));
        _ = inside.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        await WaitFor(() => transport.Written.Contains("EXEC"));

        Assert.StartsWith("*2|$6|SELECT|$1|4|*1|$5|MULTI|", transport.Written);
        Assert.Equal(1, CountOf(transport.Written, "|SELECT|"));

        transport.Reply("+OK\r\n+OK\r\n+QUEUED\r\n*1\r\n$1\r\nA\r\n"); // SELECT, MULTI, queued, EXEC
        Assert.True(await executing);
    }

    /// <summary>
    /// A <b>conditional</b> transaction selects once, before its <c>WATCH</c> - so the watches guard keys
    /// in the same database the body operates on.
    /// </summary>
    /// <remarks>
    /// This is the one that bites silently. The WATCH phase used to be written straight to the connection
    /// while the MULTI run went through the executor, so the watches landed on whatever the connection had
    /// last selected and the body on the right database. Nothing errors: the condition simply reaches its
    /// verdict against the wrong keys, and the transaction applies or aborts for the wrong reason.
    /// </remarks>
    [Fact]
    public async Task AConditionalTransactionSelectsBeforeItsWatch()
    {
        var (transport, context) = ForDatabase(4);

        var tran = new RespTransactionExecutor(ExecutorOf(context), new RespContext());
        tran.AddCondition(Condition.KeyExists("guard"));
        var inside = new RespDatabaseContext(new RespContext().WithExecutor(tran));
        _ = inside.Strings.GetAsync("a");
        var executing = tran.ExecuteAsync();

        await WaitFor(() => transport.Written.Contains("EXISTS"));

        // the SELECT comes first, and the WATCH is inside it rather than in front of it
        Assert.StartsWith("*2|$6|SELECT|$1|4|*2|$5|WATCH|$5|guard|*2|$6|EXISTS|$5|guard|", transport.Written);

        transport.Reply("+OK\r\n+OK\r\n:1\r\n"); // SELECT, WATCH, and the key exists
        await WaitFor(() => transport.Written.Contains("EXEC"));

        // and the body needs no second SELECT: the write slot is held throughout, so nothing moved it
        Assert.Equal(1, CountOf(transport.Written, "|SELECT|"));

        transport.Reply("+OK\r\n+QUEUED\r\n*1\r\n$1\r\nA\r\n");
        Assert.True(await executing);
    }

    private static RespExecutorBase ExecutorOf(RespDatabaseContext context)
        => context.Raw.Executor ?? throw new InvalidOperationException("no executor");

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
