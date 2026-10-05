#if NET
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A blocked synchronous caller is woken by the reader, not by a thread-pool hop.
/// </summary>
/// <remarks>
/// <para>
/// Operations complete their continuations on the pool, and a synchronous call blocks on a task over one,
/// so the reply arrived and the caller still waited for a pool thread to finish the task - one hop per call
/// on top of anything else, and under a saturated pool (the case <c>DedicatedThreads</c> exists for) the hop
/// that kept the caller blocked. v3 pulsed its sync waiters from the reader; now the blocked caller's
/// operation completes inline (see <c>SyncCall</c>).
/// </para>
/// <para>
/// <b>Measured relative to blocking on the async API</b>, which still pays the hop, rather than as an absolute:
/// the socket layer has costs of its own that vary by platform - on Linux a socket that has done any async
/// operation completes even synchronous reads through the pool unless
/// <c>DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1</c> (measured: 0 pool items for 500 sync calls with it, 500
/// without, either way one fewer per call than the async API). In the non-parallel collection, so nothing
/// else is using the pool.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class SyncCompletionTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ASyncCallSavesTheCompletionHop()
    {
        var wasSet = ConnectionMultiplexer.GetFeatureFlag("DedicatedThreads");
        ConnectionMultiplexer.SetFeatureFlag("DedicatedThreads", true);
        try
        {
            // a REAL server: an in-process one does its own work on this process's pool, and would be counted
            await using var conn = Create(shared: false);
            var db = conn.GetDatabase();
            RedisKey key = Me();
            db.StringSet(key, "v");
            for (var i = 0; i < 50; i++) db.StringGet(key); // warm up

            const int Calls = 500;
            static long Measure(Action action)
            {
                var before = ThreadPool.CompletedWorkItemCount;
                action();
                return ThreadPool.CompletedWorkItemCount - before;
            }

            var sync = Measure(() =>
            {
                for (var i = 0; i < Calls; i++) Assert.Equal("v", (string?)db.StringGet(key));
            });
#pragma warning disable SER307 // blocking on the async API IS the comparison
            var blockedOnAsync = Measure(() =>
            {
                for (var i = 0; i < Calls; i++) Assert.Equal("v", (string?)db.StringGetAsync(key).GetAwaiter().GetResult());
            });
#pragma warning restore SER307

            Log($"{Calls} calls: {sync} pool work items synchronously, {blockedOnAsync} blocking on the async API");
            Assert.True(
                sync <= blockedOnAsync - (Calls * 8 / 10),
                $"a synchronous call should save the completion hop: {sync} vs {blockedOnAsync} pool work items for {Calls} calls");
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag("DedicatedThreads", wasSet);
        }
    }
}
#endif
