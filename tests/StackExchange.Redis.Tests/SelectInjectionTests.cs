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
