using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Runs an iteration delegate concurrently on several workers until stopped - the analogue of the
/// cross-client scenario suites' multi-threaded fake app. Connection-shaped failures
/// (<see cref="RedisConnectionException"/>/<see cref="RedisTimeoutException"/>) escaping an iteration are
/// captured and the worker keeps going; anything else propagates and fails the run.
/// </summary>
/// <remarks>
/// The only concurrent workload in this tier, and it exists because a failover is only observable under load:
/// the circuit breaker trips on *observed* failures, so a single foreground ping loop - what every other test
/// here uses - generates too few in-flight commands during the outage to drive one.
/// </remarks>
internal sealed class MultiThreadedWorkload(Func<int, CancellationToken, Task> iteration, int workerCount)
{
    private Task? _completion;

    public ConcurrentQueue<Exception> CapturedExceptions { get; } = new();

    /// <summary>
    /// Completes when every worker has observed the stop token (faulted if any worker hit a
    /// non-connection exception).
    /// </summary>
    public Task Completion => _completion ?? throw new InvalidOperationException("Workload has not been started.");

    public void Start(CancellationToken stopToken)
    {
        if (_completion is not null) throw new InvalidOperationException("Workload has already been started.");

        RaiseThreadPoolFloor();

        var workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i;
            // Task.Run rather than dedicated threads: the multiplexer is thread-agnostic, and the floor raised
            // above covers the burst. If pool starvation ever skews timings, TaskCreationOptions.LongRunning is
            // the one-line fix.
            workers[i] = Task.Run(() => RunWorkerAsync(workerId, stopToken), CancellationToken.None);
        }

        _completion = Task.WhenAll(workers);
    }

    /// <summary>
    /// Raises the worker/completion-port minimums before the workers start.
    /// </summary>
    /// <remarks>
    /// Done here rather than inherited: the main test project raises this floor in <c>TestConfig</c>'s static
    /// constructor, and this project has no <c>TestConfig</c> - so without this the floor is whatever the
    /// runtime defaults to. It matters at exactly one moment: when the injector blackholes the endpoint, every
    /// in-flight command's timeout and every retry's delay come due together, and a pool still growing at its
    /// injection rate would add its own latency to the very interval the test measures.
    /// </remarks>
    private static void RaiseThreadPoolFloor()
    {
        try
        {
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            var target = Math.Max(64, Environment.ProcessorCount * 8);
            ThreadPool.SetMinThreads(Math.Max(workerThreads, target), Math.Max(completionPortThreads, target));
        }
        catch (Exception)
        {
            // advisory only - a host that refuses is not a reason to fail the run
        }
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                await iteration(workerId, stopToken).ForAwait();
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                return; // clean stop
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                CapturedExceptions.Enqueue(ex);
            }
        }
    }
}
