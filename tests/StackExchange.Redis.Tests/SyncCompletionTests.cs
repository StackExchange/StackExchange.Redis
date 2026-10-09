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
/// operation is waited on directly, through a blocking context (see <c>RespContext.Blocking</c>).
/// </para>
/// <para>
/// <b>Measured relative to blocking on the async API</b>, which still pays the hop, and also as an absolute.
/// The absolute is the socket's: on Linux a socket that has done any async operation is registered with the
/// runtime's epoll engine, which wakes even a synchronous read through the pool - so a dedicated connection
/// is now opened synchronously, and costs none (measured: 500 pool items for 500 sync calls before, 0 after;
/// <c>DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1</c> had the same effect process-wide). In the
/// non-parallel collection, so nothing else is using the pool.
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

            // ...and costs the pool nothing at all: the connection was opened synchronously, so its socket never
            // registered for asynchronous IO, and a reply wakes the blocked reader without a pool work item (see
            // RespTransportFactory.RunBlocking). A tenth, not zero, for the timers that share the process.
            Assert.True(
                sync <= Calls / 10,
                $"a dedicated connection's synchronous calls should not need the pool at all: {sync} pool work items for {Calls} calls");
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag("DedicatedThreads", wasSet);
        }
    }
}
#endif
