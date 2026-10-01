using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// How long a transaction blocks a <i>thread</i> before its work is even in flight.
/// </summary>
/// <remarks>
/// <para>
/// A transaction with conditions cannot choose between EXEC, DISCARD and UNWATCH until the server has
/// answered, so it waits - <c>Monitor.Wait</c> on a result box, inside
/// <c>TransactionMessage.GetMessages</c>, which is enumerated inside the bridge's write lock. The round
/// trip is unavoidable; parking a thread across it is the part worth questioning.
/// </para>
/// <para>
/// Measured as the <b>synchronous</b> duration of <c>ExecuteAsync</c> - the time before it hands back a
/// task - because that is what "a thread is parked" looks like from outside. Latency is injected into the
/// in-process server so a round trip is far larger than scheduling noise, and the no-conditions case is
/// the control: same transaction, same latency, no reply to wait for.
/// </para>
/// </remarks>
public class TransactionWriteLockStallTests(ITestOutputHelper log)
{
    private static readonly TimeSpan Injected = TimeSpan.FromMilliseconds(250);

    private static async Task<(TimeSpan Sync, TimeSpan Total)> TimeAsync(InProcessTestServer server, bool withCondition)
    {
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        var db = muxer.GetDatabase();
        await db.StringSetAsync("cond", "v");
        await db.PingAsync(); // warm, so nothing here is handshake

        server.SetLatency(Injected);
        try
        {
            var tran = db.CreateTransaction();
            if (withCondition) tran.AddCondition(Condition.StringEqual("cond", "v"));
            _ = tran.StringSetAsync("other", "x");

            var watch = Stopwatch.StartNew();
            var pending = tran.ExecuteAsync();   // synchronous portion ends here
            var sync = watch.Elapsed;
            await pending;
            return (sync, watch.Elapsed);
        }
        finally
        {
            server.SetLatency(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task AConditionParksAThreadForARoundTrip()
    {
        using var server = new InProcessTestServer(log);

        var control = await TimeAsync(server, withCondition: false);
        var withCondition = await TimeAsync(server, withCondition: true);

        log.WriteLine($"injected round trip:      {Injected.TotalMilliseconds:0}ms");
        log.WriteLine($"no conditions:  sync {control.Sync.TotalMilliseconds:0}ms, total {control.Total.TotalMilliseconds:0}ms");
        log.WriteLine($"one condition:  sync {withCondition.Sync.TotalMilliseconds:0}ms, total {withCondition.Total.TotalMilliseconds:0}ms");
        log.WriteLine($"=> the condition added {(withCondition.Sync - control.Sync).TotalMilliseconds:0}ms of BLOCKED thread time");

        // no threshold: the numbers are the finding, and a margin on a shared machine is a flake
        Assert.True(withCondition.Total > TimeSpan.Zero);
    }
}
