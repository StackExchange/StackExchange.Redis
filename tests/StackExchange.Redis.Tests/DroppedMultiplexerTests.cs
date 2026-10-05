using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A multiplexer the caller drops - neither disposed nor referenced - while a command is still in flight.
/// </summary>
/// <remarks>
/// v3 kept such a multiplexer alive on purpose: its heartbeat rooted the multiplexer while caller work was
/// pending, so the work still timed out instead of hanging for ever on a task nothing would complete. That
/// mechanism went with the bridges, which were what triggered it. The new core should not need it - an open
/// socket's pending read and the retry timers reach the core, and the core holds the multiplexer - and this is
/// the test of that belief rather than the belief itself.
/// </remarks>
public class DroppedMultiplexerTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task InFlightWorkStillTimesOutAfterTheMultiplexerIsDropped()
    {
        using var server = new InProcessTestServer(Output);
        try
        {
            var (pending, muxer) = await StartAndDropAsync(server);

            // as hard as we can make it try: if nothing roots the multiplexer, this collects it
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Log($"multiplexer collected: {!muxer.TryGetTarget(out _)}");

            // the timeout is 500ms; ten seconds means "never", and a hang is exactly the failure being tested
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsType<RedisTimeoutException>(ex);
        }
        finally
        {
            server.SetLatency(TimeSpan.Zero);
        }
    }

    /// <summary>Connect, start a command the server will not answer in time, and return without the multiplexer.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(Task<RedisValue> Pending, WeakReference<ConnectionMultiplexer> Muxer)> StartAndDropAsync(InProcessTestServer server)
    {
        var config = server.GetClientConfig();
        config.AsyncTimeout = 500;
        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        await conn.GetDatabase().PingAsync(); // connected and handshaken, so the command is IN FLIGHT, not backlogged

        server.SetLatency(TimeSpan.FromSeconds(30)); // every reply from here on is held
        var pending = conn.GetDatabase().StringGetAsync("dropped");
        return (pending, new WeakReference<ConnectionMultiplexer>(conn));
    }
}
