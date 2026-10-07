using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RESPite;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A faulted command task that the caller drops must not surface as <see cref="TaskScheduler.UnobservedTaskException"/>.
/// </summary>
/// <remarks>
/// v3 marked every faulted command task observed as it faulted; down-level, with <c>ThrowUnobservedTaskExceptions</c>
/// enabled, an unobserved fault takes the process down. v4's transitional surface handed out
/// <c>ValueTask.AsTask()</c>, which does not, until <c>TaskBridge</c> bridged through a task of its own.
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class UnobservedFaultTests
{
    private static readonly ConcurrentQueue<string> Seen = new();

    static UnobservedFaultTests()
        => TaskScheduler.UnobservedTaskException += (_, args) => Seen.Enqueue(args.Exception.ToString());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADroppedFaultedCommandIsNotReportedUnobserved(bool withAsyncState)
    {
        var marker = "dropped-" + Guid.NewGuid().ToString("N");
        await FaultAndDropAsync(marker, withAsyncState);

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.DoesNotContain(Seen, entry => entry.Contains(marker));
    }

    /// <summary>A caller who awaits still gets the fault: observed is not swallowed.</summary>
    [Fact]
    public async Task AnAwaitedFaultStillThrows()
    {
        var db = Database("awaited", asyncState: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.StringGetAsync("k"));
        Assert.Equal("awaited", ex.Message);
    }

    /// <summary>
    /// Why v3's <c>GC.SuppressFinalize(task)</c> is not copied: <see cref="Task"/> declares no finalizer - the
    /// unobserved-exception finalizer is on its internal exception holder.
    /// </summary>
    [Fact]
    public void TaskItselfHasNoFinalizer()
        => Assert.Null(typeof(Task).GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));

    [MethodImpl(MethodImplOptions.NoInlining)] // so nothing here keeps the task reachable afterwards
    private static async Task FaultAndDropAsync(string marker, bool withAsyncState)
    {
        var db = Database(marker, withAsyncState ? "state" : null);
        var task = db.StringGetAsync("k");
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = task.ContinueWith(
            static (t, s) => ((TaskCompletionSource<bool>)s!).TrySetResult(t.IsFaulted),
            done,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Assert.True(await done.Task, "the command should have faulted");
    }

    private static IDatabase Database(string message, object? asyncState)
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.TimeoutMilliseconds.Returns(5000);
        return new RespDatabaseContext(new RespContext().WithExecutor(new FaultsLater(message))).AsDatabase(multiplexer, asyncState);
    }

    /// <summary>Faults every send, asynchronously, so the bridge is what sees the fault.</summary>
    private sealed class FaultsLater(string message) : RespExecutorBase
    {
        public override int Database => 0;

        public override RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public override async ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException(message);
        }
    }
}
