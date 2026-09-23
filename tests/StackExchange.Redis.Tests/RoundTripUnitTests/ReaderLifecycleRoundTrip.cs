using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Exercises the filler/parser split in <c>PhysicalConnection.ReadAllAsync</c>/<c>ReadAllSync</c> through
/// the normal request/response machinery, for behaviours that a simple one-shot round trip does not cover.
/// </summary>
public class ReaderLifecycleRoundTrip(ITestOutputHelper log)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Theory(Timeout = 15_000)]
    [InlineData(WriteMode.Default)]
    [InlineData(WriteMode.Sync)]
    public async Task ReceivedBytesAreCounted(WriteMode mode)
    {
        // the received-byte counter feeds the "in:"/"recd=" diagnostics in timeout and connection-failure
        // messages; it is maintained by the filler, so it must keep working in both reader modes
        using var conn = new TestConnection(startReading: true, writeMode: mode, log: log);
        Assert.Equal(0, conn.GetBytesReceived());

        Assert.True(await Bounded(conn.PingAsync()));
        Assert.Equal("+PONG\r\n".Length, conn.GetBytesReceived());

        Assert.True(await Bounded(conn.PingAsync()));
        Assert.Equal(2 * "+PONG\r\n".Length, conn.GetBytesReceived());
    }

    [Fact(Timeout = 30_000)]
    public async Task SyncToAsyncHandoffLosesNoReplies()
    {
        using var conn = new TestConnection(startReading: true, writeMode: WriteMode.Sync, log: log);
        Assert.True(conn.IsSyncWriter);
        Assert.True(conn.IsSyncReader);
        Assert.True(await Bounded(conn.PingAsync(), "sync round trip"));

        // flip the writer; the reader follows on its next wake, i.e. after it has parsed the *next* reply
        Assert.True(conn.TransitionToAsync(), "writer should accept the transition");
        SpinUntil(() => !conn.IsSyncWriter, "writer should leave sync mode");
        Assert.True(await Bounded(conn.PingAsync(), "reply that triggers the transition"));

        // The sync parse loop has now returned and is waiting for its filler thread to stop - and that filler
        // is blocked in a read, with nothing to read. The next reply lands in *that* read: it must be
        // committed and parsed, not dropped, or this command never completes and every later reply on the
        // connection pairs with the wrong command.
        SpinUntil(() => conn.GetReadStatus() == PhysicalConnection.ReadStatus.TransitioningToAsync, "reader should be handing off");
        Assert.True(await Bounded(conn.PingAsync(), "reply that arrives mid-handoff"));

        // and from here on the async reader owns the connection
        SpinUntil(() => conn.GetReadStatus() == PhysicalConnection.ReadStatus.ReadAsync, "async reader should take over");
        Assert.False(conn.IsSyncReader);
        Assert.True(await Bounded(conn.PingAsync(), "async round trip"));
        Assert.True(await Bounded(conn.PingAsync(), "second async round trip"));
        Assert.Equal(5 * "+PONG\r\n".Length, conn.GetBytesReceived());
    }

    private static void SpinUntil(Func<bool> condition, string what)
        => Assert.True(SpinWait.SpinUntil(condition, Patience), $"timed out: {what}");

    private static async Task<T> Bounded<T>(Task<T> task, string? what = null)
    {
        // Task.WaitAsync is not available on every test TFM, so: race against a delay, but fail with a
        // message rather than just hanging until the test-level timeout fires
        var completed = await Task.WhenAny(task, Task.Delay(Patience));
        Assert.True(ReferenceEquals(completed, task), $"timed out waiting for reply{(what is null ? "" : ": " + what)}");
        return await task;
    }
}
