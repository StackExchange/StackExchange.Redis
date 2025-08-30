using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using Xunit;
using Xunit.Internal;

namespace RESP.Core.Tests;

[SuppressMessage(
    "Usage",
    "xUnit1031:Do not use blocking task operations in test method",
    Justification = "This isn't actually async; we're testing an awaitable.")]
public class OperationUnitTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManuallyImplementedAsync_NotSent(bool sent)
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        if (sent) remote.OnSent();
        var awaiter = op.GetAwaiter();
        if (sent)
        {
            Assert.False(awaiter.IsCompleted);
        }
        else
        {
            Assert.True(awaiter.IsFaulted);
            var ex = Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
            Assert.Contains("This command has not yet been sent", ex.Message);
        }
    }

    [Fact]
    public void UnsentDetectedSync()
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        var ex = Assert.Throws<InvalidOperationException>(() => op.Wait());
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact]
    public async Task UnsentDetected_Operation_Async()
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op);
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact]
    public async Task UnsentDetected_ValueTask_Async()
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op.AsValueTask());
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact]
    public async Task UnsentDetected_Task_Async()
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op.AsTask());
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact]
    public void CanCreateAndCompleteOperation()
    {
        var op = RespOperation.Create(out var remote, CancellationToken);
        remote.OnSent();

        // initial state
        Assert.False(op.IsCanceled);
        Assert.False(op.IsCompleted);
        Assert.False(op.IsCompletedSuccessfully);
        Assert.False(op.IsFaulted);

        // complete first time
        Assert.True(remote.TrySetResult(default));
        Assert.False(op.IsCanceled);
        Assert.True(op.IsCompleted);
        Assert.True(op.IsCompletedSuccessfully);
        Assert.False(op.IsFaulted);

        // additional completions fail
        Assert.False(remote.TrySetResult(default));
#pragma warning disable xUnit1051
        Assert.False(remote.TrySetCanceled());
#pragma warning restore xUnit1051
        Assert.False(remote.TrySetException(null!));

        // can get result
        op.GetResult();

        // but only once, after that: bad things
        Assert.Throws<NullReferenceException>(() => op.GetResult());
        Assert.Throws<NullReferenceException>(() => op.IsCanceled);
        Assert.Throws<NullReferenceException>(() => op.IsCompleted);
        Assert.Throws<NullReferenceException>(() => op.IsCompletedSuccessfully);
        Assert.Throws<NullReferenceException>(() => op.IsFaulted);

        // additional completions continue to fail
        Assert.False(remote.TrySetResult(default));
#pragma warning disable xUnit1051
        Assert.False(remote.TrySetCanceled());
#pragma warning restore xUnit1051
        Assert.False(remote.TrySetException(null!));
    }

    [Fact]
    public void CanCreateAndCompleteWithoutLeaking()
    {
        int before = RespOperation.DebugPerThreadMessageAllocations;
        for (int i = 0; i < 100; i++)
        {
            var op = RespOperation.Create(out var remote, CancellationToken);
            remote.OnSent();
            remote.TrySetResult(default);
            Assert.True(op.IsCompleted);
            op.Wait();
        }
        int after = RespOperation.DebugPerThreadMessageAllocations;
        var allocs = after - before;
        Debug.Assert(allocs < 2, $"allocations: {allocs}");
    }

    [Fact]
    public async Task CanCreateAndCompleteWithoutLeaking_Async()
    {
        var threadId = Environment.CurrentManagedThreadId;
        int before = RespOperation.DebugPerThreadMessageAllocations;
        for (int i = 0; i < 100; i++)
        {
            var op = RespOperation.Create(out var remote, CancellationToken);
            remote.OnSent();
            remote.TrySetResult(default);
            Assert.True(op.IsCompleted);
            await op;
        }
        int after = RespOperation.DebugPerThreadMessageAllocations;
        var allocs = after - before;
        Debug.Assert(allocs < 2, $"allocations: {allocs}");

        // do not expect thread switch
        Assert.Equal(threadId, Environment.CurrentManagedThreadId);
    }
}
