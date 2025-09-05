using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Xunit;

namespace RESPite.Tests;

[SuppressMessage(
    "Usage",
    "xUnit1031:Do not use blocking task operations in test method",
    Justification = "This isn't actually async; we're testing an awaitable.")]
public class OperationUnitTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ManuallyImplementedAsync_NotSent_Untyped(bool sent, bool @unsafe)
    {
        var op = RespOperation.Create(out var remote, sent, cancellationToken: CancellationToken);
        Assert.Equal(sent, op.IsSent);
        var awaiter = op.GetAwaiter();
        Assert.False(awaiter.IsCompleted, "not completed first IsCompleted check");

        if (@unsafe)
        {
            op.UnsafeOnCompleted(() => { });
        }
        else
        {
            op.OnCompleted(() => { });
        }

        if (sent)
        {
            Assert.False(awaiter.IsCompleted, "incomplete after OnCompleted");
            Assert.True(remote.TrySetResult(default));
            awaiter.GetResult();
        }
        else
        {
            Assert.True(awaiter.IsFaulted, "faulted after OnCompleted");
            Assert.False(remote.TrySetResult(default));
            var ex = Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
            Assert.Contains("This command has not yet been sent", ex.Message);
        }

        Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ManuallyImplementedAsync_NotSent_Typed(bool sent, bool @unsafe)
    {
        var op = RespOperation.Create<bool>(null, out var remote, sent, cancellationToken: CancellationToken);
        Assert.Equal(sent, op.IsSent);
        var awaiter = op.GetAwaiter();
        Assert.False(awaiter.IsCompleted, "not completed first IsCompleted check");

        if (@unsafe)
        {
            op.UnsafeOnCompleted(() => { });
        }
        else
        {
            op.OnCompleted(() => { });
        }

        if (sent)
        {
            Assert.False(awaiter.IsCompleted, "incomplete after OnCompleted");
            Assert.True(remote.TrySetResult(default));
            awaiter.GetResult();
        }
        else
        {
            Assert.True(awaiter.IsFaulted, "faulted after OnCompleted");
            Assert.False(remote.TrySetResult(default));
            var ex = Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
            Assert.Contains("This command has not yet been sent", ex.Message);
        }

        Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ManuallyImplementedAsync_NotSent_Stateful(bool sent, bool @unsafe)
    {
        var op = RespOperation.Create<string, bool>("abc", null, out var remote, sent, CancellationToken);
        Assert.Equal(sent, op.IsSent);
        var awaiter = op.GetAwaiter();
        Assert.False(awaiter.IsCompleted, "not completed first IsCompleted check");

        if (@unsafe)
        {
            op.UnsafeOnCompleted(() => { });
        }
        else
        {
            op.OnCompleted(() => { });
        }

        if (sent)
        {
            Assert.False(awaiter.IsCompleted, "incomplete after OnCompleted");
            Assert.True(remote.TrySetResult(default));
            awaiter.GetResult();
        }
        else
        {
            Assert.True(awaiter.IsFaulted, "faulted after OnCompleted");
            Assert.False(remote.TrySetResult(default));
            var ex = Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
            Assert.Contains("This command has not yet been sent", ex.Message);
        }

        Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
    }

    [Fact(Timeout = 1000)]
    public void UnsentDetectedSync()
    {
        var op = RespOperation.Create(out var remote, false, CancellationToken);
        var ex = Assert.Throws<InvalidOperationException>(() => op.Wait());
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact(Timeout = 1000)]
    public async Task UnsentDetected_Operation_Async()
    {
        var op = RespOperation.Create(out var remote, false, CancellationToken);
        Assert.False(op.IsCompleted);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op);
        Assert.Contains("This command has not yet been sent", ex.Message);
    }

    [Fact(Timeout = 1000)]
    public async Task UnsentNotDetected_ValueTask_Async()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(100);
        var op = RespOperation.Create(out var remote, false, cts.Token);
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () => await op.AsValueTask());
        AssertCT(ex.CancellationToken, cts.Token);
    }

    [Fact]
    public void CoreValueTaskToTaskSupportsCancellation()
    {
        // The purpose of this test is to show that there are some inherent limitations in netfx
        // regarding IVTS:AsTask (compared with modern .NET), specifically:
        // - it manifests as TaskCanceledException instead of OperationCanceledException
        // - the token is not propagated correctly - it comes back as .None
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var ta = new TestAwaitable();
        var task = ta.AsValueTask().AsTask();
        Assert.Equal(TaskStatus.WaitingForActivation, task.Status);
        ta.Cancel(cts.Token);
        Assert.Equal(TaskStatus.Canceled, task.Status);
        // ReSharper disable once MethodSupportsCancellation - this task is not incomplete
#pragma warning disable xUnit1051
        // use awaiter to unroll aggregate exception
#if NETFRAMEWORK
        var ex = Assert.Throws<TaskCanceledException>(() => task.GetAwaiter().GetResult());
#else
        var ex = Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
#endif
#pragma warning restore xUnit1051
        var summary = SummarizeCT(ex.CancellationToken, cts.Token);

#if NETFRAMEWORK // I *wish* this wasn't the case, but: wishes are free
        Assert.Equal(
            CancellationProblems.DefaultToken | CancellationProblems.NotCanceled
            | CancellationProblems.CannotBeCanceled | CancellationProblems.NotExpectedToken,
            summary);
#else
        Assert.Equal(CancellationProblems.None, summary);
#endif
    }

    private class TestAwaitable : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<int> _core;
        public ValueTask AsValueTask() => new(this, _core.Version);
        public void GetResult(short token) => _core.GetResult(token);
        public void Cancel(CancellationToken token) => _core.SetException(new OperationCanceledException(token));
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }

    [Fact(Timeout = 1000)]
    public async Task UnsentNotDetected_Task_Async()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(100);
        var op = RespOperation.Create(out var remote, false, cts.Token);
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await op.AsTask());

        #if NETFRAMEWORK // see CoreValueTaskToTaskSupportsCancellation for more context
        Assert.Equal(CancellationToken.None, ex.CancellationToken);
        #else
        AssertCT(ex.CancellationToken, cts.Token);
        #endif
    }

    [Flags]
    private enum CancellationProblems
    {
        None = 0,
        DefaultToken = 1 << 0,
        NotCanceled = 1 << 1,
        CannotBeCanceled = 1 << 2,
        TestInfrastuctureToken = 1 << 3,
        NotExpectedToken = 1 << 4,
    }

    private static CancellationProblems SummarizeCT(CancellationToken actual, CancellationToken expected)
    {
        CancellationProblems problems = 0;
        if (actual == CancellationToken.None) problems |= CancellationProblems.DefaultToken;
        if (!actual.IsCancellationRequested) problems |= CancellationProblems.NotCanceled;
        if (!actual.CanBeCanceled) problems |= CancellationProblems.CannotBeCanceled;
        if (actual == CancellationToken) problems |= CancellationProblems.TestInfrastuctureToken;
        if (actual != expected) problems |= CancellationProblems.NotExpectedToken;
        return problems;
    }

    private static void AssertCT(CancellationToken actual, CancellationToken expected)
        => Assert.Equal(CancellationProblems.None, SummarizeCT(actual, expected));

    [Fact(Timeout = 1000)]
    public void CanCreateAndCompleteOperation()
    {
        var op = RespOperation.Create(out var remote, cancellationToken: CancellationToken);

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
        Assert.True(remote.IsTokenMatch, "should match before GetResult");
        op.GetResult();
        Assert.False(remote.IsTokenMatch, "should have reset token");

        // but only once, after that: bad things
        Assert.Throws<InvalidOperationException>(() => op.GetResult());
        Assert.Throws<InvalidOperationException>(() => op.IsCanceled);
        Assert.Throws<InvalidOperationException>(() => op.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => op.IsCompletedSuccessfully);
        Assert.Throws<InvalidOperationException>(() => op.IsFaulted);

        // additional completions continue to fail
        Assert.False(remote.TrySetResult(default), "TrySetResult");
        Assert.False(remote.TrySetCanceled(CancellationToken), "TrySetCanceled");
        Assert.False(remote.TrySetException(null!), "TrySetException");
    }

    [Fact(Timeout = 1000)]
    public void CanCreateAndCompleteWithoutLeaking()
    {
        int before = RespOperation.DebugPerThreadMessageAllocations;
        for (int i = 0; i < 100; i++)
        {
            var op = RespOperation.Create(out var remote, cancellationToken: CancellationToken);
            remote.TrySetResult(default);
            Assert.True(op.IsCompleted);
            op.Wait();
        }

        int after = RespOperation.DebugPerThreadMessageAllocations;
        var allocs = after - before;
        Debug.Assert(allocs < 2, $"allocations: {allocs}");
    }

    [Fact(Timeout = 1000)]
    public async Task CanCreateAndCompleteWithoutLeaking_Async()
    {
        var threadId = Environment.CurrentManagedThreadId;
        int before = RespOperation.DebugPerThreadMessageAllocations;
        for (int i = 0; i < 100; i++)
        {
            var op = RespOperation.Create(out var remote, cancellationToken: CancellationToken);
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
